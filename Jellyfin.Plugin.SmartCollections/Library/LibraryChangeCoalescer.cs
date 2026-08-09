using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// Accumulates library changes and lets one evaluation run per burst instead of one per event.
/// </summary>
/// <remarks>
/// Subscribing naively is worse than not subscribing at all. A library scan importing two thousand
/// episodes raises two thousand item events, one after another on the thread doing the import, and
/// a full evaluation inside each one would keep the server doing the same work two thousand times
/// while the import waits behind it.
///
/// So changes accumulate here and an evaluation runs once the stream has been quiet for
/// <see cref="DefaultQuietPeriod"/>, or once <see cref="DefaultMaximumWait"/> has passed since the
/// first change in the burst, whichever comes first. The quiet period alone is not enough: a scan
/// that keeps producing events for an hour would keep pushing the evaluation out and the
/// collections would not move until it stopped. The maximum alone is not enough either, because a
/// single added film would then wait the whole maximum for no reason.
///
/// <see cref="ItemChanged"/> records and returns. It takes a lock, writes one dictionary entry and
/// re-arms a timer, and it does nothing that reads a library or writes a collection, because it
/// runs on the server's own thread inside a raise the server catches and logs. An exception thrown
/// here would be swallowed by the server and the event lost with nothing above learning of it,
/// which is why the work in it is kept to what cannot throw for any reason other than memory.
///
/// The clock is injected rather than ambient. The two intervals are the whole behaviour of this
/// type, and a test that had to wait real seconds to see either of them would be a test nobody
/// runs; with <see cref="TimeProvider"/> the whole of it is exercised in microseconds and no test
/// sleeps. The type is the framework's own rather than an interface of this plugin's, so it adds
/// no package, no runtime and no vocabulary a .NET reader does not already have. The determinism
/// work needs the same injected clock for the evaluation instant, and this is the type it takes
/// rather than a second one beside it.
///
/// The two intervals are constructor arguments carrying the defaults below rather than settings
/// read from the configuration page. A setting arrives with the surface that reads it, and the
/// settings surface is planned separately; what is here is the pair of values and the reason for
/// each, which is what that surface needs when it arrives.
/// </remarks>
public sealed class LibraryChangeCoalescer : ILibraryChangeObserver, IDisposable
{
    /// <summary>
    /// How long the stream has to be quiet before a burst is treated as finished.
    /// </summary>
    /// <remarks>
    /// Thirty seconds. Short enough that one film added by hand reaches its collection while the
    /// person who added it is still looking at the screen, and long enough that the gaps a scan
    /// leaves between batches of items do not each close a burst of their own.
    /// </remarks>
    public static readonly TimeSpan DefaultQuietPeriod = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest a change waits behind a stream that keeps producing more.
    /// </summary>
    /// <remarks>
    /// Five minutes. It bounds how stale a collection can be during an import that runs for hours,
    /// and it is far enough above the quiet period that an ordinary burst never reaches it, so the
    /// reason on a batch stays informative rather than reporting the maximum every time.
    /// </remarks>
    public static readonly TimeSpan DefaultMaximumWait = TimeSpan.FromMinutes(5);

    private readonly IReadOnlyList<ILibraryChangeBatchSink> _sinks;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _quietPeriod;
    private readonly TimeSpan _maximumWait;
    private readonly Dictionary<Guid, LibraryChangeKind> _pending = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _sync = new();
    private readonly ITimer _timer;

    private DateTimeOffset _firstChangeAt;
    private DateTimeOffset _lastChangeAt;
    private Task _dispatch = Task.CompletedTask;
    private int _batchesClosed;
    private int _sinkFaults;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangeCoalescer"/> class.
    /// </summary>
    /// <param name="sinks">What runs when a burst settles. Empty is legal and costs one closed batch that reaches nobody.</param>
    /// <param name="timeProvider">The clock the two intervals are measured on.</param>
    /// <param name="quietPeriod">How long the stream has to be quiet before a burst is closed.</param>
    /// <param name="maximumWait">The longest a burst may stay open while changes keep arriving.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sinks"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="quietPeriod"/> is not positive, or <paramref name="maximumWait"/> is below it. A maximum
    /// below the quiet period would close every burst at the maximum and the quiet period would never decide
    /// anything, which is a configuration that reads as coalescing and is not.
    /// </exception>
    public LibraryChangeCoalescer(
        IEnumerable<ILibraryChangeBatchSink> sinks,
        TimeProvider timeProvider,
        TimeSpan quietPeriod,
        TimeSpan maximumWait)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quietPeriod, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumWait, quietPeriod);

        _sinks = sinks.ToArray();
        _timeProvider = timeProvider;
        _quietPeriod = quietPeriod;
        _maximumWait = maximumWait;
        _timer = timeProvider.CreateTimer(OnDue, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Gets the number of batches this coalescer has closed since it was built.
    /// </summary>
    /// <remarks>
    /// This is what "two thousand events produce exactly one evaluation" is counted on. It counts
    /// batches closed rather than sinks called, so it means the same thing whether there are no
    /// sinks or three.
    /// </remarks>
    public int BatchesClosed
    {
        get
        {
            lock (_sync)
            {
                return _batchesClosed;
            }
        }
    }

    /// <summary>
    /// Gets the number of times a sink threw while handling a batch.
    /// </summary>
    /// <remarks>
    /// Its own counter rather than anything the server reports, because the server reports nothing
    /// about this path: it catches and logs whatever a library event handler throws, and this runs
    /// after that handler has already returned. A sink that fails every time otherwise looks
    /// exactly like a plugin with nothing to do. Showing this figure to an operator without them
    /// reading a log file is planned separately, and this is the number that surface reads.
    /// </remarks>
    public int SinkFaults => Volatile.Read(ref _sinkFaults);

    /// <summary>
    /// Gets the number of changed items waiting for the current burst to close.
    /// </summary>
    public int PendingChanges
    {
        get
        {
            lock (_sync)
            {
                return _pending.Count;
            }
        }
    }

    /// <inheritdoc />
    public void ItemChanged(Guid itemId, LibraryChangeKind kind)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();

            // The first change of a burst is what the maximum wait is measured from. Setting it on
            // every change instead would make the maximum a second quiet period and a stream that
            // never goes quiet would never be evaluated, which is the failure the maximum exists
            // against. AnUnendingStreamIsEvaluatedAtTheMaximumRatherThanNever goes red for it.
            if (_pending.Count == 0)
            {
                _firstChangeAt = now;
            }

            // One entry per item, holding the last kind seen. A list would grow with the size of
            // the scan rather than with the number of items in it, which is the same unbounded
            // growth this type exists to prevent, one step further along.
            _pending[itemId] = kind;
            _lastChangeAt = now;

            ArmTimer(now);
        }
    }

    /// <summary>
    /// Waits for the sinks of the batches closed so far to finish.
    /// </summary>
    /// <remarks>
    /// For the suite. Closing a batch and handing it to the sinks are two steps, and a test that
    /// asserted on a sink straight after advancing the clock would be asserting before the sink
    /// had run. Nothing in the plugin calls this; a dispatch that nobody waits for is the ordinary
    /// case, and <see cref="SinkFaults"/> is how a failure in one is seen.
    /// </remarks>
    /// <returns>A task that completes when the dispatch in flight, if any, has finished.</returns>
    public Task WaitForDispatchAsync()
    {
        lock (_sync)
        {
            return _dispatch;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
        }

        // Outside the lock. Cancelling runs the continuations registered on the token, and running
        // those under the lock a library event also takes would hand an unrelated caller's code a
        // lock of this plugin's.
        _stopping.Cancel();
        _timer.Dispose();
        _stopping.Dispose();
    }

    private void ArmTimer(DateTimeOffset now)
    {
        var dueAt = DueAt();
        var delay = dueAt - now;

        // A due time already in the past asks the timer for a negative delay, which is not a delay
        // the framework accepts. It means the burst is due now, so the timer is asked for now.
        _timer.Change(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
    }

    private DateTimeOffset DueAt()
    {
        var quietAt = _lastChangeAt + _quietPeriod;
        var latestAt = _firstChangeAt + _maximumWait;
        return latestAt < quietAt ? latestAt : quietAt;
    }

    private void OnDue(object? state)
    {
        LibraryChangeBatch? batch;

        lock (_sync)
        {
            // Disposal is not tested for separately. Dispose clears the pending set and refuses
            // every later change, so a callback still in flight when the plugin stops finds
            // nothing here, and a second condition naming _disposed would be an arm no input can
            // reach. A timer that runs with nothing pending is otherwise an ordinary event: the
            // framework does not promise a cancelled callback never arrives.
            if (_pending.Count == 0)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            var dueAt = DueAt();

            // A timer may run early, and one that did would close a burst that is still filling.
            // Rather than trust the callback, the decision is made again against the clock, and a
            // callback that arrived early re-arms instead of closing.
            if (now < dueAt)
            {
                ArmTimer(now);
                return;
            }

            var latestAt = _firstChangeAt + _maximumWait;
            var reason = latestAt < _lastChangeAt + _quietPeriod
                ? LibraryChangeBatchReason.MaximumWaitReached
                : LibraryChangeBatchReason.StreamWentQuiet;

            var changes = _pending
                .Select(pending => new LibraryItemChange(pending.Key, pending.Value))
                .OrderBy(change => change.ItemId)
                .ToArray();

            _pending.Clear();
            _batchesClosed++;
            batch = new LibraryChangeBatch(changes, _firstChangeAt, now, reason);
        }

        // Outside the lock, and the batch is already cleared out of the pending set, so a sink
        // that takes minutes delays nothing arriving behind it: the next change starts a new burst
        // straight away rather than queueing behind this dispatch.
        var dispatch = DispatchAsync(batch);

        lock (_sync)
        {
            _dispatch = dispatch;
        }
    }

    private async Task DispatchAsync(LibraryChangeBatch batch)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.ChangesSettledAsync(batch, _stopping.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // One sink that throws must not stop the sinks behind it, and it must not fault a
                // task nobody awaits. Counted rather than rethrown, for the reason on SinkFaults.
                Interlocked.Increment(ref _sinkFaults);
            }
        }
    }
}
