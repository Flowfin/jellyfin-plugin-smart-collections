using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// Something that runs when a burst of library changes has settled.
/// </summary>
/// <remarks>
/// This is the seam an evaluation plugs into, and it is a port with no implementation in this
/// repository for the same reason <c>ICollectionMembershipWriter</c> was one before anything
/// implemented it: what runs here is the evaluation, which is planned separately, and a coalescer
/// that named the evaluation directly could not be tested without one. The list of sinks is
/// resolved, so nothing here decides how many there are, and today there are none.
///
/// The method is asynchronous because an evaluation reads a library and writes a collection.
/// Nothing about the coalescer waits for it: the batch is closed and cleared before this is
/// called, so a slow evaluation delays the next batch's dispatch and never blocks the library
/// thread that raised the events.
/// </remarks>
public interface ILibraryChangeBatchSink
{
    /// <summary>
    /// Handles one settled batch of library changes.
    /// </summary>
    /// <param name="batch">What changed, and over what window.</param>
    /// <param name="cancellationToken">Cancelled when the coalescer is disposed.</param>
    /// <returns>A task that completes when this sink is finished with the batch.</returns>
    Task ChangesSettledAsync(LibraryChangeBatch batch, CancellationToken cancellationToken);
}
