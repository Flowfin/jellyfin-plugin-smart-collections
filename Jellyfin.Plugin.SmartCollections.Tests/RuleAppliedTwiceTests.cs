using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Membership;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// One rule applied twice against one library, end to end: evaluate, diff, write, and again.
/// </summary>
/// <remarks>
/// The done condition #39 carries in its last clause, and it is deliberately not a test over the
/// applier alone. That one exists - a diff that changes nothing issues no call - and it starts
/// from a diff somebody handed it. What this asserts is that the SECOND EVALUATION produces a diff
/// that changes nothing, which is a statement about the whole chain: an evaluation whose answer
/// moved between two identical runs, or an order that moved under it, would produce a diff with
/// work in it and the applier would faithfully write it.
///
/// A rule that rewrote its collection on every refresh is the failure this rules out, and it is
/// not hypothetical for a plugin of this shape: the collection is written from a list, and a list
/// whose order depends on what the server answered first is a different list every run.
/// </remarks>
public class RuleAppliedTwiceTests
{
    private static readonly DateTimeOffset Given = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid Collection = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private const string Rule = "every-film";

    /// <summary>
    /// A rule with a declared order and a cap, so what is repeated is the whole of what #39 adds
    /// rather than the identifier order the step already had.
    /// </summary>
    private const string Document = """
        {
            "schemaVersion": 1,
            "id": "every-film",
            "name": "Every film",
            "collects": ["movie"],
            "limit": 4,
            "sort": [ { "field": "productionYear", "direction": "descending" } ],
            "match": { "allOf": [ { "field": "overview", "operator": "contains", "value": "heist" } ] }
        }
        """;

    /// <summary>
    /// The done condition this test carries: applying the same rule twice produces no writes the
    /// second time.
    /// </summary>
    [Fact]
    public async Task ASecondApplicationOfOneRuleWritesNothing()
    {
        var library = Library(reversed: false);
        var writer = new CountingWriter();

        var first = await ApplyAsync(library, writer);

        Assert.NotEmpty(first.Added);
        Assert.NotEmpty(writer.Calls);

        writer.Forget();

        var second = await ApplyAsync(Library(reversed: true), writer);

        Assert.Empty(second.Added);
        Assert.Empty(second.Removed);
        Assert.Empty(writer.Calls);
    }

    /// <summary>
    /// The cap is part of what repeats. A rule that took its first four items from a different
    /// place on the second run would collect a different set, so the diff would carry both an add
    /// and a remove rather than nothing.
    /// </summary>
    [Fact]
    public async Task TheCapTakesTheSameItemsOnBothRuns()
    {
        var writer = new CountingWriter();
        var first = await ApplyAsync(Library(reversed: false), writer);

        Assert.Equal(4, writer.Held(Collection).Count);
        Assert.Equal(4, first.Added.Count);

        writer.Forget();
        var second = await ApplyAsync(Library(reversed: true), writer);

        Assert.Empty(second.Added);
        Assert.Empty(second.Removed);
        Assert.Equal(4, writer.Held(Collection).Count);
    }

    private static async Task<CollectionRefreshOutcome> ApplyAsync(FakeRuleItemSource library, CountingWriter writer)
    {
        var validation = RuleDocumentValidator.Read(Document);
        Assert.True(validation.IsValid);

        var evaluation = RuleEvaluator.Evaluate(validation.Document!, library, Given);
        Assert.True(evaluation.IsAccepted);

        writer.PutInLibrary(evaluation.ItemIds);

        var diff = MembershipDiff.Between(writer.Held(Collection), evaluation.ItemIds);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, diff)],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.True(outcome.Succeeded);

        return outcome;
    }

    /// <summary>
    /// A library of films the rule collects and films it does not, with identifiers out of step
    /// with the fill order so a passed-through order is a different list from an ordered one.
    /// </summary>
    /// <param name="reversed">Whether the source answers in the reverse of the fill order.</param>
    /// <returns>The source.</returns>
    private static FakeRuleItemSource Library(bool reversed)
    {
        var source = new FakeRuleItemSource { AnswersInReverse = reversed };

        for (var index = 0; index < 10; index++)
        {
            source.Put(new Movie
            {
                Id = Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"{10 - index:D8}-2222-2222-2222-222222222222")),
                Name = string.Create(CultureInfo.InvariantCulture, $"Film {index}"),

                // Two films share each year, so the cap cuts through a tie and the tie-break is
                // what decides which of the two is inside it.
                ProductionYear = 1990 + (index / 2),
                Overview = index % 3 == 0
                    ? "A quiet film about bread."
                    : "A heist in three acts."
            });
        }

        return source;
    }

    /// <summary>
    /// A collection this plugin writes to, which remembers what it holds and every call it was
    /// asked to make.
    /// </summary>
    private sealed class CountingWriter : ICollectionMembershipWriter
    {
        private readonly Dictionary<Guid, List<Guid>> _held = [];

        private readonly HashSet<Guid> _library = [];

        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls => _calls;

        public void PutInLibrary(IEnumerable<Guid> itemIds) => _library.UnionWith(itemIds);

        public void Forget() => _calls.Clear();

        public IReadOnlyList<Guid> Held(Guid collectionId)
            => _held.TryGetValue(collectionId, out var items) ? items : [];

        public IReadOnlyList<Guid> ItemsThatStillResolve(IReadOnlyList<Guid> itemIds)
        {
            _calls.Add("resolve");
            return itemIds.Where(_library.Contains).ToArray();
        }

        public IReadOnlyList<Guid> ItemsInCollection(Guid collectionId)
        {
            _calls.Add("read");
            return Held(collectionId);
        }

        public Task AddToCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            _calls.Add("add");

            if (!_held.TryGetValue(collectionId, out var items))
            {
                items = [];
                _held.Add(collectionId, items);
            }

            items.AddRange(itemIds);
            return Task.CompletedTask;
        }

        public Task RemoveFromCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            _calls.Add("remove");

            if (_held.TryGetValue(collectionId, out var items))
            {
                items.RemoveAll(itemIds.Contains);
            }

            return Task.CompletedTask;
        }
    }
}
