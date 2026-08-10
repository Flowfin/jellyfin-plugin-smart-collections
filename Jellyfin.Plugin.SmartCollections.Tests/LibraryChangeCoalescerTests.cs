using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Library;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// One evaluation per burst, and never none.
/// </summary>
/// <remarks>
/// Two failures sit either side of this type and both are quiet ones. Evaluating per event turns a
/// scan importing two thousand episodes into two thousand full evaluations on the thread doing the
/// import, and nothing reports it except a server that has become slow. Waiting for the stream to
/// go quiet and nothing else means a library that keeps changing is never evaluated at all, and
/// nothing reports that either, because a collection that is merely out of date looks exactly like
/// a collection that is correct.
///
/// Every case here drives a clock the test owns. Nothing sleeps and nothing waits on a real
/// interval, so the thirty seconds and five minutes under test cost microseconds and the results
/// do not move on a loaded machine.
/// </remarks>
public class LibraryChangeCoalescerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Maximum = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The headline of this issue. Two thousand events arriving inside one quiet period are one
    /// evaluation, not two thousand and not two.
    /// </summary>
    [Fact]
    public async Task ABurstOfTwoThousandEventsInsideTheQuietPeriodIsExactlyOneEvaluation()
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        // Two thousand items, one every millisecond, so the whole burst spans two seconds and sits
        // well inside both the quiet period and the maximum wait.
        for (var i = 0; i < 2000; i++)
        {
            coalescer.ItemChanged(ItemId(i), LibraryChangeKind.Added);
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(0, coalescer.BatchesClosed);
        Assert.Equal(2000, coalescer.PendingChanges);

        clock.Advance(Quiet);
        await coalescer.WaitForDispatchAsync();

        Assert.Equal(1, coalescer.BatchesClosed);
        var batch = Assert.Single(sink.Batches);
        Assert.Equal(2000, batch.Changes.Count);
        Assert.Equal(LibraryChangeBatchReason.StreamWentQuiet, batch.Reason);
        Assert.Equal(0, coalescer.PendingChanges);
    }

    /// <summary>
    /// The other half of the same rule. Without the maximum wait, a stream that keeps producing
    /// changes pushes the evaluation out for as long as it lasts and the collections never move.
    /// </summary>
    [Fact]
    public async Task AnUnendingStreamIsEvaluatedAtTheMaximumRatherThanNever()
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        // One change every fifteen seconds. That is inside the thirty-second quiet period every
        // time, so the quiet period alone would re-arm for ever and close nothing.
        coalescer.ItemChanged(ItemId(0), LibraryChangeKind.Added);
        for (var step = 1; step <= 25; step++)
        {
            clock.Advance(TimeSpan.FromSeconds(15));
            coalescer.ItemChanged(ItemId(step), LibraryChangeKind.Added);
        }

        await coalescer.WaitForDispatchAsync();

        // Six minutes and fifteen seconds of unbroken stream: one evaluation, at the maximum.
        Assert.Equal(1, coalescer.BatchesClosed);
        var batch = Assert.Single(sink.Batches);
        Assert.Equal(LibraryChangeBatchReason.MaximumWaitReached, batch.Reason);
        Assert.Equal(Start, batch.FirstChangeAt);
        Assert.Equal(Start + Maximum, batch.SettledAt);

        // Everything raised in the first five minutes, and nothing raised after it.
        Assert.Equal(20, batch.Changes.Count);
    }

    [Fact]
    public async Task NothingIsEvaluatedUntilTheQuietPeriodHasFullyPassed()
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        coalescer.ItemChanged(ItemId(0), LibraryChangeKind.Added);

        clock.Advance(Quiet - TimeSpan.FromMilliseconds(1));
        await coalescer.WaitForDispatchAsync();

        Assert.Equal(0, coalescer.BatchesClosed);
        Assert.Empty(sink.Batches);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await coalescer.WaitForDispatchAsync();

        Assert.Equal(1, coalescer.BatchesClosed);
        Assert.Equal(Start + Quiet, Assert.Single(sink.Batches).SettledAt);
    }

    [Fact]
    public async Task ASecondBurstAfterTheFirstHasSettledIsASecondEvaluation()
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        coalescer.ItemChanged(ItemId(0), LibraryChangeKind.Added);
        clock.Advance(Quiet);

        clock.Advance(TimeSpan.FromHours(1));
        coalescer.ItemChanged(ItemId(1), LibraryChangeKind.Removed);
        clock.Advance(Quiet);
        await coalescer.WaitForDispatchAsync();

        Assert.Equal(2, coalescer.BatchesClosed);
        Assert.Equal(2, sink.Batches.Count);
        Assert.All(sink.Batches, batch => Assert.Equal(LibraryChangeBatchReason.StreamWentQuiet, batch.Reason));
    }

    /// <summary>
    /// One entry per item however many events it arrived on, holding the last kind. An item added
    /// and then removed inside one burst has, by the time anything evaluates, gone.
    /// </summary>
    [Fact]
    public async Task AnItemChangingSeveralTimesInOneBurstIsOneEntryHoldingItsLastKind()
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        var item = ItemId(7);
        coalescer.ItemChanged(item, LibraryChangeKind.Added);
        coalescer.ItemChanged(item, LibraryChangeKind.Updated);
        coalescer.ItemChanged(item, LibraryChangeKind.Removed);

        clock.Advance(Quiet);
        await coalescer.WaitForDispatchAsync();

        var change = Assert.Single(Assert.Single(sink.Batches).Changes);
        Assert.Equal(item, change.ItemId);
        Assert.Equal(LibraryChangeKind.Removed, change.Kind);
    }

    /// <summary>
    /// The batch is a function of what changed and not of the order the server happened to raise
    /// it in. Two runs over the same items in opposite orders produce the same batch.
    /// </summary>
    [Fact]
    public async Task TheOrderOfABatchDoesNotFollowTheOrderTheEventsArrivedIn()
    {
        var items = Enumerable.Range(0, 8).Select(ItemId).ToArray();

        var forwards = await BatchFrom(items);
        var backwards = await BatchFrom(items.Reverse().ToArray());

        Assert.Equal(forwards, backwards);

        // And it is not simply arrival order preserved, which would make the line above pass for
        // the wrong reason if both runs had been in the same order.
        Assert.NotEqual(items.Reverse().ToArray(), backwards);
    }

    /// <summary>
    /// The shutdown clause. The coalescer is what is registered as an observer, so this is the
    /// path a real library event travels, and the handlers have to come back off at the end of it.
    /// </summary>
    [Fact]
    public async Task StoppingTheSubscriptionLeavesNoHandlerAttachedAndNothingMoreReachesTheCoalescer()
    {
        var clock = new ManualTimeProvider(Start);
        var library = new FakeLibraryChangeSource();
        using var coalescer = new LibraryChangeCoalescer([], clock, Quiet, Maximum);
        var subscription = new LibraryChangeSubscription(library, [coalescer]);

        await subscription.StartAsync(CancellationToken.None);
        Assert.Equal(3, library.AttachedHandlers);

        library.RaiseItemAdded(ItemId(1));
        Assert.Equal(1, coalescer.PendingChanges);

        await subscription.StopAsync(CancellationToken.None);

        Assert.Equal(0, library.AttachedHandlers);

        library.RaiseItemAdded(ItemId(2));
        library.RaiseItemUpdated(ItemId(3));
        library.RaiseItemRemoved(ItemId(4));

        Assert.Equal(1, coalescer.PendingChanges);
    }

    /// <summary>
    /// A sink that throws is counted here and nowhere else. The server catches and logs what a
    /// library event handler throws, and this runs after that handler has returned, so a failing
    /// evaluation would otherwise look exactly like a plugin with nothing to do.
    /// </summary>
    [Fact]
    public async Task ASinkThatThrowsIsCountedAndTheSinkBehindItStillRuns()
    {
        var clock = new ManualTimeProvider(Start);
        var throwing = new ThrowingSink();
        var behind = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([throwing, behind], clock, Quiet, Maximum);

        coalescer.ItemChanged(ItemId(0), LibraryChangeKind.Added);
        clock.Advance(Quiet);
        await coalescer.WaitForDispatchAsync();

        Assert.Equal(1, coalescer.SinkFaults);
        Assert.Single(behind.Batches);
        Assert.Equal(1, coalescer.BatchesClosed);
    }

    /// <summary>
    /// Being called is not the same as being due. A callback that arrived early has to re-arm and
    /// leave the burst open; closing it would hand an evaluation half a scan and then hand it the
    /// other half a moment later, which is the double evaluation this whole type exists against.
    /// </summary>
    [Fact]
    public async Task ATimerCallbackThatArrivesEarlyReArmsRatherThanClosingTheBurst()
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        coalescer.ItemChanged(ItemId(0), LibraryChangeKind.Added);

        clock.Advance(Quiet - TimeSpan.FromSeconds(5));
        clock.FireEveryTimerNow();
        await coalescer.WaitForDispatchAsync();

        Assert.Equal(0, coalescer.BatchesClosed);
        Assert.Equal(1, coalescer.PendingChanges);

        // And it re-armed rather than giving up: the burst still closes at its own due time.
        clock.Advance(TimeSpan.FromSeconds(5));
        await coalescer.WaitForDispatchAsync();

        Assert.Equal(1, coalescer.BatchesClosed);
        Assert.Equal(Start + Quiet, Assert.Single(sink.Batches).SettledAt);
    }

    [Fact]
    public async Task ATimerCallbackWithNothingWaitingClosesNoBatch()
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        clock.FireEveryTimerNow();
        await coalescer.WaitForDispatchAsync();

        Assert.Equal(0, coalescer.BatchesClosed);
        Assert.Empty(sink.Batches);
    }

    /// <summary>
    /// The clamp. A timer that ran late leaves the clock past a due time nothing acted on, and the
    /// next change then asks for a delay in the past. A negative delay is not something a timer
    /// accepts, and the throw would land inside a library event handler whose exceptions the
    /// server swallows, so the change would be lost with nothing anywhere reporting it.
    /// </summary>
    [Fact]
    public async Task AChangeArrivingAfterTheMaximumHasPassedIsDueAtOnceRatherThanInThePast()
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        coalescer.ItemChanged(ItemId(0), LibraryChangeKind.Added);

        // Six minutes in which the timer never ran, so the burst is a minute past the maximum and
        // still open.
        clock.AdvanceWithoutFiring(Maximum + TimeSpan.FromMinutes(1));

        coalescer.ItemChanged(ItemId(1), LibraryChangeKind.Updated);

        clock.Advance(TimeSpan.Zero);
        await coalescer.WaitForDispatchAsync();

        Assert.Equal(1, coalescer.BatchesClosed);
        var batch = Assert.Single(sink.Batches);
        Assert.Equal(LibraryChangeBatchReason.MaximumWaitReached, batch.Reason);
        Assert.Equal(2, batch.Changes.Count);
    }

    [Fact]
    public void NothingIsRecordedAfterDisposal()
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();

        // The two Dispose calls below are what this test is about, and the declaration is still
        // a using: an assertion failing between them would otherwise leave the timer and the
        // cancellation source alive for the rest of the process.
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        coalescer.ItemChanged(ItemId(0), LibraryChangeKind.Added);
        coalescer.Dispose();

        Assert.Equal(0, coalescer.PendingChanges);

        coalescer.ItemChanged(ItemId(1), LibraryChangeKind.Added);
        clock.Advance(Maximum + Quiet);

        Assert.Equal(0, coalescer.PendingChanges);
        Assert.Equal(0, coalescer.BatchesClosed);
        Assert.Empty(sink.Batches);

        // Disposing twice is what a container does when a scope ends inside a shutdown that is
        // already disposing singletons, and it must not throw on the second call.
        coalescer.Dispose();
    }

    /// <summary>
    /// A maximum below the quiet period would close every burst at the maximum, so the quiet
    /// period would decide nothing while still reading like coalescing.
    /// </summary>
    [Fact]
    public void IntervalsThatWouldMakeTheQuietPeriodDecideNothingAreRefused()
    {
        var clock = new ManualTimeProvider(Start);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryChangeCoalescer([], clock, TimeSpan.Zero, Maximum));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryChangeCoalescer([], clock, TimeSpan.FromSeconds(-1), Maximum));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LibraryChangeCoalescer([], clock, Quiet, Quiet - TimeSpan.FromMilliseconds(1)));

        Assert.Throws<ArgumentNullException>(
            () => new LibraryChangeCoalescer(null!, clock, Quiet, Maximum));
        Assert.Throws<ArgumentNullException>(
            () => new LibraryChangeCoalescer([], null!, Quiet, Maximum));
    }

    /// <summary>
    /// The defaults are what the plugin runs on, so they are asserted rather than left to a
    /// reader. A maximum at or below the quiet period would be refused by the constructor above.
    /// </summary>
    [Fact]
    public void TheDefaultIntervalsAreThirtySecondsAndFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), LibraryChangeCoalescer.DefaultQuietPeriod);
        Assert.Equal(TimeSpan.FromMinutes(5), LibraryChangeCoalescer.DefaultMaximumWait);
        Assert.True(LibraryChangeCoalescer.DefaultMaximumWait > LibraryChangeCoalescer.DefaultQuietPeriod);
    }

    private static async Task<Guid[]> BatchFrom(IReadOnlyList<Guid> arrivalOrder)
    {
        var clock = new ManualTimeProvider(Start);
        var sink = new RecordingSink();
        using var coalescer = new LibraryChangeCoalescer([sink], clock, Quiet, Maximum);

        foreach (var item in arrivalOrder)
        {
            coalescer.ItemChanged(item, LibraryChangeKind.Added);
        }

        clock.Advance(Quiet);
        await coalescer.WaitForDispatchAsync();

        return Assert.Single(sink.Batches).Changes.Select(change => change.ItemId).ToArray();
    }

    // Derived from the index rather than generated, so the same case produces the same identifiers
    // in every process and a failure names the same item twice running. One past the index, so no
    // item is Guid.Empty: an empty identifier is what a bug produces and not what a server sends.
    private static Guid ItemId(int index) =>
        Guid.Parse(string.Format(CultureInfo.InvariantCulture, "{0:D8}-0000-0000-0000-000000000000", index + 1));

    private sealed class RecordingSink : ILibraryChangeBatchSink
    {
        private readonly List<LibraryChangeBatch> _batches = [];

        public IReadOnlyList<LibraryChangeBatch> Batches => _batches;

        public Task ChangesSettledAsync(LibraryChangeBatch batch, CancellationToken cancellationToken)
        {
            _batches.Add(batch);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSink : ILibraryChangeBatchSink
    {
        public Task ChangesSettledAsync(LibraryChangeBatch batch, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An evaluation that could not run.");
    }
}
