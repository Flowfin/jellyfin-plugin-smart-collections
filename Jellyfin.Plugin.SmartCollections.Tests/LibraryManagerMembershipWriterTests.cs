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
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The server side of the port a refresh changes a collection's membership through.
/// </summary>
/// <remarks>
/// Every member is asserted here against a stand-in for the server's own two managers rather than
/// against a running one, which is what <see cref="FakeServer"/> is for and why the paragraph in
/// <c>docs/testing.md</c> that said this adapter could not be tested no longer says so.
///
/// What is NOT claimed is that the server behaves as the stand-in does. These tests say which
/// server member each port member reaches, with what arguments, and what it does with the answer.
/// Whether <c>ICollectionManager.AddToCollectionAsync</c> then writes what this plugin expects is a
/// property of the server, read out of its source in the adapter's own remarks and observed by
/// nothing here.
/// </remarks>
public class LibraryManagerMembershipWriterTests
{
    private static readonly Guid Collection = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid First = Guid.Parse("00000001-0000-0000-0000-000000000000");
    private static readonly Guid Second = Guid.Parse("00000002-0000-0000-0000-000000000000");
    private static readonly Guid Third = Guid.Parse("00000003-0000-0000-0000-000000000000");

    /// <summary>
    /// The port promises one query rather than a lookup per identifier, because a refresh over a
    /// large library may not issue a number of calls that grows with what it matched. An adapter
    /// that looped would pass every other assertion in this file.
    /// </summary>
    [Fact]
    public void TheResolveAsksTheServerOnceAndCarriesEveryIdentifierInThatOneQuery()
    {
        InternalItemsQuery? asked = null;
        var (library, recorder) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemList"] = args =>
            {
                asked = (InternalItemsQuery)args[0]!;
                return (IReadOnlyList<BaseItem>)[Film(First), Film(Third)];
            },
        });

        var writer = new LibraryManagerMembershipWriter(library, Collections());

        var resolved = writer.ItemsThatStillResolve([First, Second, Third]);

        Assert.Equal(["GetItemList"], recorder.Calls);
        Assert.Equal([First, Second, Third], asked!.ItemIds);
        Assert.Equal([First, Third], resolved);
    }

    /// <summary>
    /// The port promises the subset in the order it was given, and the server answers in whatever
    /// order its own store produced. An adapter that handed the server's answer straight back would
    /// pass the test above, because the fixture there answers in the order it was asked.
    /// </summary>
    [Fact]
    public void TheResolveAnswersInTheOrderItWasAskedRatherThanTheOrderTheServerAnsweredIn()
    {
        var (library, _) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemList"] = _ => (IReadOnlyList<BaseItem>)[Film(Third), Film(First), Film(Second)],
        });

        var writer = new LibraryManagerMembershipWriter(library, Collections());

        Assert.Equal([First, Second, Third], writer.ItemsThatStillResolve([First, Second, Third]));
    }

    /// <summary>
    /// An identifier list with nothing in it is not a query for nothing on either line: it is a
    /// query with no narrowing on that property, which is the whole library. The adapter therefore
    /// asks nothing at all, and this is the leg that says so rather than reading an answer.
    /// </summary>
    [Fact]
    public void TheResolveOfNothingAsksTheServerNothing()
    {
        var (library, recorder) = FakeServer.For<ILibraryManager>([]);

        var writer = new LibraryManagerMembershipWriter(library, Collections());

        Assert.Empty(writer.ItemsThatStillResolve([]));
        Assert.Empty(recorder.Calls);
    }

    /// <summary>
    /// The membership read is the current side of a diff, and it reads the collection's linked
    /// children rather than asking for its children through a member that takes a user and sorts by
    /// the collection's own display order.
    /// </summary>
    /// <remarks>
    /// THIS IS THE ONE TEST IN THE FILE THAT ASSIGNS A SERVER STATIC, and it does it because the
    /// adapter's remarks say the call it makes reaches one: <c>Folder.GetLinkedChildren</c> resolves
    /// each entry through <c>BaseItem.LibraryManager</c>, which the server sets at startup. Handing
    /// the stand-in to that static is what lets the resolution happen against a fake rather than
    /// against a running server, and it is also the assertion that the resolution goes through a
    /// library manager at all.
    ///
    /// The static is put back in a <c>finally</c>, and xUnit runs the methods of one class one at a
    /// time, so no other test in this class sees it set. What is NOT claimed is isolation from the
    /// rest of the suite: the assignment is process-wide while it is in force, and what makes that
    /// safe today is that nothing else in this suite reads that static. A test added elsewhere that
    /// does would have to be written knowing this one exists.
    ///
    /// <para>
    /// THE TWO LINES RESOLVE THOSE ENTRIES DIFFERENTLY, which the stand-in reported by refusing a
    /// member this test had not named. 10.11 asks for one item per entry and 12.0 collects the
    /// identifiers and asks for them in one query:
    /// </para>
    ///
    /// <code>
    /// gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Entities/Folder.cs?ref=v10.11.11"     ///   --jq .content | base64 -d | sed -n '1531,1533p'
    ///             foreach (var i in linkedChildren)
    ///             {
    ///                 var child = GetLinkedChild(i);
    /// gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Entities/Folder.cs?ref=v12.0-rc4"     ///   --jq .content | base64 -d | sed -n '1754,1757p'
    ///                 var batched = LibraryManager.GetItemList(new InternalItemsQuery
    ///                 {
    ///                     ItemIds = [.. idsToBatch]
    ///                 });
    /// </code>
    ///
    /// <para>
    /// So both members are answered here and the call sequence is not asserted: it is the server's
    /// and it differs between the packages. What is still asserted is that the adapter reached no
    /// THIRD member, because the stand-in refuses one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheMembershipReadAnswersWhatTheCollectionsLinkedChildrenAre()
    {
        var films = new Dictionary<Guid, BaseItem>
        {
            [First] = Film(First),
            [Second] = Film(Second),
        };

        var collection = new BoxSet { Id = Collection };
        collection.LinkedChildren =
        [
            new LinkedChild { ItemId = First },
            new LinkedChild { ItemId = Second },
        ];

        var (library, recorder) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemById"] = args => Equals(args[0], Collection)
                ? collection
                : films.GetValueOrDefault((Guid)args[0]!),
            ["GetItemList"] = args =>
            {
                var wanted = ((InternalItemsQuery)args[0]!).ItemIds;

                return (IReadOnlyList<BaseItem>)[.. wanted.Where(films.ContainsKey).Select(id => films[id])];
            },
        });

        var was = BaseItem.LibraryManager;
        try
        {
            BaseItem.LibraryManager = library;

            var writer = new LibraryManagerMembershipWriter(library, Collections());

            var held = writer.ItemsInCollection(Collection);

            Assert.Equal(2, held.Count);
            Assert.Empty(held.Except([First, Second]));
        }
        finally
        {
            BaseItem.LibraryManager = was;
        }

        Assert.Contains("GetItemById", recorder.Calls);
        Assert.Empty(recorder.Calls.Distinct().Except(["GetItemById", "GetItemList"]));
    }

    /// <summary>
    /// The port declares that a collection which is not there answers empty rather than throwing,
    /// because a run whose collection was deleted between the resolve and the read is a run whose
    /// diff adds everything.
    /// </summary>
    [Fact]
    public void TheMembershipReadOfACollectionThatIsNotThereIsEmpty()
    {
        var (library, _) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemById"] = _ => null,
        });

        var writer = new LibraryManagerMembershipWriter(library, Collections());

        Assert.Empty(writer.ItemsInCollection(Collection));
    }

    /// <summary>
    /// An identifier that resolves to something which is not a folder answers the same way. It is a
    /// state the server is entitled to be in, and a cast that threw would fail a run over every
    /// other collection beside it.
    /// </summary>
    [Fact]
    public void TheMembershipReadOfSomethingThatIsNotAFolderIsEmpty()
    {
        var (library, _) = FakeServer.For<ILibraryManager>(new()
        {
            ["GetItemById"] = _ => Film(First),
        });

        var writer = new LibraryManagerMembershipWriter(library, Collections());

        Assert.Empty(writer.ItemsInCollection(Collection));
    }

    /// <summary>
    /// The add reaches the server's own batch add, with the collection and the identifiers it was
    /// given, and asks the library manager nothing.
    /// </summary>
    [Fact]
    public async Task TheAddReachesTheCollectionManagersOwnBatchAdd()
    {
        object?[] passed = [];
        var (collections, recorder) = FakeServer.For<ICollectionManager>(new()
        {
            ["AddToCollectionAsync"] = args =>
            {
                passed = args;
                return Task.CompletedTask;
            },
        });

        var (library, libraryCalls) = FakeServer.For<ILibraryManager>([]);
        var writer = new LibraryManagerMembershipWriter(library, collections);

        await writer.AddToCollectionAsync(Collection, [First, Second], CancellationToken.None);

        Assert.Equal(["AddToCollectionAsync"], recorder.Calls);
        Assert.Empty(libraryCalls.Calls);
        Assert.Equal(Collection, passed[0]);
        Assert.Equal([First, Second], ((IEnumerable<Guid>)passed[1]!).ToArray());
    }

    /// <summary>
    /// The remove reaches the server's own batch remove, the same way.
    /// </summary>
    [Fact]
    public async Task TheRemoveReachesTheCollectionManagersOwnBatchRemove()
    {
        object?[] passed = [];
        var (collections, recorder) = FakeServer.For<ICollectionManager>(new()
        {
            ["RemoveFromCollectionAsync"] = args =>
            {
                passed = args;
                return Task.CompletedTask;
            },
        });

        var (library, _) = FakeServer.For<ILibraryManager>([]);
        var writer = new LibraryManagerMembershipWriter(library, collections);

        await writer.RemoveFromCollectionAsync(Collection, [Second], CancellationToken.None);

        Assert.Equal(["RemoveFromCollectionAsync"], recorder.Calls);
        Assert.Equal(Collection, passed[0]);
        Assert.Equal([Second], ((IEnumerable<Guid>)passed[1]!).ToArray());
    }

    /// <summary>
    /// Neither server call takes a cancellation token on either line, so the token is checked in
    /// front of the call. What that buys is a run that stops between writes; what it cannot buy is
    /// a write interrupted once the server has it, and this asserts the half that exists.
    /// </summary>
    /// <param name="write">Which of the two writes is being cancelled.</param>
    /// <returns>The assertion.</returns>
    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task ACancelledRunReachesNeitherWrite(string write)
    {
        var (collections, recorder) = FakeServer.For<ICollectionManager>([]);
        var (library, _) = FakeServer.For<ILibraryManager>([]);
        var writer = new LibraryManagerMembershipWriter(library, collections);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => string.Equals(write, "add", StringComparison.Ordinal)
                ? writer.AddToCollectionAsync(Collection, [First], cancelled.Token)
                : writer.RemoveFromCollectionAsync(Collection, [First], cancelled.Token));

        Assert.Empty(recorder.Calls);
    }

    /// <summary>
    /// The stand-in refuses a member no test named, which is what makes every assertion above an
    /// assertion that the adapter touched nothing else. A stand-in answering a member it was not
    /// given would leave those tests saying only that the named call happened.
    /// </summary>
    [Fact]
    public void TheStandInRefusesAServerMemberNoTestNamed()
    {
        var (library, _) = FakeServer.For<ILibraryManager>([]);

        var refusal = Assert.Throws<NotSupportedException>(() => library.GetItemById(Collection));

        Assert.Contains("ILibraryManager.GetItemById", refusal.Message, StringComparison.Ordinal);
    }

    private static ICollectionManager Collections()
    {
        var (collections, _) = FakeServer.For<ICollectionManager>([]);

        return collections;
    }

    private static BaseItem Film(Guid id) => new Movie { Id = id, Name = "Film" };
}
