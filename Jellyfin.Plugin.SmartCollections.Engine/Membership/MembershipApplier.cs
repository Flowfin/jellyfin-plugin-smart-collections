using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// Applies a diff to a collection so that a failure leaves it as it was.
/// </summary>
/// <remarks>
/// A diff is two writes, and two writes can fail between them. What decides whether a failure
/// leaves a collection half changed is not error handling, it is the order the two are issued in.
///
/// The server's add resolves every identifier in the batch before it assigns anything, so an add
/// that throws has written nothing. Its remove takes items out one at a time and saves the item
/// afterwards. Issue the add first and a failure anywhere in it leaves the collection untouched.
/// Issue the remove first and the same failure leaves a collection with items taken out and none
/// put back, which is the state a refresh may never produce: the collection is then neither what
/// the rule describes nor what it was, and nothing on the server records which.
///
/// So the order is add, then remove, and it is the guard rather than a preference. The test named
/// <c>AnAddThatThrowsLeavesTheCollectionWithTheMembershipItStartedWith</c> is what holds it, and
/// swapping the two calls below is what makes that test red.
///
/// The other half is isolation. A refresh covers several collections and one of them throwing
/// says nothing about the rest, so each is applied inside its own attempt and the fault is
/// recorded against that collection rather than ending the run.
///
/// Both of those hold within one run and neither of them says anything about a second run
/// arriving at the same collection. Two runs whose writes interleave both finish and both report
/// success, and the membership they leave behind is neither of the two their rules describe, so
/// the ordering above buys nothing unless one refresh at a time reaches a collection. A
/// <see cref="CollectionRefreshGate"/> is therefore an argument here rather than something a
/// caller may remember to wrap around this, because a run that skipped it would apply exactly
/// like one that did not.
/// </remarks>
public static class MembershipApplier
{
    /// <summary>
    /// Applies each diff to its collection.
    /// </summary>
    /// <param name="refreshes">The collections to change and what to change about them.</param>
    /// <param name="writer">The port the writes go through.</param>
    /// <param name="gate">
    /// What keeps another refresh of the same collection out while this one writes. Two runs
    /// sharing a gate exclude each other; two runs holding a gate each exclude nothing, which is
    /// why the instance is decided where the plugin's services are registered rather than here.
    /// </param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>
    /// One outcome per element of <paramref name="refreshes"/>, in the same order, whether it
    /// succeeded or not.
    /// </returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">
    /// The run was cancelled. This is not recorded against a collection, because continuing to the
    /// next one is the opposite of what a cancelled run was asked to do.
    /// </exception>
    public static async Task<IReadOnlyList<CollectionRefreshOutcome>> ApplyAsync(
        IReadOnlyList<CollectionRefresh> refreshes,
        ICollectionMembershipWriter writer,
        CollectionRefreshGate gate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshes);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(gate);

        var outcomes = new List<CollectionRefreshOutcome>(refreshes.Count);
        foreach (var refresh in refreshes)
        {
            // Entered per collection rather than once around the loop. A run holding one gate over
            // every collection it touches would make two runs that share a single collection
            // serialise on all of the others as well, which is the cost this design refuses.
            outcomes.Add(await gate.ApplyExclusivelyAsync(
                refresh.CollectionId,
                token => ApplyOneAsync(refresh, writer, token),
                cancellationToken).ConfigureAwait(false));
        }

        return outcomes;
    }

    private static async Task<CollectionRefreshOutcome> ApplyOneAsync(
        CollectionRefresh refresh,
        ICollectionMembershipWriter writer,
        CancellationToken cancellationToken)
    {
        var diff = refresh.Diff;
        var added = Array.Empty<Guid>() as IReadOnlyList<Guid>;
        var removed = Array.Empty<Guid>() as IReadOnlyList<Guid>;
        var dropped = Array.Empty<Guid>() as IReadOnlyList<Guid>;

        try
        {
            // The re-resolve is in front of the add and not in front of the remove, and the
            // asymmetry is the point. An item deleted between the query and the write is one the
            // add would throw on, and one the remove still has to take out of the collection: the
            // link is matched by item id, so a link to a deleted item is removable and a remove
            // that skipped it would leave it in the collection for good.
            var wanted = diff.Added;
            if (wanted.Count > 0)
            {
                var resolved = new HashSet<Guid>(writer.ItemsThatStillResolve(wanted));
                var keep = new List<Guid>(wanted.Count);
                var lost = new List<Guid>();
                foreach (var id in wanted)
                {
                    if (resolved.Contains(id))
                    {
                        keep.Add(id);
                    }
                    else
                    {
                        lost.Add(id);
                    }
                }

                dropped = lost;

                // The add goes first. See the remarks on this type; swapping these two is what the
                // guard refuses.
                if (keep.Count > 0)
                {
                    await writer.AddToCollectionAsync(refresh.CollectionId, keep, cancellationToken).ConfigureAwait(false);
                    added = keep;
                }
            }

            // Skipped where there is nothing to take out. The server's remove saves the item and
            // queues a high priority metadata refresh whether or not it matched anything, so
            // calling it with an empty set costs a write to change nothing, which is the whole
            // reason a refresh derives a diff instead of rebuilding.
            if (diff.Removed.Count > 0)
            {
                await writer.RemoveFromCollectionAsync(refresh.CollectionId, diff.Removed, cancellationToken).ConfigureAwait(false);
                removed = diff.Removed;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new CollectionRefreshOutcome(refresh.RuleId, refresh.CollectionId, added, removed, dropped, ex);
        }

        return new CollectionRefreshOutcome(refresh.RuleId, refresh.CollectionId, added, removed, dropped, null);
    }
}
