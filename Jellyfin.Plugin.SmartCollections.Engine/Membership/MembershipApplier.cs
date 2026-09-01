using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
/// What the run says out loud is decided here for the same reason. An operator reads the server
/// log to find out which collection went wrong, so every line names the rule it is about, and a
/// refresh that changed nothing writes nothing at information level: a plugin logging a line per
/// collection per run buries the one line that matters under the collections that were already
/// right.
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
    /// <param name="logger">
    /// Where the run reports itself. One line per collection, at the level the outcome earns:
    /// information where the membership moved, error where the apply failed, and debug where the
    /// collection was already what its rule describes.
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
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshes);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(logger);

        var outcomes = new List<CollectionRefreshOutcome>(refreshes.Count);
        foreach (var refresh in refreshes)
        {
            // Entered per collection rather than once around the loop. A run holding one gate over
            // every collection it touches would make two runs that share a single collection
            // serialise on all of the others as well, which is the cost this design refuses.
            var outcome = await gate.ApplyExclusivelyAsync(
                refresh.CollectionId,
                token => ApplyOneAsync(refresh, writer, token),
                cancellationToken).ConfigureAwait(false);

            outcomes.Add(outcome);
            Report(logger, outcome);
        }

        return outcomes;
    }

    /// <summary>
    /// Writes one line about one collection, at the level its outcome earns.
    /// </summary>
    /// <remarks>
    /// Counts and identifiers only. A rule document is text an operator wrote and an item has a
    /// path on a disk, and neither belongs in a log an administrator may paste into a bug report,
    /// so what is written here is the rule the outcome is about, the collection it wrote to, and
    /// how many identifiers moved in each direction.
    ///
    /// Called after the gate has been released rather than inside it. The line is about a
    /// collection that has finished, so holding the gate to write it would make one collection's
    /// logging wait on another's.
    ///
    /// Each branch asks whether its level is enabled before it builds the line. That is the
    /// analyser's requirement rather than a preference, and it costs nothing here: a run over a
    /// server logging at warning writes none of these and counts nothing for them.
    /// </remarks>
    private static void Report(ILogger logger, CollectionRefreshOutcome outcome)
    {
        if (outcome.Fault is not null)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(
                    outcome.Fault,
                    "Rule {RuleId} could not be applied to collection {CollectionId}",
                    outcome.RuleId,
                    outcome.CollectionId);
            }

            return;
        }

        if (outcome.Added.Count == 0 && outcome.Removed.Count == 0)
        {
            // Debug rather than information, and the level is the whole point of this branch. A
            // collection that is already what its rule describes is the ordinary case on a server
            // whose library did not move, so a run over fifty rules would write fifty lines an
            // operator has to read past to find the one that failed.
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug(
                    "Rule {RuleId} left collection {CollectionId} unchanged, {DroppedCount} match(es) no longer resolve",
                    outcome.RuleId,
                    outcome.CollectionId,
                    outcome.Dropped.Count);
            }

            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Rule {RuleId} changed collection {CollectionId}: {AddedCount} added, {RemovedCount} removed, {DroppedCount} match(es) no longer resolve",
                outcome.RuleId,
                outcome.CollectionId,
                outcome.Added.Count,
                outcome.Removed.Count,
                outcome.Dropped.Count);
        }
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
