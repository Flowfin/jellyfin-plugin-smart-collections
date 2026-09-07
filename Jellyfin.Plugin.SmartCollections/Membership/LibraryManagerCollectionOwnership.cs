using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// The server as far as deciding which collection a rule owns reaches it.
/// </summary>
/// <remarks>
/// One forward per member and no state, like the membership writer beside it. The engine never
/// holds a <c>BoxSet</c>, so this is where a lookup by mark becomes identifiers with titles and
/// where a create carrying a mark is made.
///
/// <para>
/// THE MARK GOES IN ON THE CREATE, which is what makes it writable at all rather than a preference.
/// <c>CollectionCreationOptions</c> declares a provider dictionary on both lines, so the mark
/// travels with the create instead of arriving in a second write that can fail on its own:
/// </para>
///
/// <code>
/// for ref in v10.11.11 v12.0-rc4; do
///   gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Collections/CollectionCreationOptions.cs?ref=$ref" \
///     --jq .content | base64 -d | grep -nE 'public Dictionary&lt;string, string&gt; ProviderIds'
/// done
/// 26:        public Dictionary&lt;string, string&gt; ProviderIds { get; set; }
/// 26:        public Dictionary&lt;string, string&gt; ProviderIds { get; set; }
/// </code>
///
/// <para>
/// THE RENAME HAS NO SERVER CALL OF ITS OWN AND IT IS THE ONE MEMBER WITH A COST. Nothing on
/// <c>ICollectionManager</c> renames a collection, so this resolves the item, writes the title and
/// saves it through the library manager. Both members are on both lines, at different offsets,
/// which is the ordinary shape of a difference here:
/// </para>
///
/// <code>
/// for ref in v10.11.11 v12.0-rc4; do
///   gh api "repos/jellyfin/jellyfin/contents/MediaBrowser.Controller/Library/ILibraryManager.cs?ref=$ref" \
///     --jq .content | base64 -d | grep -nE 'BaseItem\? GetItemById\(Guid|Task UpdateItemAsync'
/// done
/// 177:        BaseItem? GetItemById(Guid id);
/// 282:        Task UpdateItemAsync(BaseItem item, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken);
/// 203:        BaseItem? GetItemById(Guid id);
/// 332:        Task UpdateItemAsync(BaseItem item, BaseItem parent, ItemUpdateType updateReason, CancellationToken cancellationToken);
/// </code>
///
/// <para>
/// THE SAVE TAKES A PARENT AND THE PORT DOES NOT CARRY ONE, so this resolves it rather than
/// guessing. <c>BaseItem.GetParent()</c> is the obvious answer and is the wrong one here: it reads
/// the static library manager the server assigns at startup rather than the one this adapter was
/// given, so it cannot be reached by anything but a running server. The parent is therefore read
/// through this adapter's own library manager, from the identifier the item carries, and a
/// collection whose parent does not resolve is saved with a null parent. What the server does with
/// the argument is clear the parent folder's cached children and name it in the change event, so a
/// null is a missed cache invalidation rather than a lost write:
/// </para>
///
/// <code>
/// gh api "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/Library/LibraryManager.cs?ref=v10.11.11" \
///   --jq .content | base64 -d | sed -n '2166,2170p'
///             if (parent is Folder folder)
///             {
///                 folder.Children = null;
///                 folder.UserData = null;
///             }
/// </code>
///
/// <para>
/// The update reason is <c>MetadataEdit</c>, which is the reason the server's own item update
/// endpoint saves a renamed item under.
/// </para>
/// </remarks>
public sealed class LibraryManagerCollectionOwnership : ICollectionOwnership
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryManagerCollectionOwnership"/> class.
    /// </summary>
    /// <param name="libraryManager">The server's library manager.</param>
    /// <param name="collectionManager">The server's collection manager.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public LibraryManagerCollectionOwnership(ILibraryManager libraryManager, ICollectionManager collectionManager)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(collectionManager);

        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<CollectionMatch> FindCollections(InternalItemsQuery lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);

        // Handed on exactly as CollectionStamp composed it. Nothing here adds a property, because a
        // lookup narrowed anywhere but in that one place is a mark this plugin looks for in a way
        // no test can read.
        var found = _libraryManager.GetItemList(lookup);

        var answer = new List<CollectionMatch>(found.Count);
        foreach (var item in found)
        {
            answer.Add(new CollectionMatch(item.Id, item.Name));
        }

        return answer;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateCollectionAsync(
        string name,
        IReadOnlyDictionary<string, string> providerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(providerIds);
        cancellationToken.ThrowIfCancellationRequested();

        var options = new CollectionCreationOptions
        {
            Name = name,
            ProviderIds = new Dictionary<string, string>(providerIds, StringComparer.Ordinal),
        };

        var created = await _collectionManager.CreateCollectionAsync(options).ConfigureAwait(false);

        return created.Id;
    }

    /// <inheritdoc />
    public async Task RenameCollectionAsync(Guid collectionId, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        cancellationToken.ThrowIfCancellationRequested();

        var collection = _libraryManager.GetItemById(collectionId);
        if (collection is null)
        {
            // A collection that has gone between the lookup and this write is not a fault of this
            // run: the next resolve finds no mark and creates one, which is the arrangement the
            // resolve already describes for a deleted collection. Renaming nothing is the answer
            // that lets the run reach the next collection.
            return;
        }

        collection.Name = name;

        var parent = collection.ParentId.Equals(default)
            ? null
            : _libraryManager.GetItemById(collection.ParentId);

        await _libraryManager
            .UpdateItemAsync(collection, parent!, ItemUpdateType.MetadataEdit, cancellationToken)
            .ConfigureAwait(false);
    }
}
