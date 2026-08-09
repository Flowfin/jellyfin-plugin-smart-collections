using System;
using System.Runtime.CompilerServices;
using Jellyfin.Plugin.SmartCollections.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The three library events, with a count of what is attached to them.
/// </summary>
/// <remarks>
/// Standing in for <c>ILibraryManager</c> itself would be a file of stubs for well over a hundred
/// members that says nothing about what this plugin subscribes to.
/// <see cref="ILibraryChangeSource"/> is the three events and no more, which is why a stand-in for
/// it is this short, and the count of attached handlers is the whole reason the port exists as a
/// port: an event on the server's own interface cannot be asked how many subscribers it has from
/// outside the class that declares it.
/// </remarks>
internal sealed class FakeLibraryChangeSource : ILibraryChangeSource
{
    public event EventHandler<ItemChangeEventArgs>? ItemAdded;

    public event EventHandler<ItemChangeEventArgs>? ItemUpdated;

    public event EventHandler<ItemChangeEventArgs>? ItemRemoved;

    public int AttachedHandlers =>
        (ItemAdded?.GetInvocationList().Length ?? 0)
        + (ItemUpdated?.GetInvocationList().Length ?? 0)
        + (ItemRemoved?.GetInvocationList().Length ?? 0);

    public void RaiseItemAdded(Guid itemId) => ItemAdded?.Invoke(this, ChangeTo(itemId));

    public void RaiseItemUpdated(Guid itemId) => ItemUpdated?.Invoke(this, ChangeTo(itemId));

    public void RaiseItemRemoved(Guid itemId) => ItemRemoved?.Invoke(this, ChangeTo(itemId));

    public void RaiseWithNoItem() => ItemAdded?.Invoke(this, new ItemChangeEventArgs());

    // Neither of these two shapes is one the server produces. They are the two ways a handler on
    // an EventHandler<T> can be called with nothing to read, and a handler that reads through
    // either one throws inside a raise the server catches and logs, which loses the event and
    // tells nothing above it.
    public void RaiseWithNoArguments() => ItemAdded?.Invoke(this, null!);

    // A BaseItem with an identifier and nothing else. Its constructor initialises collections this
    // suite has no use for and its properties reach static server state, so the object is created
    // without running either and only the one member a handler reads is set.
    private static ItemChangeEventArgs ChangeTo(Guid itemId)
    {
        var item = (Folder)RuntimeHelpers.GetUninitializedObject(typeof(Folder));
        item.Id = itemId;
        return new ItemChangeEventArgs { Item = item };
    }
}
