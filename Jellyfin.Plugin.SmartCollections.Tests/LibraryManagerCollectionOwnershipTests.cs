using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Membership;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The server side of the port that decides which collection a rule owns and what it is called.
/// </summary>
/// <remarks>
/// The same bound as the writer tests beside it: what is asserted is which server member each port
/// member reaches, with what arguments, and what it does with the answer. Whether the server then
/// stores a provider dictionary the way this plugin expects is a property of the server, read out
/// of its source in the adapter's remarks and observed by nothing here.
/// </remarks>
public class LibraryManagerCollectionOwnershipTests
{
    private static readonly Guid Collection = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid Parent = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");

    /// <summary>
    /// The lookup hands the query on exactly as it was composed. An adapter that added a property
    /// would be narrowing the search for this plugin's own mark somewhere no document can be read,
    /// which is the one thing the port says it may not do.
    /// </summary>
    [Fact]
    public void TheLookupHandsTheQueryToTheServerUnchanged()
    {
        var composed = new InternalItemsQuery { Name = "given" };
        InternalItemsQuery? asked = null;

        var (library, recorder) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemList"] = args =>
            {
                asked = (InternalItemsQuery)args[0]!;
                return (IReadOnlyList<BaseItem>)[];
            },
        });

        var ownership = new LibraryManagerCollectionOwnership(library, Collections());

        Assert.Empty(ownership.FindCollections(composed));
        Assert.Equal(["GetItemList"], recorder.Calls);
        Assert.Same(composed, asked);
    }

    /// <summary>
    /// The port answers with every match and with the title the library shows for each one, because
    /// choosing between two collections carrying one mark is the resolver's decision and the
    /// comparison against the rule's name is made on that title.
    /// </summary>
    [Fact]
    public void TheLookupAnswersEveryMatchWithTheTitleTheLibraryShows()
    {
        var first = new BoxSet { Id = Collection, Name = "Nineties Thrillers" };
        var second = new BoxSet { Id = Parent, Name = "Nineties thrillers" };

        var (library, _) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemList"] = _ => (IReadOnlyList<BaseItem>)[first, second],
        });

        var ownership = new LibraryManagerCollectionOwnership(library, Collections());

        var found = ownership.FindCollections(new InternalItemsQuery());

        Assert.Equal(
            [new CollectionMatch(Collection, "Nineties Thrillers"), new CollectionMatch(Parent, "Nineties thrillers")],
            found);
    }

    /// <summary>
    /// The mark goes in on the create rather than in a write afterwards, which is the property the
    /// port's own remarks rest on: a create followed by a stamp can fail between the two and leave a
    /// collection this plugin made and cannot recognise.
    /// </summary>
    [Fact]
    public async Task TheCreateCarriesTheMarkAndAnswersWithTheIdentifierTheServerMade()
    {
        CollectionCreationOptions? options = null;

        var (collections, recorder) = FakeServer.For<ICollectionManager>(new()
        {
            ["CreateCollectionAsync"] = args =>
            {
                options = (CollectionCreationOptions)args[0]!;
                return Task.FromResult(new BoxSet { Id = Collection });
            },
        });

        var (library, libraryCalls) = FakeServer.For<ILibraryManager>([]);
        var ownership = new LibraryManagerCollectionOwnership(library, collections);

        var created = await ownership.CreateCollectionAsync(
            "Nineties Thrillers",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["SmartCollections"] = "nineties-thrillers" },
            CancellationToken.None);

        Assert.Equal(Collection, created);
        Assert.Equal(["CreateCollectionAsync"], recorder.Calls);
        Assert.Empty(libraryCalls.Calls);
        Assert.Equal("Nineties Thrillers", options!.Name);
        Assert.Equal("nineties-thrillers", options.ProviderIds["SmartCollections"]);
    }

    /// <summary>
    /// The rename has no server call of its own, so it resolves the item, writes the title and saves
    /// it. The parent is resolved through the adapter's own library manager rather than through
    /// <c>BaseItem.GetParent</c>, which reads the static one the server assigns at startup.
    /// </summary>
    [Fact]
    public async Task TheRenameWritesTheTitleAndSavesItWithTheParentItResolved()
    {
        var collection = new BoxSet { Id = Collection, Name = "Old", ParentId = Parent };
        var parent = new Folder { Id = Parent, Name = "Collections" };
        object?[] saved = [];

        var (library, recorder) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemById"] = args => Equals(args[0], Collection) ? collection : parent,
            ["UpdateItemAsync"] = args =>
            {
                saved = args;
                return Task.CompletedTask;
            },
        });

        var ownership = new LibraryManagerCollectionOwnership(library, Collections());

        await ownership.RenameCollectionAsync(Collection, "New", CancellationToken.None);

        Assert.Equal(["GetItemById", "GetItemById", "UpdateItemAsync"], recorder.Calls);
        Assert.Equal("New", collection.Name);
        Assert.Same(collection, saved[0]);
        Assert.Same(parent, saved[1]);
        Assert.Equal(ItemUpdateType.MetadataEdit, saved[2]);
    }

    /// <summary>
    /// A collection whose parent identifier is not set is saved with no parent rather than with a
    /// lookup for an empty identifier. The server reads the argument to clear the parent folder's
    /// cached children and to name it in the change event, so a null is a missed cache invalidation
    /// and not a lost write.
    /// </summary>
    [Fact]
    public async Task TheRenameOfACollectionWithNoParentSavesItWithNone()
    {
        var collection = new BoxSet { Id = Collection, Name = "Old" };
        object?[] saved = [];

        var (library, recorder) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemById"] = _ => collection,
            ["UpdateItemAsync"] = args =>
            {
                saved = args;
                return Task.CompletedTask;
            },
        });

        var ownership = new LibraryManagerCollectionOwnership(library, Collections());

        await ownership.RenameCollectionAsync(Collection, "New", CancellationToken.None);

        Assert.Equal(["GetItemById", "UpdateItemAsync"], recorder.Calls);
        Assert.Null(saved[1]);
    }

    /// <summary>
    /// A collection that has gone between the lookup and the write is not a fault of this run: the
    /// next resolve finds no mark and creates one, which is what the resolver already does for a
    /// deleted collection. Renaming nothing is what lets a run reach the collection after it.
    /// </summary>
    [Fact]
    public async Task TheRenameOfACollectionThatIsNotThereWritesNothing()
    {
        var (library, recorder) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemById"] = _ => null,
        });

        var ownership = new LibraryManagerCollectionOwnership(library, Collections());

        await ownership.RenameCollectionAsync(Collection, "New", CancellationToken.None);

        Assert.Equal(["GetItemById"], recorder.Calls);
    }

    /// <summary>
    /// Neither write takes a cancellation token from the server on the create's side, so the token
    /// is checked in front of the call. A cancelled run reaches neither the create nor the rename.
    /// </summary>
    /// <param name="write">Which of the two writes is being cancelled.</param>
    /// <returns>The assertion.</returns>
    [Theory]
    [InlineData("create")]
    [InlineData("rename")]
    public async Task ACancelledRunReachesNeitherWrite(string write)
    {
        var (collections, collectionCalls) = FakeServer.For<ICollectionManager>([]);
        var (library, libraryCalls) = FakeServer.For<ILibraryManager>([]);
        var ownership = new LibraryManagerCollectionOwnership(library, collections);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => string.Equals(write, "create", StringComparison.Ordinal)
                ? ownership.CreateCollectionAsync("New", new Dictionary<string, string>(StringComparer.Ordinal), cancelled.Token)
                : ownership.RenameCollectionAsync(Collection, "New", cancelled.Token));

        Assert.Empty(collectionCalls.Calls);
        Assert.Empty(libraryCalls.Calls);
    }

    /// <summary>
    /// Every member refuses an absent argument rather than handing one to the server, which is what
    /// the rest of this plugin does at every boundary it owns.
    /// </summary>
    [Fact]
    public async Task EveryMemberRefusesAnAbsentArgument()
    {
        var (library, _) = FakeServer.For<ILibraryManager>([]);
        var ownership = new LibraryManagerCollectionOwnership(library, Collections());

        Assert.Throws<ArgumentNullException>(() => ownership.FindCollections(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ownership.CreateCollectionAsync(null!, new Dictionary<string, string>(StringComparer.Ordinal), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ownership.CreateCollectionAsync("New", null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => ownership.RenameCollectionAsync(Collection, null!, CancellationToken.None));
    }

    /// <summary>
    /// Neither adapter may be built without both managers, because a null one would reach the first
    /// call rather than the construction and the fault would name a member instead of a wiring.
    /// </summary>
    [Fact]
    public void NeitherAdapterIsBuiltWithoutBothManagers()
    {
        var (library, _) = FakeServer.For<ILibraryManager>([]);
        var collections = Collections();

        Assert.Throws<ArgumentNullException>(() => new LibraryManagerCollectionOwnership(null!, collections));
        Assert.Throws<ArgumentNullException>(() => new LibraryManagerCollectionOwnership(library, null!));
        Assert.Throws<ArgumentNullException>(() => new LibraryManagerMembershipWriter(null!, collections));
        Assert.Throws<ArgumentNullException>(() => new LibraryManagerMembershipWriter(library, null!));
    }

    private static ICollectionManager Collections()
    {
        var (collections, _) = FakeServer.For<ICollectionManager>([]);

        return collections;
    }
}
