using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Library;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A subscription that is not given back is the failure here, and it is a silent one.
/// </summary>
/// <remarks>
/// The server holds the event, so a handler this plugin attaches and never detaches keeps the
/// plugin's objects alive and keeps running after the plugin is meant to have stopped. Nothing
/// reports it: the server raises the event, the handler runs, and an exception out of it is caught
/// and logged by the server rather than passed on. So the only place a lost detach can be caught
/// is here, against a stand-in for the library that can be asked how many handlers are attached.
/// </remarks>
public class LibraryChangeSubscriptionTests
{
    [Fact]
    public async Task StartingAttachesOneHandlerToEachOfTheThreeEvents()
    {
        var library = new FakeLibraryChangeSource();
        var subscription = new LibraryChangeSubscription(library, []);

        Assert.Equal(0, library.AttachedHandlers);
        Assert.False(subscription.IsAttached);

        await subscription.StartAsync(CancellationToken.None);

        Assert.Equal(3, library.AttachedHandlers);
        Assert.True(subscription.IsAttached);
    }

    /// <summary>
    /// The guard. Building the three handlers once and attaching those instances is what makes the
    /// detach take them off again; a lambda written at each <c>+=</c> and another at each <c>-=</c>
    /// would leave this at three with nothing anywhere reporting it.
    /// </summary>
    [Fact]
    public async Task StoppingLeavesNoHandlerAttached()
    {
        var library = new FakeLibraryChangeSource();
        var subscription = new LibraryChangeSubscription(library, []);

        await subscription.StartAsync(CancellationToken.None);
        await subscription.StopAsync(CancellationToken.None);

        Assert.Equal(0, library.AttachedHandlers);
        Assert.False(subscription.IsAttached);
    }

    [Fact]
    public async Task StartingTwiceStillAttachesOneSetOfHandlers()
    {
        var library = new FakeLibraryChangeSource();
        var subscription = new LibraryChangeSubscription(library, []);

        await subscription.StartAsync(CancellationToken.None);
        await subscription.StartAsync(CancellationToken.None);

        Assert.Equal(3, library.AttachedHandlers);

        // The point of refusing the second attach is that one stop still ends the subscription. A
        // second copy of each handler would leave three attached here.
        await subscription.StopAsync(CancellationToken.None);

        Assert.Equal(0, library.AttachedHandlers);
    }

    [Fact]
    public async Task StoppingWithoutStartingTouchesNothing()
    {
        var library = new FakeLibraryChangeSource();
        var subscription = new LibraryChangeSubscription(library, []);

        await subscription.StopAsync(CancellationToken.None);

        Assert.Equal(0, library.AttachedHandlers);
        Assert.False(subscription.IsAttached);
    }

    [Fact]
    public async Task EachEventReachesEveryObserverWithTheItemAndTheKindItArrivedOn()
    {
        var library = new FakeLibraryChangeSource();
        var first = new RecordingObserver();
        var second = new RecordingObserver();
        var subscription = new LibraryChangeSubscription(library, [first, second]);

        await subscription.StartAsync(CancellationToken.None);

        var added = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var updated = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var removed = Guid.Parse("33333333-3333-3333-3333-333333333333");

        library.RaiseItemAdded(added);
        library.RaiseItemUpdated(updated);
        library.RaiseItemRemoved(removed);

        (Guid, LibraryChangeKind)[] expected =
        [
            (added, LibraryChangeKind.Added),
            (updated, LibraryChangeKind.Updated),
            (removed, LibraryChangeKind.Removed)
        ];

        Assert.Equal(expected, first.Seen);
        Assert.Equal(expected, second.Seen);
    }

    [Fact]
    public async Task NothingIsReportedAfterTheSubscriptionStops()
    {
        var library = new FakeLibraryChangeSource();
        var observer = new RecordingObserver();
        var subscription = new LibraryChangeSubscription(library, [observer]);

        await subscription.StartAsync(CancellationToken.None);
        await subscription.StopAsync(CancellationToken.None);

        library.RaiseItemAdded(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        Assert.Empty(observer.Seen);
    }

    /// <summary>
    /// The server names an item on every raise of these three events. A raise that does not is
    /// nothing to report, and reading through it would be a null reference inside a handler whose
    /// exceptions the server swallows, so the event would be lost and nothing above would learn.
    /// </summary>
    [Fact]
    public async Task AnEventCarryingNoItemReachesNoObserver()
    {
        var library = new FakeLibraryChangeSource();
        var observer = new RecordingObserver();
        var subscription = new LibraryChangeSubscription(library, [observer]);

        await subscription.StartAsync(CancellationToken.None);

        library.RaiseWithNoItem();
        library.RaiseWithNoArguments();

        Assert.Empty(observer.Seen);
    }

    [Fact]
    public void ASubscriptionRefusesToBeBuiltWithoutALibraryOrAnObserverList()
    {
        Assert.Throws<ArgumentNullException>(() => new LibraryChangeSubscription(null!, []));
        Assert.Throws<ArgumentNullException>(() => new LibraryChangeSubscription(new FakeLibraryChangeSource(), null!));
    }

    private sealed class RecordingObserver : ILibraryChangeObserver
    {
        private readonly List<(Guid ItemId, LibraryChangeKind Kind)> _seen = [];

        public IReadOnlyList<(Guid ItemId, LibraryChangeKind Kind)> Seen => _seen;

        public void ItemChanged(Guid itemId, LibraryChangeKind kind) => _seen.Add((itemId, kind));
    }
}
