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
/// One type implements this and nothing in this file names it. The list of observers is resolved,
/// so the subscription fans out to whatever is registered rather than to a type written down here,
/// and what that resolves to is read rather than restated:
///
/// <code>
/// git grep -l ': ILibraryChangeObserver' -- Jellyfin.Plugin.SmartCollections/
/// Jellyfin.Plugin.SmartCollections/Library/LibraryChangeCoalescer.cs
/// </code>
///
/// THIS PARAGRAPH SAID NOTHING IMPLEMENTED THIS YET and named two issues that would change that.
/// Both closed and the implementation arrived. A remark saying a seam is unused is what a reader
/// trusts when they are deciding whether a change here reaches anything, so it was wrong in the
/// direction that costs.
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
