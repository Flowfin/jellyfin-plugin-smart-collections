using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// The server as far as changing one collection's membership reaches it.
/// </summary>
/// <remarks>
/// One forward per member and no state, which is what <c>LibraryManagerItemSource</c> is for the
/// one question an evaluation asks. The engine never holds a <c>BaseItem</c> or a <c>BoxSet</c>,
/// so this is where identifiers become server objects and back again, and it is the only place in
/// this plugin where that happens for a membership.
///
/// <para>
/// WHICH SERVER CALL EACH MEMBER IS, read at both lines this plugin ships for rather than inferred
/// from a name. The two files are byte-identical over the three members used here, at the same line
/// numbers:
/// </para>
///
/// <code>
/// for ref in v10.11.11 v12.0-rc4; do
///   gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Collections/ICollectionManager.cs?ref=$ref" \
///     --jq .content | base64 -d | grep -nE 'Task (AddToCollectionAsync|RemoveFromCollectionAsync)'
/// done
/// 42:        Task AddToCollectionAsync(Guid collectionId, IEnumerable&lt;Guid&gt; itemIds);
/// 50:        Task RemoveFromCollectionAsync(Guid collectionId, IEnumerable&lt;Guid&gt; itemIds);
/// </code>
///
/// <para>
/// NEITHER SERVER CALL TAKES A CANCELLATION TOKEN, on either line, which is why every member here
/// checks the token in front of the call rather than passing it on. What that buys is a run that
/// stops between collections and between the halves of one collection's change; what it does not
/// buy is a write that can be interrupted once the server has it, and nothing here pretends
/// otherwise.
/// </para>
///
/// <para>
/// THE RESOLVE IS ONE QUERY RATHER THAN A LOOKUP PER IDENTIFIER, which the port asks for in those
/// words because a refresh over a large library may not issue a number of calls that grows with
/// what it matched. The query type carries the identifiers directly on both lines:
/// </para>
///
/// <code>
/// for ref in v10.11.11 v12.0-rc4; do
///   gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Entities/InternalItemsQuery.cs?ref=$ref" \
///     --jq .content | base64 -d | grep -nE 'public Guid\[\] ItemIds'
/// done
/// 134:        public Guid[] ItemIds { get; set; }
/// 243:        public Guid[] ItemIds { get; set; }
/// </code>
///
/// <para>
/// THE MEMBERSHIP READ IS THE CALL THE SERVER'S OWN COLLECTION MANAGER MAKES FOR THE SAME QUESTION.
/// A collection is a <c>BoxSet</c>, a <c>BoxSet</c> is a <see cref="Folder"/>, and a folder's
/// linked children are readable without a user, which matters because a refresh has none. It is how
/// the server itself decides which of the identifiers it was handed a collection already holds:
/// </para>
///
/// <code>
/// gh api "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/Collections/CollectionManager.cs?ref=v10.11.11" ///   --jq .content | base64 -d | sed -n '214,215p'
///             var linkedChildrenList = collection.GetLinkedChildren();
///             var currentLinkedChildrenIds = linkedChildrenList.Select(i =&gt; i.Id).ToList();
/// </code>
///
/// <para>
/// <c>BoxSet.GetChildren</c> is the member that looks like the answer and is not: it takes a user
/// and sorts by the collection's own display order, so a membership read through it would be a
/// per-user, re-ordered view of what the collection holds rather than what it holds. Reading the
/// <c>LinkedChildren</c> array directly is the other thing that looks like the answer, and it is
/// worse: <c>LinkedChild.Create</c> writes a path and leaves the identifier null, so a member the
/// server added a moment ago is an entry this plugin could not name.
/// </para>
///
/// <para>
/// WHAT THAT CALL COSTS IS WORTH KNOWING, because it is the one place in this adapter where the
/// server object does not use the manager this adapter was given. <c>GetLinkedChildren</c> resolves
/// each entry through <c>BaseItem.LibraryManager</c>, a public static the server assigns at
/// startup. On a running server the two are one object. Off one they are not, which is why the test
/// that reaches this member assigns that static and says so where it does it.
/// </para>
///
/// <code>
/// for ref in v10.11.11 v12.0-rc4; do
///   gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Entities/Folder.cs?ref=$ref" \
///     --jq .content | base64 -d | grep -nE 'public List&lt;BaseItem&gt; GetLinkedChildren\(\)'
/// done
/// 1526:        public List&lt;BaseItem&gt; GetLinkedChildren()
/// 1614:        public List&lt;BaseItem&gt; GetLinkedChildren()
/// </code>
/// </remarks>
public sealed class LibraryManagerMembershipWriter : ICollectionMembershipWriter
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryManagerMembershipWriter"/> class.
    /// </summary>
    /// <param name="libraryManager">The server's library manager.</param>
    /// <param name="collectionManager">The server's collection manager.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public LibraryManagerMembershipWriter(ILibraryManager libraryManager, ICollectionManager collectionManager)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(collectionManager);

        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<Guid> ItemsThatStillResolve(IReadOnlyList<Guid> itemIds)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        if (itemIds.Count == 0)
        {
            // Before the server is asked anything. A query carrying an empty identifier list is not
            // a query for nothing on either line; it is a query with no narrowing on that property,
            // which is the whole library.
            return [];
        }

        var query = new InternalItemsQuery { ItemIds = [.. itemIds] };

        var resolved = new HashSet<Guid>();
        foreach (var item in _libraryManager.GetItemList(query))
        {
            resolved.Add(item.Id);
        }

        // The port promises the subset in the order it was given, and the server answers in whatever
        // order its own store produced. Walking the argument rather than the answer is what keeps
        // that promise without sorting anything.
        var answer = new List<Guid>(resolved.Count);
        foreach (var itemId in itemIds)
        {
            if (resolved.Contains(itemId))
            {
                answer.Add(itemId);
            }
        }

        return answer;
    }

    /// <inheritdoc />
    public IReadOnlyList<Guid> ItemsInCollection(Guid collectionId)
    {
        if (_libraryManager.GetItemById(collectionId) is not Folder collection)
        {
            // Empty rather than a fault, which the port declares. A collection that is not there and
            // one that is not a folder are the same answer to a caller building a diff: nothing is
            // held, so everything the rule matched is an addition.
            return [];
        }

        var answer = new List<Guid>();
        foreach (var child in collection.GetLinkedChildren())
        {
            answer.Add(child.Id);
        }

        return answer;
    }

    /// <inheritdoc />
    public async Task AddToCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        cancellationToken.ThrowIfCancellationRequested();

        await _collectionManager.AddToCollectionAsync(collectionId, itemIds).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveFromCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        cancellationToken.ThrowIfCancellationRequested();

        await _collectionManager.RemoveFromCollectionAsync(collectionId, itemIds).ConfigureAwait(false);
    }
}
