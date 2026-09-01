using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Membership;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// What a refresh is allowed to leave behind when it fails.
/// </summary>
/// <remarks>
/// A collection that is neither what its rule describes nor what it was is worse than one that was
/// never refreshed, because nothing on the server records which of the two it is and the operator
/// has no way to tell a partial write from a rule that changed. Every test here is about a failure
/// rather than about a success, and the fake below exists to reproduce the two ways the server's
/// own calls fail: an add that resolves the whole batch before assigning anything, and a remove
/// that takes items out one at a time.
/// </remarks>
public class MembershipApplierTests
{
    /// <summary>
    /// The guard. Issuing the remove first would leave the collection with items taken out and
    /// none put back, and this is the test that goes red when the two calls in
    /// <see cref="MembershipApplier"/> are swapped.
    /// </summary>
    [Fact]
    public async Task AnAddThatThrowsLeavesTheCollectionWithTheMembershipItStartedWith()
    {
        var held = new[] { Identifier(1), Identifier(2) };
        var arriving = new[] { Identifier(3), Identifier(4), Identifier(5) };
        var diff = MembershipDiff.Between(held, [Identifier(1), .. arriving]);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([.. held, .. arriving]);
        writer.Seed(Collection, held);
        writer.ThrowOnTheAddOf(Collection, position: 3);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, diff)],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(held.OrderBy(id => id), writer.Held(Collection));
        Assert.False(outcomes[0].Succeeded);
        Assert.Empty(outcomes[0].Added);
        Assert.Empty(outcomes[0].Removed);
    }

    /// <summary>
    /// The same case read from the other side. The collection is unchanged above because the
    /// remove was never issued, and a reader should not have to infer that from a membership.
    /// </summary>
    [Fact]
    public async Task AnAddThatThrowsMeansTheRemoveIsNeverIssued()
    {
        var held = new[] { Identifier(1), Identifier(2) };
        var arriving = new[] { Identifier(3), Identifier(4), Identifier(5) };
        var diff = MembershipDiff.Between(held, [Identifier(1), .. arriving]);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([.. held, .. arriving]);
        writer.Seed(Collection, held);
        writer.ThrowOnTheAddOf(Collection, position: 3);

        await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, diff)],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.DoesNotContain(writer.Calls, call => call.StartsWith("remove", StringComparison.Ordinal));
    }

    /// <summary>
    /// The ordering stated directly, so a change that makes the test above pass for some other
    /// reason still has this one to get past.
    /// </summary>
    [Fact]
    public async Task TheAddIsIssuedBeforeTheRemove()
    {
        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([Identifier(1), Identifier(3)]);
        writer.Seed(Collection, [Identifier(1), Identifier(2)]);

        await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([Identifier(1), Identifier(2)], [Identifier(1), Identifier(3)]))],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(
            ["resolve", "add", "remove"],
            writer.Calls.Select(call => call.Split(' ')[0]));
    }

    /// <summary>
    /// A refresh covers several collections and one of them throwing says nothing about the rest.
    /// </summary>
    [Fact]
    public async Task OneCollectionFailingStillLeavesTheOthersApplied()
    {
        var first = Identifier(20);
        var second = Identifier(21);
        var third = Identifier(22);
        var arriving = Identifier(9);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([Identifier(1), arriving]);
        writer.Seed(first, [Identifier(1)]);
        writer.Seed(second, [Identifier(1)]);
        writer.Seed(third, [Identifier(1)]);
        writer.ThrowOnTheAddOf(second, position: 1);

        var diff = MembershipDiff.Between([Identifier(1)], [Identifier(1), arriving]);
        var outcomes = await MembershipApplier.ApplyAsync(
            [
                new CollectionRefresh("first-rule", first, diff),
                new CollectionRefresh("second-rule", second, diff),
                new CollectionRefresh("third-rule", third, diff)
            ],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Contains(arriving, writer.Held(first));
        Assert.DoesNotContain(arriving, writer.Held(second));
        Assert.Contains(arriving, writer.Held(third));
        Assert.True(outcomes[0].Succeeded);
        Assert.False(outcomes[1].Succeeded);
        Assert.True(outcomes[2].Succeeded);
    }

    /// <summary>
    /// A run that reported one verdict for every collection would leave an administrator with a
    /// failure and no way to tell which collection it belonged to.
    /// </summary>
    [Fact]
    public async Task AFailureIsRecordedAgainstItsOwnCollectionWithTheReason()
    {
        var good = Identifier(20);
        var bad = Identifier(21);
        var arriving = Identifier(9);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([arriving]);
        writer.Seed(good, []);
        writer.Seed(bad, []);
        writer.ThrowOnTheAddOf(bad, position: 1);

        var diff = MembershipDiff.Between([], [arriving]);
        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh("good-rule", good, diff), new CollectionRefresh("bad-rule", bad, diff)],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        var failed = Assert.Single(outcomes, outcome => !outcome.Succeeded);
        Assert.Equal(bad, failed.CollectionId);
        Assert.Contains(
            arriving.ToString("D", CultureInfo.InvariantCulture),
            failed.Fault!.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The outcome names the rule the refresh was derived from, on the path where nothing went
    /// wrong.
    /// </summary>
    [Fact]
    public async Task AnOutcomeNamesTheRuleTheRefreshWasDerivedFrom()
    {
        var arriving = Identifier(9);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([arriving]);
        writer.Seed(Collection, []);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh("recently-added-films", Collection, MembershipDiff.Between([], [arriving]))],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal("recently-added-films", outcomes[0].RuleId);
        Assert.Equal(Collection, outcomes[0].CollectionId);
    }

    /// <summary>
    /// And on the path where something did. The fault outcome is built at a second site, so a
    /// change that carries the identity on one of the two and not the other is a rule whose
    /// history has a hole in it exactly where the failures are.
    /// </summary>
    [Fact]
    public async Task AFailedOutcomeNamesItsRuleToo()
    {
        var arriving = Identifier(9);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([arriving]);
        writer.Seed(Collection, []);
        writer.ThrowOnTheAddOf(Collection, position: 1);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh("recently-added-films", Collection, MembershipDiff.Between([], [arriving]))],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.False(outcomes[0].Succeeded);
        Assert.Equal("recently-added-films", outcomes[0].RuleId);
    }

    /// <summary>
    /// The reason the identity is carried at all, stated as the case that loses it. An operator
    /// deleting a collection gets it back under the same mark with a NEW identifier, which
    /// <c>CollectionResolverTests.ARuleWhoseCollectionWasDeletedComesBackUnderTheSameMark</c>
    /// asserts on the resolve. Two runs of one rule either side of that are two runs of one rule,
    /// and a record keyed on the collection reads the second as a rule that has never run.
    /// </summary>
    [Fact]
    public async Task ARuleWhoseCollectionCameBackUnderANewIdentifierIsStillTheSameRule()
    {
        var arriving = Identifier(9);
        var before = Identifier(30);
        var after = Identifier(31);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([arriving]);
        writer.Seed(before, []);
        writer.Seed(after, []);

        var diff = MembershipDiff.Between([], [arriving]);
        var first = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh("recently-added-films", before, diff)],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);
        var second = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh("recently-added-films", after, diff)],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.NotEqual(first[0].CollectionId, second[0].CollectionId);
        Assert.Equal(first[0].RuleId, second[0].RuleId);
    }

    /// <summary>
    /// A run over several collections keeps each outcome under its own rule, in the order the
    /// refreshes were handed over. A record that took the rule from anywhere but the refresh
    /// beside it would put one rule's run in another rule's history.
    /// </summary>
    [Fact]
    public async Task EachOutcomeCarriesTheRuleOfTheRefreshItAnswers()
    {
        var arriving = Identifier(9);
        var first = Identifier(20);
        var second = Identifier(21);
        var third = Identifier(22);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([arriving]);
        writer.Seed(first, []);
        writer.Seed(second, []);
        writer.Seed(third, []);
        writer.ThrowOnTheAddOf(second, position: 1);

        var diff = MembershipDiff.Between([], [arriving]);
        var outcomes = await MembershipApplier.ApplyAsync(
            [
                new CollectionRefresh("first-rule", first, diff),
                new CollectionRefresh("second-rule", second, diff),
                new CollectionRefresh("third-rule", third, diff)
            ],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(
            ["first-rule", "second-rule", "third-rule"],
            outcomes.Select(outcome => outcome.RuleId));
    }

    /// <summary>
    /// An item deleted between the query and the write is an ordinary event on a live server. The
    /// add would throw on it and take the rest of the batch with it, so it is dropped in front of
    /// the write instead.
    /// </summary>
    [Fact]
    public async Task AnItemThatVanishedBetweenTheQueryAndTheWriteIsDroppedRatherThanThrownOn()
    {
        var stays = Identifier(3);
        var vanished = Identifier(4);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([stays]);
        writer.Seed(Collection, []);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([], [stays, vanished]))],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.True(outcomes[0].Succeeded);
        Assert.Equal([vanished], outcomes[0].Dropped);
        Assert.Equal([stays], outcomes[0].Added);
        Assert.Equal([stays], writer.Held(Collection));
    }

    /// <summary>
    /// Where every match has vanished there is nothing to add, and issuing an empty add would be a
    /// call that exists only to be a call.
    /// </summary>
    [Fact]
    public async Task AnAddWhoseItemsHaveAllVanishedIssuesNoAdd()
    {
        var vanished = Identifier(4);

        var writer = new FakeCollectionWriter();
        writer.Seed(Collection, []);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([], [vanished]))],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(["resolve"], writer.Calls.Select(call => call.Split(' ')[0]));
        Assert.Equal([vanished], outcomes[0].Dropped);
        Assert.Empty(outcomes[0].Added);
    }

    /// <summary>
    /// The remove is not re-resolved, and this is the test that says so. A link to a deleted item
    /// is still matched by item id, so a remove that skipped what no longer resolves would leave
    /// that item in the collection for good.
    /// </summary>
    [Fact]
    public async Task AnItemThatVanishedIsStillTakenOutOfTheCollection()
    {
        var vanished = Identifier(4);

        var writer = new FakeCollectionWriter();
        writer.Seed(Collection, [vanished]);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([vanished], []))],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Empty(writer.Held(Collection));
        Assert.Equal([vanished], outcomes[0].Removed);
        Assert.True(outcomes[0].Succeeded);
    }

    /// <summary>
    /// The server's remove saves the item and queues a metadata refresh whether or not it matched
    /// anything, so a refresh that issued it over an empty set would pay a write to change
    /// nothing, which is what deriving a diff was for.
    /// </summary>
    [Fact]
    public async Task ADiffThatChangesNothingIssuesNoCallAtAll()
    {
        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([Identifier(1)]);
        writer.Seed(Collection, [Identifier(1)]);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([Identifier(1)], [Identifier(1)]))],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Empty(writer.Calls);
        Assert.True(outcomes[0].Succeeded);
        Assert.Empty(outcomes[0].Added);
        Assert.Empty(outcomes[0].Removed);
        Assert.Empty(outcomes[0].Dropped);
    }

    /// <summary>
    /// A collection with nothing arriving still has its remove issued, and the resolve in front of
    /// the add is skipped rather than called with an empty list.
    /// </summary>
    [Fact]
    public async Task ACollectionWithNothingArrivingStillHasItsRemoveIssued()
    {
        var writer = new FakeCollectionWriter();
        writer.Seed(Collection, [Identifier(1), Identifier(2)]);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([Identifier(1), Identifier(2)], [Identifier(1)]))],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(["remove"], writer.Calls.Select(call => call.Split(' ')[0]));
        Assert.Equal([Identifier(2)], outcomes[0].Removed);
    }

    /// <summary>
    /// A remove that throws is recorded like any other fault, and what the add already put in
    /// stays put in rather than being reported as work that did not happen.
    /// </summary>
    [Fact]
    public async Task ARemoveThatThrowsIsRecordedAndKeepsWhatTheAddAlreadyDid()
    {
        var arriving = Identifier(3);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([Identifier(1), arriving]);
        writer.Seed(Collection, [Identifier(1), Identifier(2)]);
        writer.ThrowOnTheRemoveOf(Collection);

        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([Identifier(1), Identifier(2)], [Identifier(1), arriving]))],
            writer,
            new CollectionRefreshGate(),
            NullLogger.Instance,
            CancellationToken.None);

        Assert.False(outcomes[0].Succeeded);
        Assert.Equal([arriving], outcomes[0].Added);
        Assert.Empty(outcomes[0].Removed);
        Assert.Contains(arriving, writer.Held(Collection));
    }

    /// <summary>
    /// A cancelled run stops. Recording the cancellation against each collection and carrying on
    /// would issue the remaining writes, which is the opposite of what cancelling asked for.
    /// </summary>
    [Fact]
    public async Task ACancelledRunStopsRatherThanRecordingAFaultPerCollection()
    {
        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([Identifier(3)]);
        writer.Seed(Collection, []);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MembershipApplier.ApplyAsync(
                [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([], [Identifier(3)]))],
                writer,
                new CollectionRefreshGate(),
                NullLogger.Instance,
                cancelled.Token));

        Assert.Empty(writer.Held(Collection));
    }

    /// <summary>
    /// The same rule one step later, and the two are not the same test. The run above is refused
    /// before it starts, at the gate, so nothing in the applier ever sees the token. This one is
    /// cancelled while it is already writing, which is the shape a server shutting down has, and
    /// it is the one that reaches the applier's own decision to stop rather than to record a fault
    /// against the collection and carry on to the next.
    /// </summary>
    [Fact]
    public async Task ARunCancelledWhileItIsWritingStopsRatherThanRecordingAFault()
    {
        var first = Identifier(20);
        var second = Identifier(21);
        var arriving = Identifier(3);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([arriving]);
        writer.Seed(first, []);
        writer.Seed(second, []);

        using var shutdown = new CancellationTokenSource();
        writer.CancelWhenTheAddStarts(shutdown);

        var diff = MembershipDiff.Between([], [arriving]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MembershipApplier.ApplyAsync(
                [new CollectionRefresh("first-rule", first, diff), new CollectionRefresh("second-rule", second, diff)],
                writer,
                new CollectionRefreshGate(),
                NullLogger.Instance,
                shutdown.Token));

        Assert.Empty(writer.Held(first));
        Assert.Empty(writer.Held(second));
        Assert.DoesNotContain(writer.Calls, call => call.StartsWith("remove", StringComparison.Ordinal));
    }

    /// <summary>
    /// Nothing here treats a missing argument as an empty one, for the same reason the diff does
    /// not: a caller that lost its list should learn about it here rather than read a run that
    /// changed nothing as a collection that needed nothing.
    /// </summary>
    [Fact]
    public async Task ARunOverNothingIsRefusedRatherThanTreatedAsEmpty()
    {
        var writer = new FakeCollectionWriter();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => MembershipApplier.ApplyAsync(null!, writer, new CollectionRefreshGate(), NullLogger.Instance, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => MembershipApplier.ApplyAsync([], null!, new CollectionRefreshGate(), NullLogger.Instance, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => MembershipApplier.ApplyAsync([], writer, null!, NullLogger.Instance, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => MembershipApplier.ApplyAsync([], writer, new CollectionRefreshGate(), null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => new CollectionRefresh(Rule, Collection, null!));
        Assert.Throws<ArgumentNullException>(
            () => new CollectionRefresh(null!, Collection, MembershipDiff.Between([], [])));
    }


    /// <summary>
    /// The rule this issue states in its own words: a run that changed nothing does not log at
    /// information level. Deleting the emptiness test in <c>Report</c> makes this red.
    /// </summary>
    /// <remarks>
    /// The collection already holds what the rule describes, so the diff is empty in both
    /// directions and neither the add nor the remove is issued. An operator running fifty rules
    /// against a library that did not move gets fifty of these, which is why the level matters
    /// more here than the text.
    /// </remarks>
    [Fact]
    public async Task ARefreshThatChangedNothingWritesNoInformationLine()
    {
        var held = new[] { Identifier(1), Identifier(2) };
        var writer = new FakeCollectionWriter();
        writer.PutInLibrary(held);
        writer.Seed(Collection, held);

        var logger = new RecordingLogger();
        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between(held, held))],
            writer,
            new CollectionRefreshGate(),
            logger,
            CancellationToken.None);

        Assert.True(outcomes[0].Succeeded);
        Assert.Empty(outcomes[0].Added);
        Assert.Empty(outcomes[0].Removed);
        Assert.Empty(logger.At(LogLevel.Information));
        Assert.Single(logger.At(LogLevel.Debug));
        Assert.Contains(Rule, logger.At(LogLevel.Debug)[0].Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other direction, so a change that silenced the applier altogether would still be red
    /// somewhere. A collection whose membership moved is worth one information-level line.
    /// </summary>
    [Fact]
    public async Task ACollectionWhoseMembershipMovedIsOneInformationLine()
    {
        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([Identifier(1), Identifier(3)]);
        writer.Seed(Collection, [Identifier(1), Identifier(2)]);

        var logger = new RecordingLogger();
        await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([Identifier(1), Identifier(2)], [Identifier(1), Identifier(3)]))],
            writer,
            new CollectionRefreshGate(),
            logger,
            CancellationToken.None);

        var information = Assert.Single(logger.At(LogLevel.Information));
        Assert.Contains(Rule, information.Message, StringComparison.Ordinal);
        Assert.Contains("1 added", information.Message, StringComparison.Ordinal);
        Assert.Contains("1 removed", information.Message, StringComparison.Ordinal);
        Assert.Empty(logger.At(LogLevel.Error));
        Assert.Empty(logger.At(LogLevel.Warning));
    }

    /// <summary>
    /// An apply that failed is an error line, and it carries what threw rather than only saying
    /// that something did.
    /// </summary>
    /// <remarks>
    /// The exception is handed to the logger rather than rendered into the message, because the
    /// surface reporting it decides how much of it to show and a message that flattened it to a
    /// sentence cannot get the stack back.
    /// </remarks>
    [Fact]
    public async Task AnApplyThatFailedIsOneErrorLineCarryingWhatThrew()
    {
        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([Identifier(1), Identifier(3)]);
        writer.Seed(Collection, [Identifier(1)]);
        writer.ThrowOnTheAddOf(Collection, position: 1);

        var logger = new RecordingLogger();
        var outcomes = await MembershipApplier.ApplyAsync(
            [new CollectionRefresh(Rule, Collection, MembershipDiff.Between([Identifier(1)], [Identifier(1), Identifier(3)]))],
            writer,
            new CollectionRefreshGate(),
            logger,
            CancellationToken.None);

        Assert.False(outcomes[0].Succeeded);
        var error = Assert.Single(logger.At(LogLevel.Error));
        Assert.Contains(Rule, error.Message, StringComparison.Ordinal);
        Assert.Same(outcomes[0].Fault, error.Exception);
        Assert.Empty(logger.At(LogLevel.Information));
    }

    /// <summary>
    /// The clause about grepping for one collection, held over a run whose three collections end
    /// in the three different states rather than over one of them at a time.
    /// </summary>
    [Fact]
    public async Task EveryLineARefreshWritesNamesTheRuleItIsAbout()
    {
        var moved = Identifier(30);
        var quiet = Identifier(31);
        var failing = Identifier(32);
        var arriving = Identifier(9);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([Identifier(1), arriving]);
        writer.Seed(moved, [Identifier(1)]);
        writer.Seed(quiet, [Identifier(1)]);
        writer.Seed(failing, [Identifier(1)]);
        writer.ThrowOnTheAddOf(failing, position: 1);

        var growing = MembershipDiff.Between([Identifier(1)], [Identifier(1), arriving]);
        var unchanged = MembershipDiff.Between([Identifier(1)], [Identifier(1)]);

        var logger = new RecordingLogger();
        await MembershipApplier.ApplyAsync(
            [
                new CollectionRefresh("moved-rule", moved, growing),
                new CollectionRefresh("quiet-rule", quiet, unchanged),
                new CollectionRefresh("failing-rule", failing, growing)
            ],
            writer,
            new CollectionRefreshGate(),
            logger,
            CancellationToken.None);

        Assert.Equal(3, logger.Lines.Count);
        Assert.Equal(
            [LogLevel.Information, LogLevel.Debug, LogLevel.Error],
            logger.Lines.Select(line => line.Level));
        Assert.Equal(
            ["moved-rule", "quiet-rule", "failing-rule"],
            logger.Lines.Select(line => line.Message.Split(' ')[1]));
    }

    /// <summary>
    /// Nothing logged carries an item, which is this issue's disclosure rule rather than a
    /// preference about verbosity.
    /// </summary>
    /// <remarks>
    /// An item identifier resolves to a title and a path on the server it came from, so a line
    /// carrying one turns a log an administrator pastes into a bug report into a partial listing
    /// of their library. What the applier writes instead is how many moved. The collection
    /// identifier is named on purpose and is not an item: it is what an operator opens.
    /// </remarks>
    [Fact]
    public async Task NoLineARefreshWritesNamesAnItemItMoved()
    {
        var arriving = Identifier(3);
        var leaving = Identifier(2);
        var vanished = Identifier(4);

        var writer = new FakeCollectionWriter();
        writer.PutInLibrary([Identifier(1), arriving]);
        writer.Seed(Collection, [Identifier(1), leaving]);

        var logger = new RecordingLogger();
        await MembershipApplier.ApplyAsync(
            [
                new CollectionRefresh(
                    Rule,
                    Collection,
                    MembershipDiff.Between([Identifier(1), leaving], [Identifier(1), arriving, vanished]))
            ],
            writer,
            new CollectionRefreshGate(),
            logger,
            CancellationToken.None);

        foreach (var line in logger.Lines)
        {
            foreach (var item in new[] { Identifier(1), arriving, leaving, vanished })
            {
                Assert.DoesNotContain(
                    item.ToString("D", CultureInfo.InvariantCulture),
                    line.Message,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Contains(
            Collection.ToString("D", CultureInfo.InvariantCulture),
            logger.At(LogLevel.Information)[0].Message,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The rule the single-collection cases here are refreshing. It is a rule identity rather than
    /// a collection name, because that is what an outcome is keyed on.
    /// </summary>
    private const string Rule = "a-rule";

    private static readonly Guid Collection = Identifier(99);

    /// <summary>
    /// The same derivation the diff's own tests use, so an identifier in a failure here can be
    /// read against one there.
    /// </summary>
    private static Guid Identifier(int index)
        => new(
            0x5C011EC7,
            (short)(index * 7),
            (short)(index * 3),
            [(byte)(index * 11), (byte)index, 0x5D, 0x1F, 0xF0, 0x00, 0x00, (byte)(index * 5)]);

    /// <summary>
    /// A stand-in for the two collection calls, reproducing how each of them fails.
    /// </summary>
    private sealed class FakeCollectionWriter : ICollectionMembershipWriter
    {
        private readonly Dictionary<Guid, HashSet<Guid>> _collections = [];
        private readonly HashSet<Guid> _library = [];
        private readonly List<string> _calls = [];
        private readonly Dictionary<Guid, int> _addThrows = [];
        private readonly HashSet<Guid> _removeThrows = [];
        private CancellationTokenSource? _cancelOnAdd;

        public IReadOnlyList<string> Calls => _calls;

        public void PutInLibrary(IEnumerable<Guid> itemIds) => _library.UnionWith(itemIds);

        public void Seed(Guid collectionId, IEnumerable<Guid> itemIds)
            => _collections[collectionId] = [.. itemIds];

        public void ThrowOnTheAddOf(Guid collectionId, int position)
            => _addThrows[collectionId] = position;

        public void ThrowOnTheRemoveOf(Guid collectionId) => _removeThrows.Add(collectionId);

        /// <summary>
        /// Cancels the run at the moment the add begins, which is the shape a server shutting down
        /// has: the token is live when the refresh starts and is cancelled while it writes.
        /// </summary>
        public void CancelWhenTheAddStarts(CancellationTokenSource cancelling) => _cancelOnAdd = cancelling;

        public IReadOnlyList<Guid> Held(Guid collectionId)
            => [.. _collections[collectionId].Order()];

        public IReadOnlyList<Guid> ItemsThatStillResolve(IReadOnlyList<Guid> itemIds)
        {
            _calls.Add("resolve " + Join(itemIds));
            return [.. itemIds.Where(_library.Contains)];
        }

        public Task AddToCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            _calls.Add("add " + Join(itemIds));
            _cancelOnAdd?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();

            // The server resolves every identifier in the batch before it assigns anything, so a
            // throw part way through leaves the collection untouched. A fake that added as it went
            // would prove the applier against a server that does not exist.
            var resolved = new List<Guid>();
            foreach (var id in itemIds)
            {
                resolved.Add(id);
                if (_addThrows.TryGetValue(collectionId, out var position) && resolved.Count == position)
                {
                    throw new ArgumentException(
                        "No item exists with the supplied Id " + id.ToString("D", CultureInfo.InvariantCulture));
                }
            }

            _collections[collectionId].UnionWith(resolved);
            return Task.CompletedTask;
        }

        public Task RemoveFromCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            _calls.Add("remove " + Join(itemIds));
            cancellationToken.ThrowIfCancellationRequested();

            if (_removeThrows.Contains(collectionId))
            {
                throw new ArgumentException(
                    "No collection exists with the supplied collectionId "
                    + collectionId.ToString("D", CultureInfo.InvariantCulture));
            }

            _collections[collectionId].ExceptWith(itemIds);
            return Task.CompletedTask;
        }

        private static string Join(IReadOnlyList<Guid> itemIds)
            => string.Join(",", itemIds.Select(id => id.ToString("D", CultureInfo.InvariantCulture)));
    }
}
