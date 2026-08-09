using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// Lets one refresh at a time change a collection, and lets different collections change at once.
/// </summary>
/// <remarks>
/// Three things start an evaluation, and nothing stops them arriving together. Two of them
/// reaching one collection can issue their adds and removes in either interleaving, and the
/// membership left behind then matches neither run: the collection is not what the earlier rule
/// described and not what the later one described, and nothing on the server records which run
/// wrote which item.
///
/// Reading the library at the same time is harmless, so the exclusion is around the writes rather
/// than around a whole evaluation. Two collections hold different items and there is no reason to
/// make a large library slower than it has to be, so the exclusion is per collection rather than
/// one lock over the plugin.
///
/// A gate excludes anything only where every trigger holds the same instance of it. Which instance
/// that is, and how long it lives, is decided where the plugin registers its services.
///
/// <see cref="SemaphoreSlim"/> rather than a chain of tasks, and this type is deliberately not
/// disposable. A chain releases a waiter that was cancelled while it queued, and releasing it
/// early lets the caller behind it start while the caller in front is still writing, which is the
/// failure this whole type exists against. A semaphore cancels a waiter without ever handing it
/// the permit. Nothing here reads <c>AvailableWaitHandle</c>, which is the only member that
/// allocates a wait handle, so there is nothing to release and a <c>Dispose</c> would add a way to
/// break a refresh already in flight in exchange for nothing.
///
/// One semaphore per collection is kept for the life of the gate. The number of collections is the
/// number of rules an operator wrote, so the table is bounded by the rules directory rather than
/// by how many refreshes have run, and removing an entry when its count returns to one is a
/// race with the next caller to arrive for a saving that does not exist.
/// </remarks>
public sealed class CollectionRefreshGate
{
    private readonly Dictionary<Guid, SemaphoreSlim> _perCollection = [];
    private readonly object _sync = new();

    /// <summary>
    /// Runs the writes for one collection with no other refresh of that collection running.
    /// </summary>
    /// <typeparam name="T">What the caller's work reports.</typeparam>
    /// <param name="collectionId">The collection being changed.</param>
    /// <param name="apply">The writes, which are the part that has to be exclusive.</param>
    /// <param name="cancellationToken">
    /// Cancels the wait as well as the work. A caller cancelled while it is queued behind another
    /// refresh never takes the gate, so nothing it does can reach the collection.
    /// </param>
    /// <returns>Whatever <paramref name="apply"/> reported.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="apply"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">
    /// The wait or the work was cancelled. The gate is free afterwards either way.
    /// </exception>
    public async Task<T> ApplyExclusivelyAsync<T>(
        Guid collectionId,
        Func<CancellationToken, Task<T>> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);

        var gate = ForCollection(collectionId);

        // Outside the try, and that is the guard rather than a layout. A wait that threw handed
        // out no permit, so a release in a finally that covered it would return a permit this
        // caller never held and let the next one in while the one in front is still writing.
        // AWaiterCancelledWhileItQueuesDoesNotReleaseTheRefreshInFrontOfIt is the test that goes
        // red when this line moves below the brace.
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await apply(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Every way out of the work releases: a return, a cancellation and a throw. A refresh
            // that failed has stopped touching the collection, so holding the gate afterwards
            // would leave the collection unrefreshable until the server restarted.
            gate.Release();
        }
    }

    private SemaphoreSlim ForCollection(Guid collectionId)
    {
        // A plain dictionary under a lock rather than a concurrent one, because the factory of a
        // concurrent dictionary may run and lose, and the semaphore it built would then be thrown
        // away after a caller had already begun waiting on it.
        lock (_sync)
        {
            if (!_perCollection.TryGetValue(collectionId, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _perCollection[collectionId] = gate;
            }

            return gate;
        }
    }
}
