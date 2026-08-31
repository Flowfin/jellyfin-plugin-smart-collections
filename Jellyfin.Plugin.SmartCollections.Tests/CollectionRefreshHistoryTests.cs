using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Membership;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// What a rule's record has to survive, and what it deliberately does not hold.
/// </summary>
/// <remarks>
/// The failure this record exists against is a collection that looks stale: one bad run and a rule
/// that has failed every run since it was written are the same thing from outside the plugin. Every
/// test here is about telling those two apart, or about the one way the telling breaks - a history
/// keyed on the collection rather than on the rule.
///
/// The outcomes are built by running the applier rather than by constructing them, because the
/// constructor is internal to the engine and because an outcome assembled by a test is a shape
/// somebody agreed with rather than the one the applier produces.
/// </remarks>
public class CollectionRefreshHistoryTests
{
    /// <summary>
    /// A rule document an operator added a minute ago has no runs, which is an ordinary state of
    /// the directory rather than a caller's mistake.
    /// </summary>
    [Fact]
    public void ARuleThatHasNeverRunHasNoLastResultAndAnEmptyHistory()
    {
        var history = new CollectionRefreshHistory();

        Assert.Null(history.Last("recently-added-films"));
        Assert.Empty(history.For("recently-added-films"));
        Assert.Empty(history.Rules);
    }

    /// <summary>
    /// The last result is the most recent run and not the first one recorded.
    /// </summary>
    [Fact]
    public async Task TheLastResultIsTheMostRecentRun()
    {
        var history = new CollectionRefreshHistory();
        history.Record(await Run("recently-added-films", Identifier(1), fails: true));
        history.Record(await Run("recently-added-films", Identifier(1), fails: false));

        Assert.True(history.Last("recently-added-films")!.Succeeded);
    }

    /// <summary>
    /// The order is newest first, so the head is the last result and reading down is reading back
    /// in time.
    /// </summary>
    [Fact]
    public async Task TheHistoryIsNewestFirst()
    {
        var history = new CollectionRefreshHistory();
        history.Record(await Run("recently-added-films", Identifier(1), fails: true));
        history.Record(await Run("recently-added-films", Identifier(2), fails: true));
        history.Record(await Run("recently-added-films", Identifier(3), fails: false));

        Assert.Equal(
            [Identifier(3), Identifier(2), Identifier(1)],
            history.For("recently-added-films").Select(outcome => outcome.CollectionId));
    }

    /// <summary>
    /// The reason this record exists. Four runs and the last three failing is a rule that is
    /// failing; the same four with only the last one failing is a rule that failed once, and the
    /// last result alone cannot tell them apart.
    /// </summary>
    [Fact]
    public async Task ARuleFailingEveryRunIsDistinguishableFromOneThatFailedOnce()
    {
        var failing = new CollectionRefreshHistory();
        var unlucky = new CollectionRefreshHistory();
        foreach (var fails in new[] { true, true, true })
        {
            failing.Record(await Run("failing", Identifier(1), fails));
        }

        foreach (var fails in new[] { false, false, true })
        {
            unlucky.Record(await Run("unlucky", Identifier(1), fails));
        }

        Assert.False(failing.Last("failing")!.Succeeded);
        Assert.False(unlucky.Last("unlucky")!.Succeeded);
        Assert.Equal(3, failing.For("failing").Count(outcome => !outcome.Succeeded));
        Assert.Equal(1, unlucky.For("unlucky").Count(outcome => !outcome.Succeeded));
    }

    /// <summary>
    /// The bound. Every run of every rule kept for the life of a server nobody restarts is growth
    /// with no ceiling, and the question this answers is answered by the last few.
    /// </summary>
    [Fact]
    public async Task OnlyTheLastFewRunsAreKeptAndTheOldestGoesFirst()
    {
        var history = new CollectionRefreshHistory(depth: 3);
        for (var run = 1; run <= 5; run++)
        {
            history.Record(await Run("recently-added-films", Identifier(run), fails: false));
        }

        Assert.Equal(
            [Identifier(5), Identifier(4), Identifier(3)],
            history.For("recently-added-films").Select(outcome => outcome.CollectionId));
    }

    /// <summary>
    /// The trap this record is keyed against. An operator who deletes a collection gets it back
    /// under the same mark with a NEW identifier, which
    /// <c>CollectionResolverTests.ARuleWhoseCollectionWasDeletedComesBackUnderTheSameMark</c>
    /// asserts on the resolve, so a table keyed on the collection would start a fresh history at
    /// exactly the moment an operator was repairing something and lose the failures that led to
    /// the deletion.
    /// </summary>
    [Fact]
    public async Task ARuleWhoseCollectionWasDeletedKeepsTheRunsFromBeforeTheDeletion()
    {
        var before = Identifier(30);
        var after = Identifier(31);

        var history = new CollectionRefreshHistory();
        history.Record(await Run("recently-added-films", before, fails: true));
        history.Record(await Run("recently-added-films", after, fails: false));

        Assert.Equal(["recently-added-films"], history.Rules);
        Assert.Equal(2, history.For("recently-added-films").Count);
        Assert.Contains(history.For("recently-added-films"), outcome => outcome.CollectionId == before);
    }

    /// <summary>
    /// A run covers several collections and is several rules' runs, never one.
    /// </summary>
    [Fact]
    public async Task ARunOverSeveralRulesIsRecordedUnderEachOfThem()
    {
        var history = new CollectionRefreshHistory();
        history.Record(await RunTogether(
            ("films-of-the-eighties", Identifier(20), false),
            ("one-studio", Identifier(21), true),
            ("unwatched", Identifier(22), false)));

        Assert.Equal(["films-of-the-eighties", "one-studio", "unwatched"], history.Rules);
        Assert.True(history.Last("films-of-the-eighties")!.Succeeded);
        Assert.False(history.Last("one-studio")!.Succeeded);
        Assert.True(history.Last("unwatched")!.Succeeded);
    }

    /// <summary>
    /// The rule list is ordered rather than handed out in whatever order the table was filled in.
    /// A page rendering it twice with nothing changed in between produces the same list both
    /// times, and determinism is what this plugin claims.
    /// </summary>
    [Fact]
    public async Task TheRuleListIsOrderedRatherThanInTheOrderTheRunsArrived()
    {
        var history = new CollectionRefreshHistory();
        foreach (var ruleId in new[] { "unwatched", "films-of-the-eighties", "one-studio" })
        {
            history.Record(await Run(ruleId, Identifier(1), fails: false));
        }

        Assert.Equal(["films-of-the-eighties", "one-studio", "unwatched"], history.Rules);
    }

    /// <summary>
    /// The table is written from several threads: a run walks its collections and a server may
    /// drive more than one run at once. Without the lock this loses entries rather than failing.
    /// </summary>
    [Fact]
    public async Task RunsArrivingTogetherAreAllKept()
    {
        var history = new CollectionRefreshHistory(depth: 200);
        var outcomes = new List<CollectionRefreshOutcome>();
        for (var run = 1; run <= 100; run++)
        {
            outcomes.Add((await Run("recently-added-films", Identifier(run), fails: false))[0]);
        }

        await Task.WhenAll(outcomes.Select(outcome => Task.Run(() => history.Record(outcome))));

        Assert.Equal(100, history.For("recently-added-films").Count);
        Assert.Equal(
            outcomes.Select(outcome => outcome.CollectionId).Order(),
            history.For("recently-added-films").Select(outcome => outcome.CollectionId).Order());
    }

    /// <summary>
    /// A history reading and a history being written to at the same time is the page's ordinary
    /// case, so what a reader gets is a copy rather than the list a recorder is inserting into.
    /// </summary>
    [Fact]
    public async Task WhatAReaderGetsIsNotTheListTheRecorderWritesTo()
    {
        var history = new CollectionRefreshHistory();
        history.Record(await Run("recently-added-films", Identifier(1), fails: false));

        var taken = history.For("recently-added-films");
        history.Record(await Run("recently-added-films", Identifier(2), fails: false));

        Assert.Single(taken);
        Assert.Equal(2, history.For("recently-added-films").Count);
    }

    /// <summary>
    /// A depth of zero is not a shorter history. It is a caller that meant to keep the last result
    /// and got a record answering nothing, which reads as a rule that has never run.
    /// </summary>
    [Fact]
    public void ADepthThatKeepsNothingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionRefreshHistory(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollectionRefreshHistory(-1));
        Assert.Equal(CollectionRefreshHistory.DefaultDepth, new CollectionRefreshHistory().Depth);
        Assert.Equal(3, new CollectionRefreshHistory(3).Depth);
    }

    /// <summary>
    /// Nothing here treats a missing argument as an empty one, for the same reason the applier
    /// does not.
    /// </summary>
    [Fact]
    public void AMissingArgumentIsRefusedRatherThanTreatedAsEmpty()
    {
        var history = new CollectionRefreshHistory();

        Assert.Throws<ArgumentNullException>(() => history.Record((CollectionRefreshOutcome)null!));
        Assert.Throws<ArgumentNullException>(() => history.Record((IReadOnlyList<CollectionRefreshOutcome>)null!));
        Assert.Throws<ArgumentNullException>(() => history.Last(null!));
        Assert.Throws<ArgumentNullException>(() => history.For(null!));
    }

    /// <summary>
    /// One rule refreshing one collection, run through the applier so the outcome is the one the
    /// applier builds rather than one a test agreed with.
    /// </summary>
    private static Task<IReadOnlyList<CollectionRefreshOutcome>> Run(string ruleId, Guid collectionId, bool fails)
        => RunTogether((ruleId, collectionId, fails));

    /// <summary>
    /// One run over several rules, each with its own collection and its own verdict.
    /// </summary>
    private static async Task<IReadOnlyList<CollectionRefreshOutcome>> RunTogether(
        params (string RuleId, Guid CollectionId, bool Fails)[] rules)
    {
        var arriving = Identifier(9);
        var writer = new FakeCollectionWriter(arriving);
        var refreshes = new List<CollectionRefresh>(rules.Length);
        foreach (var (ruleId, collectionId, fails) in rules)
        {
            if (fails)
            {
                writer.ThrowOnTheAddOf(collectionId);
            }

            refreshes.Add(new CollectionRefresh(ruleId, collectionId, MembershipDiff.Between([], [arriving])));
        }

        return await MembershipApplier.ApplyAsync(
            refreshes,
            writer,
            new CollectionRefreshGate(),
            CancellationToken.None);
    }

    /// <summary>
    /// The same derivation the applier's own tests use, so an identifier in a failure here can be
    /// read against one there.
    /// </summary>
    private static Guid Identifier(int index)
        => new(
            0x5C011EC7,
            (short)(index * 7),
            (short)(index * 3),
            [(byte)(index * 11), (byte)index, 0x5D, 0x1F, 0xF0, 0x00, 0x00, (byte)(index * 5)]);

    /// <summary>
    /// The smallest writer these tests need: one item in the library, and an add that throws for
    /// the collections a test names.
    /// </summary>
    private sealed class FakeCollectionWriter(Guid inLibrary) : ICollectionMembershipWriter
    {
        private readonly HashSet<Guid> _throwOnAdd = [];

        public void ThrowOnTheAddOf(Guid collectionId) => _throwOnAdd.Add(collectionId);

        public IReadOnlyList<Guid> ItemsThatStillResolve(IReadOnlyList<Guid> itemIds)
            => [.. itemIds.Where(id => id == inLibrary)];

        public Task AddToCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
            => _throwOnAdd.Contains(collectionId)
                ? throw new ArgumentException(
                    "No collection exists with the supplied collectionId "
                    + collectionId.ToString("D", CultureInfo.InvariantCulture))
                : Task.CompletedTask;

        public Task RemoveFromCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
