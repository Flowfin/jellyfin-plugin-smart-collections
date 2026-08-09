using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// Holds this plugin's subscription to the library's item events for as long as the server runs.
/// </summary>
/// <remarks>
/// A subscription is a thing that has to be given back. The plugin class cannot be what gives this
/// one back: the server constructs it, hands it a configuration and never tells it the server is
/// stopping, so a handler attached from there stays attached to a library manager that outlives
/// the plugin object. A hosted service is told both ends, and the server runs one because its host
/// is a <c>Host.CreateDefaultBuilder</c> host and plugin registrations reach it through
/// <c>appHost.Init(services)</c> inside <c>ConfigureServices</c>.
///
/// The three handlers are built once, in the constructor, and the same three instances are used to
/// attach and to detach. Attaching with a lambda per call is the mistake this shape refuses: a
/// lambda written at the <c>+=</c> and another written at the <c>-=</c> are different objects, the
/// detach silently removes nothing, and the subscription survives a stop with no error anywhere.
/// <c>StoppingLeavesNoHandlerAttached</c> is what goes red if these fields become inline lambdas.
///
/// What the handlers do is deliberately almost nothing: they name the item and the kind to whatever
/// observers are registered, and return. The server raises these events synchronously on the thread
/// doing the library work, so an evaluation inside one of them would run once per imported episode
/// on the importing thread. Accumulating the changes and deciding when an evaluation runs is #35,
/// which registers itself as an observer. No observer is registered today, so the fan-out below is
/// over an empty list and the subscription costs one delegate call per library event.
/// </remarks>
public sealed class LibraryChangeSubscription : IHostedService
{
    private readonly ILibraryChangeSource _source;
    private readonly IReadOnlyList<ILibraryChangeObserver> _observers;
    private readonly EventHandler<ItemChangeEventArgs> _onItemAdded;
    private readonly EventHandler<ItemChangeEventArgs> _onItemUpdated;
    private readonly EventHandler<ItemChangeEventArgs> _onItemRemoved;

    private bool _attached;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangeSubscription"/> class.
    /// </summary>
    /// <param name="source">The library events to subscribe to.</param>
    /// <param name="observers">Everything that wants to be told about a change.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public LibraryChangeSubscription(ILibraryChangeSource source, IEnumerable<ILibraryChangeObserver> observers)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(observers);

        _source = source;
        _observers = observers.ToArray();
        _onItemAdded = (_, e) => Observe(e, LibraryChangeKind.Added);
        _onItemUpdated = (_, e) => Observe(e, LibraryChangeKind.Updated);
        _onItemRemoved = (_, e) => Observe(e, LibraryChangeKind.Removed);
    }

    /// <summary>
    /// Gets a value indicating whether the handlers are attached to the library right now.
    /// </summary>
    public bool IsAttached => _attached;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Attaching twice would add a second copy of each handler and every event would be
        // reported twice, and the matching detach would take only one copy back off.
        if (!_attached)
        {
            _source.ItemAdded += _onItemAdded;
            _source.ItemUpdated += _onItemUpdated;
            _source.ItemRemoved += _onItemRemoved;
            _attached = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_attached)
        {
            _source.ItemAdded -= _onItemAdded;
            _source.ItemUpdated -= _onItemUpdated;
            _source.ItemRemoved -= _onItemRemoved;
            _attached = false;
        }

        return Task.CompletedTask;
    }

    private void Observe(ItemChangeEventArgs? change, LibraryChangeKind kind)
    {
        // The server always names an item on these events. A raise that does not is nothing this
        // plugin can tell anyone about, and reading through it would turn a server-side oddity
        // into a null reference inside a handler the server catches and logs.
        var item = change?.Item;
        if (item is null)
        {
            return;
        }

        foreach (var observer in _observers)
        {
            observer.ItemChanged(item.Id, kind);
        }
    }
}
