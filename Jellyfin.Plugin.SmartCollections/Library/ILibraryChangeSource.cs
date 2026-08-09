using System;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// The three library events this plugin subscribes to.
/// </summary>
/// <remarks>
/// Narrower than <c>ILibraryManager</c> on purpose, for the same reason
/// <c>ICollectionMembershipWriter</c> is narrower than <c>ICollectionManager</c>. The server's
/// interface carries well over a hundred members and a test standing in for it would be a file of
/// stubs that says nothing about what this plugin uses. Three events is what a subscription needs,
/// and a stand-in for three events is three events.
///
/// The event arguments are the server's, not a type of this plugin's own. Translating them at this
/// boundary would put a mapping between the server and the only thing that reads it, and the
/// mapping would then be the untested part of the path a real event travels.
/// </remarks>
public interface ILibraryChangeSource
{
    /// <summary>
    /// Raised after an item is added to the library.
    /// </summary>
    event EventHandler<ItemChangeEventArgs>? ItemAdded;

    /// <summary>
    /// Raised after something about an item in the library changes.
    /// </summary>
    event EventHandler<ItemChangeEventArgs>? ItemUpdated;

    /// <summary>
    /// Raised after an item is removed from the library.
    /// </summary>
    event EventHandler<ItemChangeEventArgs>? ItemRemoved;
}
