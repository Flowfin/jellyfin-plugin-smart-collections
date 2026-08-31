using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Membership;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// What two refreshes arriving at one collection at the same time are allowed to do to it.
/// </summary>
/// <remarks>
/// The failure these tests are about is not a crash. Two runs whose writes interleave both finish,
/// both report success, and the collection they leave behind holds a membership neither of their
/// rules describes. Nothing on the server records that it happened, so the only place it can be
/// refused is here.
///
/// Every test drives the real applier rather than the gate alone wherever the property is about a
/// refresh, because a gate a caller has to remember to wrap around the writes is a convention and
/// the point of this issue is that it is not one.
///
/// The waits are bounded rather than open. A gate that stopped excluding turns these into a fast
/// failed assertion, and a gate that excluded too much turns them into a timeout, so neither
/// direction hangs a suite.
/// </remarks>
public class CollectionRefreshGateTests
{
    /// <summary>
    /// How long a thing that should happen is given before the suite calls it a failure. Generous,
    /// because it is only ever paid when a test is already failing.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a thing that should not happen is watched for. A refresh held at the gate cannot
    /// reach the writer however long it is given, so this bounds the cost of the test rather than
    /// its confidence.
    /// </summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The guard. Two refreshes of one collection have to write one after the other, and this is
    /// the test that goes red when the gate is taken out of the applier.
    /// </summary>
    [Fact]
    public async Task TwoRefreshesOfOneCollectionDoNotInterleaveTheirWrites()
    {
        var writer = new HeldWriter();
        writer.CallItThis(First, "first");
        var gate = new CollectionRefreshGate();

        var holder = Task.Run(() => Apply(First, writer, gate, CancellationToken.None));
        await writer.EnteredAtLeast(1).WaitAsync(Patience);

        var second = Task.Run(() => Apply(First, writer, gate, CancellationToken.None));

        // Queued rather than merely slower, and the difference is the whole property: the second
        // refresh may not reach the writer while the first is inside it, however long it is given.
        await AssertDoesNotHappen(writer.EnteredAtLeast(2));

        writer.LetGo();
        await Task.WhenAll(holder, second).WaitAsync(Patience);

        Assert.Equal(1, writer.MostInsideAtOnce);
        Assert.Equal(["enter first", "leave first", "enter first", "leave first"], writer.Log);
    }

    /// <summary>
    /// The cost the design refuses to pay. One lock over the plugin would hold this property too
    /// and would make every collection on a large library wait for every other one.
    /// </summary>
    [Fact]
    public async Task TwoRefreshesOfDifferentCollectionsWriteAtTheSameTime()
    {
        var writer = new HeldWriter();
        writer.CallItThis(First, "first");
        writer.CallItThis(Second, "second");
        var gate = new CollectionRefreshGate();

        var one = Task.Run(() => Apply(First, writer, gate, CancellationToken.None));
        var two = Task.Run(() => Apply(Second, writer, gate, CancellationToken.None));

        // Both inside the writer at once. Neither can leave until the other has arrived, so a gate
        // that serialised these would not be slow here, it would never finish.
        await writer.EnteredAtLeast(2).WaitAsync(Patience);

        writer.LetGo();
        await Task.WhenAll(one, two).WaitAsync(Patience);

        Assert.Equal(2, writer.MostInsideAtOnce);
    }

    /// <summary>
    /// The near miss. Moving the wait inside the applier's try block is a one-line mistake that
    /// leaves every test above green: the release in the finally then hands back a permit the
    /// cancelled caller never held, and the refresh behind it starts while the one in front is
    /// still writing.
    /// </summary>
    [Fact]
    public async Task AWaiterCancelledWhileItQueuesDoesNotReleaseTheRefreshInFrontOfIt()
    {
        var writer = new HeldWriter();
        writer.CallItThis(First, "first");
        var gate = new CollectionRefreshGate();

        var holder = Task.Run(() => Apply(First, writer, gate, CancellationToken.None));
        await writer.EnteredAtLeast(1).WaitAsync(Patience);

        using var givesUp = new CancellationTokenSource();
        var waiter = Task.Run(() => Apply(First, writer, gate, givesUp.Token));
        await givesUp.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);

        var third = Task.Run(() => Apply(First, writer, gate, CancellationToken.None));
        await AssertDoesNotHappen(writer.EnteredAtLeast(2));

        writer.LetGo();
        await Task.WhenAll(holder, third).WaitAsync(Patience);

        Assert.Equal(1, writer.MostInsideAtOnce);
    }

    /// <summary>
    /// The server cancels a scheduled task when it shuts down. A refresh that took the gate down
    /// with it would leave that collection unrefreshable until the server was restarted.
    /// </summary>
    [Fact]
    public async Task ARefreshCancelledWhileItWritesLeavesTheGateFree()
    {
        var gate = new CollectionRefreshGate();
        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shutdown = new CancellationTokenSource();

        var cancelled = gate.ApplyExclusivelyAsync(
            First,
            async token =>
            {
                inside.SetResult();
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return 0;
            },
            shutdown.Token);

        await inside.Task.WaitAsync(Patience);
        await shutdown.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        var next = gate.ApplyExclusivelyAsync(First, _ => Task.FromResult(1), CancellationToken.None);
        Assert.Equal(1, await next.WaitAsync(Patience));
    }

    /// <summary>
    /// A refresh that failed has stopped touching the collection, so the gate it held has to come
    /// back whether it returned or threw.
    /// </summary>
    [Fact]
    public async Task ARefreshThatThrowsLeavesTheGateFree()
    {
        var gate = new CollectionRefreshGate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.ApplyExclusivelyAsync<int>(
                First,
                _ => throw new InvalidOperationException("the write failed"),
                CancellationToken.None));

        var next = gate.ApplyExclusivelyAsync(First, _ => Task.FromResult(1), CancellationToken.None);
        Assert.Equal(1, await next.WaitAsync(Patience));
    }

    /// <summary>
    /// A caller that lost the work it meant to run should learn about it here rather than take a
    /// gate, hold it for nothing and report a refresh that did nothing.
    /// </summary>
    [Fact]
    public async Task ARefreshWithNoWorkIsRefusedRatherThanTreatedAsNothingToDo()
    {
        var gate = new CollectionRefreshGate();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => gate.ApplyExclusivelyAsync<int>(First, null!, CancellationToken.None));

        var next = gate.ApplyExclusivelyAsync(First, _ => Task.FromResult(1), CancellationToken.None);
        Assert.Equal(1, await next.WaitAsync(Patience));
    }

    private static readonly Guid First = Identifier(41);
    private static readonly Guid Second = Identifier(42);
    private static readonly Guid Arriving = Identifier(7);

    /// <summary>
    /// One collection getting one item, which is the smallest refresh that issues a write.
    /// </summary>
    private static Task<IReadOnlyList<CollectionRefreshOutcome>> Apply(
        Guid collectionId,
        ICollectionMembershipWriter writer,
        CollectionRefreshGate gate,
        CancellationToken cancellationToken)
        => MembershipApplier.ApplyAsync(
            [new CollectionRefresh("a-rule", collectionId, MembershipDiff.Between([], [Arriving]))],
            writer,
            gate,
            cancellationToken);

    /// <summary>
    /// Watches something that must not happen for a bounded time. The thing being watched cannot
    /// happen later than this if it did not happen inside it, because a refresh held at the gate
    /// is not waiting on a clock.
    /// </summary>
    private static async Task AssertDoesNotHappen(Task shouldNotComplete)
    {
        var finished = await Task.WhenAny(shouldNotComplete, Task.Delay(Grace));

        Assert.True(
            !ReferenceEquals(shouldNotComplete, finished),
            "A second refresh of one collection got inside the writer while the first was still "
            + "in there. The two can interleave their adds and removes from here, and the "
            + "membership they leave behind is neither of the two the rules describe.");
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
    /// A writer that stays inside its add until the test lets it out, and counts how many refreshes
    /// were inside it at once.
    /// </summary>
    /// <remarks>
    /// The count is what the property is actually about. A log of calls says what order things
    /// happened in; only a high-water mark says whether two of them were ever in there together.
    /// </remarks>
    private sealed class HeldWriter : ICollectionMembershipWriter
    {
        private readonly object _sync = new();
        private readonly List<string> _log = [];
        private readonly List<TaskCompletionSource> _arrivals = [];
        private readonly Dictionary<Guid, string> _names = [];
        private readonly TaskCompletionSource _mayLeave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inside;
        private int _mostInsideAtOnce;
        private int _entered;

        public IReadOnlyList<string> Log
        {
            get
            {
                lock (_sync)
                {
                    return [.. _log];
                }
            }
        }

        public int MostInsideAtOnce
        {
            get
            {
                lock (_sync)
                {
                    return _mostInsideAtOnce;
                }
            }
        }

        public void CallItThis(Guid collectionId, string name)
        {
            lock (_sync)
            {
                _names[collectionId] = name;
            }
        }

        /// <summary>
        /// A task that completes once that many refreshes have got inside the add.
        /// </summary>
        public Task EnteredAtLeast(int count)
        {
            lock (_sync)
            {
                Arrival(count);

                return _arrivals[count - 1].Task;
            }
        }

        public void LetGo() => _mayLeave.TrySetResult();

        public IReadOnlyList<Guid> ItemsThatStillResolve(IReadOnlyList<Guid> itemIds) => itemIds;

        public async Task AddToCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            Enter(collectionId);

            try
            {
                await _mayLeave.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Leave(collectionId);
            }
        }

        public Task RemoveFromCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
        {
            Enter(collectionId);
            Leave(collectionId);

            return Task.CompletedTask;
        }

        private void Enter(Guid collectionId)
        {
            lock (_sync)
            {
                _log.Add("enter " + _names[collectionId]);
                _inside++;
                _entered++;

                if (_inside > _mostInsideAtOnce)
                {
                    _mostInsideAtOnce = _inside;
                }

                Arrival(_entered).TrySetResult();
            }
        }

        private void Leave(Guid collectionId)
        {
            lock (_sync)
            {
                _inside--;
                _log.Add("leave " + _names[collectionId]);
            }
        }

        /// <summary>
        /// The signal for the given arrival, created on demand so a test may ask for one that has
        /// not happened yet. Called under the lock.
        /// </summary>
        private TaskCompletionSource Arrival(int count)
        {
            while (_arrivals.Count < count)
            {
                _arrivals.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            }

            return _arrivals[count - 1];
        }
    }
}
