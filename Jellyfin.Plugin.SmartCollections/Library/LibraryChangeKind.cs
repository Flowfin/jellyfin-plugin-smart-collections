namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// Which of the three library events a change arrived on.
/// </summary>
/// <remarks>
/// The server raises three separate events and a subscriber that collapsed them into "something
/// changed" would throw away the one distinction that decides what a rule has to do about it: an
/// item that has gone can only leave collections, and an item that arrived can only join them.
/// Whether anything downstream uses the distinction is not settled here; losing it at the boundary
/// would settle it by accident.
/// </remarks>
public enum LibraryChangeKind
{
    /// <summary>
    /// The item was added to the library.
    /// </summary>
    Added,

    /// <summary>
    /// The item is still in the library and something about it changed.
    /// </summary>
    Updated,

    /// <summary>
    /// The item was removed from the library.
    /// </summary>
    Removed
}
