using System;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The instant an evaluation runs at, followed from the argument to both places a rule can read
/// it: the query the compiler builds, and the comparison the stage after the query makes.
/// </summary>
/// <remarks>
/// A rule saying "added in the last thirty days" is the single largest threat to a determinism
/// claim, because the obvious way to write one reads the wall clock. The engine reads none -
/// <c>ambient-clock-in-the-engine</c> in the invariant lint refuses it by name over the engine
/// assembly - and the instant arrives as an argument instead. What that leaves to assert is the
/// half a lint cannot see: that the one instant handed in is the one every relative condition
/// answers against, and that pinning it pins the answer.
///
/// The first two rules here put their relative conditions under an <c>anyOf</c> on purpose. A
/// condition reachable from the root through <c>allOf</c> groups is pushed into the server's query
/// and read as satisfied afterwards, and the fake this suite runs against answers with its whole
/// list whatever query it is handed, so a pushed condition would filter nothing here. Under a
/// disjunction the condition is compared by the stage after the query instead, which is the arm
/// that reads the instant. The third rule is the deliberate exception and is where the query's own
/// half is read.
/// </remarks>
public class RuleEvaluationClockTests
{
    /// <summary>
    /// A rule whose only condition is relative, held under a disjunction so the comparison rather
    /// than the query answers it.
    /// </summary>
    private const string AddedRecently = """
        {
            "schemaVersion": 1,
            "id": "added-recently",
            "name": "Added recently",
            "collects": ["movie"],
            "match": {
                "anyOf": [
                    { "field": "dateAdded", "operator": "withinLast", "value": "P30D" }
                ]
            }
        }
        """;

    /// <summary>
    /// A rule with two relative conditions over two fields, with one span, so an item sitting
    /// exactly at the boundary satisfies both only where both were measured from one instant.
    /// </summary>
    private const string TwoRelativeConditions = """
        {
            "schemaVersion": 1,
            "id": "two-relative-conditions",
            "name": "Two relative conditions",
            "collects": ["movie"],
            "match": {
                "anyOf": [
                    {
                        "allOf": [
                            { "field": "dateAdded", "operator": "withinLast", "value": "P30D" },
                            { "field": "premiereDate", "operator": "withinLast", "value": "P30D" }
                        ]
                    }
                ]
            }
        }
        """;

    /// <summary>
    /// The same pair reachable through <c>allOf</c>, which splits them across the two stages: the
    /// release date compiles into the query and the added date does not, so one instant has to
    /// reach both a query bound and a comparison.
    /// </summary>
    /// <remarks>
    /// Which of the two compiles is the compile table's business rather than this file's, and
    /// <c>docs/rule-queries.md</c> is where it is written down. What matters here is that the pair
    /// is split, because that is what makes this case say something the two above do not.
    /// </remarks>
    private const string TwoRelativeConditionsPushed = """
        {
            "schemaVersion": 1,
            "id": "two-relative-conditions-pushed",
            "name": "Two relative conditions pushed",
            "collects": ["movie"],
            "match": {
                "allOf": [
                    { "field": "dateAdded", "operator": "withinLast", "value": "P30D" },
                    { "field": "premiereDate", "operator": "withinLast", "value": "P30D" }
                ]
            }
        }
        """;

    /// <summary>
    /// The done condition this test carries: a pinned instant makes a relative rule answer with a
    /// fixed set.
    /// </summary>
    /// <remarks>
    /// The second instant is what stops this passing over an engine that ignored the argument
    /// entirely. A rule read against a clock nobody pinned would answer the same way here as one
    /// read against the argument, because both runs happen within a second of each other; moving
    /// the argument a year and watching the answer move is what separates the two.
    /// </remarks>
    [Fact]
    public void APinnedInstantMakesARelativeRuleAnswerAFixedSet()
    {
        var source = new FakeRuleItemSource();
        var recent = Added(1, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        var older = Added(2, new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        source.Put(recent);
        source.Put(older);

        var atTheStartOf2026 = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var aYearEarlier = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal([recent.Id], Evaluate(AddedRecently, source, atTheStartOf2026));
        Assert.Equal([recent.Id], Evaluate(AddedRecently, source, atTheStartOf2026));
        Assert.Equal([older.Id], Evaluate(AddedRecently, source, aYearEarlier));
    }

    /// <summary>
    /// The done condition this test carries: two relative conditions in one rule see the same
    /// instant, asserted through the comparison the stage after the query makes.
    /// </summary>
    /// <remarks>
    /// The item sits exactly at the far end of both spans, and the span a <c>withinLast</c> names
    /// is closed at both ends, so it is collected where both conditions were measured from one
    /// instant and dropped where either was measured from a clock read a moment later. A test
    /// putting the item comfortably inside both spans would pass over an engine reading two
    /// clocks, because two instants a microsecond apart both contain it.
    /// </remarks>
    [Fact]
    public void TwoRelativeConditionsInOneRuleSeeOneInstant()
    {
        var given = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var boundary = given.AddDays(-30).UtcDateTime;

        var source = new FakeRuleItemSource();
        var atTheBoundary = new Movie
        {
            Id = Id(1),
            Name = "At the boundary",
            DateCreated = boundary,
            PremiereDate = boundary
        };
        source.Put(atTheBoundary);

        Assert.Equal([atTheBoundary.Id], Evaluate(TwoRelativeConditions, source, given));
    }

    /// <summary>
    /// The same property across the two stages: the bound the compiler wrote into the query and
    /// the comparison the stage after it made are one instant rather than two clock reads.
    /// </summary>
    /// <remarks>
    /// Asserted against the argument rather than against each other, so a compiler that read one
    /// clock once and used it for everything would still be caught. Both ends of the span are
    /// read, because the span is closed at both ends and a ceiling taken from a second clock read
    /// is the same fault as a floor taken from one. The item sits exactly at the far end of the
    /// span the comparison answers, so the assertion that it was collected is the sharp one for
    /// that half.
    /// </remarks>
    [Fact]
    public void OneInstantReachesTheQueryAndTheComparisonAfterIt()
    {
        var given = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var boundary = given.AddDays(-30).UtcDateTime;

        var source = new FakeRuleItemSource();
        var atTheBoundary = new Movie
        {
            Id = Id(1),
            Name = "At the boundary",
            DateCreated = boundary,
            PremiereDate = boundary
        };
        source.Put(atTheBoundary);

        Assert.Equal([atTheBoundary.Id], Evaluate(TwoRelativeConditionsPushed, source, given));

        var query = QuerySnapshot.Of(Assert.Single(source.Asked));

        Assert.Equal(boundary.ToString(CultureInfo.InvariantCulture), query["MinPremiereDate"]);
        Assert.Equal(given.UtcDateTime.ToString(CultureInfo.InvariantCulture), query["MaxPremiereDate"]);
    }

    private static Guid Id(int seed)
        => Guid.Parse(seed.ToString("D8", CultureInfo.InvariantCulture) + "-3333-3333-3333-333333333333");

    private static Movie Added(int seed, DateTime added)
        => new()
        {
            Id = Id(seed),
            Name = "Film " + seed.ToString(CultureInfo.InvariantCulture),
            DateCreated = added
        };

    private static Guid[] Evaluate(string document, FakeRuleItemSource source, DateTimeOffset given)
    {
        var validation = RuleDocumentValidator.Read(document);

        Assert.True(
            validation.IsValid,
            string.Join("; ", validation.Errors.Select(error => error.ToString())));

        var evaluation = RuleEvaluator.Evaluate(validation.Document!, source, given);

        Assert.True(
            evaluation.IsAccepted,
            string.Join("; ", evaluation.Errors.Select(error => error.ToString())));
        Assert.Equal(given, evaluation.EvaluatedAt);

        return [.. evaluation.ItemIds];
    }
}
