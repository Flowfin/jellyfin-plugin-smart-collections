using System;

namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// Something that wants to be told a library item changed.
/// </summary>
/// <remarks>
/// The server raises its three item events synchronously, on whatever thread is doing the library
/// work, and it catches and logs anything a handler throws rather than passing it on. Two things
/// follow for an implementation of this interface. It records the change and returns, because a
/// library scan importing two thousand episodes raises two thousand of these and an evaluation
/// inside one of them would run two thousand times on the thread doing the import. And it counts
/// its own faults, because an exception out of here is swallowed by the server and nothing above
/// learns that an event was lost.
///
/// Nothing implements this yet. The subscription and its lifetime are what this milestone's
/// registration issue owes; what accumulates the changes and decides when an evaluation runs is
/// the coalescing issue, #35, and this is the seam it plugs into. The list of observers is
/// resolved rather than the coalescer being named here, so that issue picks its own type.
/// </remarks>
public interface ILibraryChangeObserver
{
    /// <summary>
    /// Records that one item changed.
    /// </summary>
    /// <param name="itemId">The item the server named.</param>
    /// <param name="kind">Which event it arrived on.</param>
    void ItemChanged(Guid itemId, LibraryChangeKind kind);
}
