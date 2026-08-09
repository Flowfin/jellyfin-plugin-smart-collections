using System;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// The server's library manager, seen through the three events this plugin uses.
/// </summary>
/// <remarks>
/// Every accessor here forwards to the same event on <see cref="ILibraryManager"/> and holds no
/// state of its own, so a handler added through this type is added to the server's event and a
/// handler removed through it is removed from the server's event. That is what keeps the
/// subscription's lifetime in one place: this type cannot be holding a subscription of its own
/// that nothing unsubscribes.
///
/// Nothing in the suite executes these six accessors, because the only way to reach them is to
/// hold a real <see cref="ILibraryManager"/>, which means a running server. What the suite covers
/// instead is the type on the other side of the boundary, <see cref="LibraryChangeSubscription"/>,
/// against a stand-in for this interface. The residual is six forwarding lines that a reader can
/// check by eye and no test can reach.
/// </remarks>
public sealed class LibraryManagerChangeSource : ILibraryChangeSource
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryManagerChangeSource"/> class.
    /// </summary>
    /// <param name="libraryManager">The server's library manager.</param>
    /// <exception cref="ArgumentNullException"><paramref name="libraryManager"/> is <see langword="null"/>.</exception>
    public LibraryManagerChangeSource(ILibraryManager libraryManager)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public event EventHandler<ItemChangeEventArgs>? ItemAdded
    {
        add => _libraryManager.ItemAdded += value;
        remove => _libraryManager.ItemAdded -= value;
    }

    /// <inheritdoc />
    public event EventHandler<ItemChangeEventArgs>? ItemUpdated
    {
        add => _libraryManager.ItemUpdated += value;
        remove => _libraryManager.ItemUpdated -= value;
    }

    /// <inheritdoc />
    public event EventHandler<ItemChangeEventArgs>? ItemRemoved
    {
        add => _libraryManager.ItemRemoved += value;
        remove => _libraryManager.ItemRemoved -= value;
    }
}
