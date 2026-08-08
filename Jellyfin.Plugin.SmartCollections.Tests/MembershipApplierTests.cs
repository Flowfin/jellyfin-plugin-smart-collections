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
            [new CollectionRefresh(Collection, diff)],
            writer,
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
            [new CollectionRefresh(Collection, diff)],
            writer,
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
            [new CollectionRefresh(Collection, MembershipDiff.Between([Identifier(1), Identifier(2)], [Identifier(1), Identifier(3)]))],
            writer,
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
                new CollectionRefresh(first, diff),
                new CollectionRefresh(second, diff),
                new CollectionRefresh(third, diff)
            ],
            writer,
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
            [new CollectionRefresh(good, diff), new CollectionRefresh(bad, diff)],
            writer,
            CancellationToken.None);

        var failed = Assert.Single(outcomes, outcome => !outcome.Succeeded);
        Assert.Equal(bad, failed.CollectionId);
        Assert.Contains(
            arriving.ToString("D", CultureInfo.InvariantCulture),
            failed.Fault!.Message,
            StringComparison.Ordinal);
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
            [new CollectionRefresh(Collection, MembershipDiff.Between([], [stays, vanished]))],
            writer,
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
            [new CollectionRefresh(Collection, MembershipDiff.Between([], [vanished]))],
            writer,
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
            [new CollectionRefresh(Collection, MembershipDiff.Between([vanished], []))],
            writer,
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
            [new CollectionRefresh(Collection, MembershipDiff.Between([Identifier(1)], [Identifier(1)]))],
            writer,
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
            [new CollectionRefresh(Collection, MembershipDiff.Between([Identifier(1), Identifier(2)], [Identifier(1)]))],
            writer,
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
            [new CollectionRefresh(Collection, MembershipDiff.Between([Identifier(1), Identifier(2)], [Identifier(1), arriving]))],
            writer,
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
                [new CollectionRefresh(Collection, MembershipDiff.Between([], [Identifier(3)]))],
                writer,
                cancelled.Token));

        Assert.Empty(writer.Held(Collection));
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
            () => MembershipApplier.ApplyAsync(null!, writer, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => MembershipApplier.ApplyAsync([], null!, CancellationToken.None));
        Assert.Throws<ArgumentNullException>(() => new CollectionRefresh(Collection, null!));
    }

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

        public IReadOnlyList<string> Calls => _calls;

        public void PutInLibrary(IEnumerable<Guid> itemIds) => _library.UnionWith(itemIds);

        public void Seed(Guid collectionId, IEnumerable<Guid> itemIds)
            => _collections[collectionId] = [.. itemIds];

        public void ThrowOnTheAddOf(Guid collectionId, int position)
            => _addThrows[collectionId] = position;

        public void ThrowOnTheRemoveOf(Guid collectionId) => _removeThrows.Add(collectionId);

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
