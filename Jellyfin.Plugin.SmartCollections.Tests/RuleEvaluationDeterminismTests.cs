using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The two tests that catch a rule engine whose answer depends on the order it happened to read
/// its input in.
/// </summary>
/// <remarks>
/// The cheapest determinism failure to write and the hardest to notice is one that depends on the
/// order a collection was enumerated in: a hash set iterated directly, a dictionary walked without
/// sorting, a parallel loop collecting into a list. Each produces a correct SET and an arbitrary
/// ORDER, and each looks fine in a test that ran once.
///
/// The two tests answer different halves of that. Repeating the evaluation catches an order that
/// varies with insertion or with a hash seed inside one process. Shuffling the input catches an
/// engine that is passing through whatever order the repository gave it, which the repeat test
/// cannot see, because a passed-through order is perfectly stable as long as the input is.
///
/// Neither needs a display, a server or an elevated right. The whole evaluation runs against
/// <see cref="FakeRuleItemSource"/>, which is a list.
/// </remarks>
public class RuleEvaluationDeterminismTests
{
    /// <summary>
    /// The instant the evaluations here are given, fixed for the reason every other evaluation
    /// test fixes one: a relative condition compiled against a clock asserts something different
    /// every day.
    /// </summary>
    private static readonly DateTimeOffset Given = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// How many times the evaluation is repeated. Enough to expose an order that varies with
    /// insertion, and cheap enough to sit in the ordinary suite.
    /// </summary>
    private const int Repeats = 100;

    /// <summary>
    /// A rule with a condition on both sides of the query boundary, so both the items the server
    /// selected and the items the post-query stage kept are in the answer's order.
    /// </summary>
    /// <remarks>
    /// A rule made only of pushed conditions would order a list the fake handed over untouched,
    /// and a rule made only of post-query conditions would order a list this plugin built. Mixing
    /// them is what puts both paths in one ordering.
    /// </remarks>
    private const string Mixed = """
        {
            "schemaVersion": 1,
            "id": "determinism",
            "name": "Determinism",
            "collects": ["movie"],
            "match": {
                "allOf": [
                    { "field": "officialRating", "operator": "equals", "value": "PG-13" },
                    { "field": "overview", "operator": "contains", "value": "heist" }
                ]
            }
        }
        """;

    /// <summary>
    /// The first half: the same rule against the same library, evaluated a hundred times in one
    /// process, produces the same ordered identifier list every time.
    /// </summary>
    [Fact]
    public void AHundredEvaluationsOfOneRuleOverOneLibraryProduceOneOrderedList()
    {
        var first = Evaluate(Library(false, null));

        Assert.NotEmpty(first);

        for (var run = 2; run <= Repeats; run++)
        {
            var again = Evaluate(Library(false, null));

            Assert.True(
                first.SequenceEqual(again),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Run {run} of {Repeats} answered {Render(again)} where run 1 answered {Render(first)}."));
        }
    }

    /// <summary>
    /// The second half: the fake answers in a different order on every call, and the output does
    /// not move.
    /// </summary>
    /// <param name="seed">The seed the shuffle runs from.</param>
    /// <remarks>
    /// THE SEED IS DECLARED RATHER THAN DRAWN, and that is the one place this test departs from
    /// the shape its issue describes. A seed drawn per run would make the suite answer a different
    /// question on every run, which is the arrangement #200 measured on the mutation gate and is
    /// the failure mode this milestone exists against: a suite that is itself irreproducible
    /// cannot hold anything else to reproducibility. Four declared seeds give four fixed orders
    /// that are none of them the fill order, and the seed is still printed on failure, so replaying
    /// a failing case is the same one-line act the issue asks for.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(20260905)]
    [InlineData(int.MaxValue)]
    public void ShufflingTheLibraryOnEveryCallMovesNothingAboutTheOutput(int seed)
    {
        var expected = Evaluate(Library(false, null));
        var shuffled = Library(false, seed);

        for (var run = 1; run <= Repeats; run++)
        {
            var answer = Evaluate(shuffled);

            Assert.True(
                expected.SequenceEqual(answer),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Seed {seed}, call {run}: the shuffled library answered {Render(answer)} where the "
                    + $"unshuffled one answered {Render(expected)}. Replay with that seed."));
        }
    }

    /// <summary>
    /// The shuffle has to actually shuffle, or the test above passes over a fake that answers in
    /// the fill order every time and proves nothing.
    /// </summary>
    /// <param name="seed">The seed the shuffle runs from.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(20260905)]
    [InlineData(int.MaxValue)]
    public void TheShuffleAnswersADifferentOrderOnEveryCall(int seed)
    {
        var source = Library(false, seed);
        var query = new MediaBrowser.Controller.Entities.InternalItemsQuery();
        var orders = new List<string>();

        for (var call = 0; call < 5; call++)
        {
            orders.Add(string.Join(
                ",",
                source.Select(query).Select(item => item.Id.ToString("N", CultureInfo.InvariantCulture))));
        }

        Assert.Equal(orders.Count, orders.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The reversed arrangement beside the shuffled one, because a fixed order that is not the
    /// fill order is the cheapest thing a repository can hand back and the two catch different
    /// mistakes.
    /// </summary>
    [Fact]
    public void ALibraryAnsweringInReverseAnswersTheSameOrderedList()
    {
        Assert.True(Evaluate(Library(false, null)).SequenceEqual(Evaluate(Library(true, null))));
    }

    /// <summary>
    /// The library the two tests run over: enough items for an order to be visible, a mix of items
    /// the rule collects and items it does not, and identifiers deliberately out of step with the
    /// fill order so a passed-through order and an ordered one are different lists.
    /// </summary>
    /// <param name="reversed">Whether the source answers in the reverse of the fill order.</param>
    /// <param name="seed">The seed to shuffle with, or <see langword="null"/> not to.</param>
    /// <returns>The source.</returns>
    private static FakeRuleItemSource Library(bool reversed, int? seed)
    {
        var source = new FakeRuleItemSource { AnswersInReverse = reversed, Seed = seed };

        for (var index = 0; index < 40; index++)
        {
            // The identifier counts down while the fill order counts up, so the ordered answer is
            // the reverse of the order the fake was filled in. A step that returned the items in
            // the order it read them would produce a list this test can tell apart from the right
            // one, which a fill order that already agreed with the identifiers would not.
            var id = string.Create(CultureInfo.InvariantCulture, $"{40 - index:D8}-1111-1111-1111-111111111111");

            source.Put(new Movie
            {
                Id = Guid.Parse(id),
                Name = string.Create(CultureInfo.InvariantCulture, $"Film {index}"),
                OfficialRating = index % 3 == 0 ? "R" : "PG-13",
                Overview = index % 2 == 0
                    ? "A heist in three acts."
                    : "A quiet film about bread."
            });
        }

        return source;
    }

    /// <summary>
    /// Runs the rule once against a source.
    /// </summary>
    /// <param name="source">The library.</param>
    /// <returns>The ordered identifiers.</returns>
    private static IReadOnlyList<Guid> Evaluate(FakeRuleItemSource source)
    {
        var validation = RuleDocumentValidator.Read(Mixed);

        Assert.True(validation.IsValid);

        var evaluation = RuleEvaluator.Evaluate(validation.Document!, source, Given);

        Assert.True(evaluation.IsAccepted);

        return evaluation.ItemIds;
    }

    /// <summary>
    /// An answer, short enough to read in a failure message.
    /// </summary>
    /// <param name="ids">The identifiers.</param>
    /// <returns>The rendering.</returns>
    private static string Render(IReadOnlyList<Guid> ids)
        => "[" + string.Join(", ", ids.Select(id => id.ToString("N", CultureInfo.InvariantCulture)[..8])) + "]";
}
