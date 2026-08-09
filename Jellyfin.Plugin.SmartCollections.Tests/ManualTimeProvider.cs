using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A clock that only moves when a test moves it, and the timers that hang off it.
/// </summary>
/// <remarks>
/// First-party rather than a package reference, and that is the means decision this file exists to
/// record. The framework ships <see cref="TimeProvider"/> itself, so the seam costs nothing; a
/// controllable implementation of it is not in the framework and comes from a package. This tree
/// carries four test-time packages and no more, and what is needed here is one clock and one
/// one-shot timer, which is the file below. A package would be a dependency to review, to update
/// and to hold at a version for the sake of about ninety lines.
///
/// <see cref="Advance"/> is what makes a coalescer testable at all: the intervals under test are
/// thirty seconds and five minutes, and a suite that waited them out would take five and a half
/// minutes per case and would be flaky on a loaded runner. Nothing here sleeps.
///
/// The loop in <see cref="Advance"/> fires timers at their own due instants rather than all at the
/// end, so a callback that reads the clock sees the moment it was due and not the moment the test
/// stopped advancing. It re-reads the timer list after every callback, because a callback that
/// re-arms its timer is the whole behaviour under test here.
/// </remarks>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private readonly object _sync = new();
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);

        lock (_sync)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Moves the clock forward, firing every timer that falls due on the way.
    /// </summary>
    public void Advance(TimeSpan by)
    {
        var target = GetUtcNow() + by;

        // A callback that re-arms its own timer to the same instant would spin here forever, and a
        // test that hit it would hang rather than fail. The cap turns that into a failure naming
        // what happened. It is far above any burst this suite drives.
        for (var fired = 0; fired < 100_000; fired++)
        {
            var next = NextDueAtOrBefore(target);
            if (next is null)
            {
                lock (_sync)
                {
                    _now = target;
                }

                return;
            }

            lock (_sync)
            {
                _now = next.DueAt!.Value;
            }

            next.Fire();
        }

        throw new InvalidOperationException(
            "A timer callback kept re-arming itself inside one Advance. Something under test is scheduling in a loop.");
    }

    /// <summary>
    /// Moves the clock forward and runs no timer, however overdue it becomes.
    /// </summary>
    /// <remarks>
    /// A real timer is a request rather than a promise. A saturated thread pool delays its
    /// callback, and code that assumed the callback had already arrived meets a clock that has
    /// moved past a due time nothing acted on. That is not an exotic case for this plugin: the
    /// thread the library raises its events on is the same pool, so the busiest moment is exactly
    /// the moment a timer runs late. This is how that moment is written down.
    /// </remarks>
    public void AdvanceWithoutFiring(TimeSpan by)
    {
        lock (_sync)
        {
            _now += by;
        }
    }

    /// <summary>
    /// Runs every live timer's callback now, whether or not it is due.
    /// </summary>
    /// <remarks>
    /// The other half of the same point. A timer may run early, and one that has been cancelled
    /// may still deliver a callback that was already on its way. Both reach a callback that has to
    /// decide for itself rather than trust that being called means it is time.
    /// </remarks>
    public void FireEveryTimerNow()
    {
        ManualTimer[] live;
        lock (_sync)
        {
            live = [.. _timers];
        }

        foreach (var timer in live)
        {
            timer.Fire();
        }
    }

    private ManualTimer? NextDueAtOrBefore(DateTimeOffset target)
    {
        lock (_sync)
        {
            ManualTimer? next = null;
            foreach (var timer in _timers)
            {
                var due = timer.DueAt;
                if (due is not null && due <= target && (next is null || due < next.DueAt))
                {
                    next = timer;
                }
            }

            return next;
        }
    }

    private void Forget(ManualTimer timer)
    {
        lock (_sync)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _disposed;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            // A stand-in that accepts what the real thing refuses is worse than no stand-in, and
            // this pair of lines is here because it was measured rather than foreseen. Every ITimer
            // the framework ships refuses a negative delay, and the clamp in the coalescer that
            // exists to prevent one was deleted and the suite stayed green, because this class
            // happily turned it into a due time in the past. The guard was reported proven and was
            // not. Refusing here is what makes deleting that clamp red.
            RefuseAnythingThatIsNotADelay(dueTime, nameof(dueTime));
            RefuseAnythingThatIsNotADelay(period, nameof(period));

            if (_disposed)
            {
                return false;
            }

            _period = period;
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _owner.GetUtcNow() + dueTime;
            return true;
        }

        public void Fire()
        {
            // The next due time is set BEFORE the callback runs, so a callback that re-arms the
            // timer wins over this line rather than being overwritten by it.
            DueAt = _period == Timeout.InfiniteTimeSpan || _period <= TimeSpan.Zero
                ? null
                : _owner.GetUtcNow() + _period;

            _callback(_state);
        }

        public void Dispose()
        {
            _disposed = true;
            DueAt = null;
            _owner.Forget(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private static void RefuseAnythingThatIsNotADelay(TimeSpan value, string name)
        {
            if (value != Timeout.InfiniteTimeSpan && value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "A timer takes a delay of zero or more, or Timeout.InfiniteTimeSpan. This is what the framework's own timers refuse.");
            }
        }
    }
}
