using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// The three server calls that decide which collection a rule owns and what it is called.
/// </summary>
/// <remarks>
/// Narrow for the same reason <see cref="ICollectionMembershipWriter"/> is. The server's
/// <c>ICollectionManager</c> carries a create, an add, a remove, a per-user collapse and a folder
/// lookup, and one of its members exists on 12.0 and not on 10.11. What resolving a rule to a
/// collection needs is a lookup by mark, a create carrying one, and a title write for the case
/// where the rule's name has moved since the create, and those three are the whole of this port.
///
/// The engine therefore never holds a <c>BoxSet</c>. A lookup answers with identifiers and the
/// titles beside them, and a create answers with an identifier, which is also what makes a fake
/// of this port a list of records rather than a stand-in for the server's item model.
///
/// <para>
/// NO MEMBER HERE REMOVES A COLLECTION OR TAKES A MARK OFF ONE, and that is the property
/// <c>docs/uninstall.md</c> rests on rather than the count above.
/// <see cref="RenameCollectionAsync"/> writes a title onto a collection that already carries the
/// mark it was found by; it cannot reach a collection this plugin did not create, because the
/// only identifier it is ever handed came out of a lookup by mark.
/// </para>
///
/// The query is passed in rather than built here, because what the lookup asks is a property of
/// this plugin's mark rather than of the server: <see cref="CollectionStamp.LookupQuery"/> is the
/// one place it is decided, and an implementation that composed its own would be a second copy of
/// that decision.
///
/// <para>
/// NOTHING IN THIS TREE IMPLEMENTS THIS PORT AGAINST A SERVER YET, and the same is true of
/// <see cref="ICollectionMembershipWriter"/>, which has stood without one since it was declared.
/// Both adapters are one call each over <c>ILibraryManager</c> and <c>ICollectionManager</c>, and
/// neither can be executed by this suite: an <c>ILibraryManager</c> is eighty-four members and
/// holding a real one means a running server, which is what <c>docs/testing.md</c> refuses for a
/// unit-level property. They therefore arrive with the first trigger that runs a refresh, which
/// needs both at once. What this suite holds instead is the decision each port is in front of, at
/// <see cref="CollectionResolver"/> and <see cref="MembershipApplier"/>, and neither of those
/// touches a server type.
/// </para>
/// </remarks>
public interface ICollectionOwnership
{
    /// <summary>
    /// Asks the server which collections carry a mark.
    /// </summary>
    /// <param name="lookup">The query, from <see cref="CollectionStamp.LookupQuery"/>.</param>
    /// <returns>
    /// The collections the mark is on with the title each one currently carries, in whatever
    /// order the server answered in, and empty where no collection carries it.
    /// </returns>
    /// <remarks>
    /// Every match rather than one, and the order is explicitly not promised. A port that answered
    /// with one would be choosing which collection a rule owns inside an adapter over a server,
    /// where no test in this suite can reach the choice; answering with all of them puts that
    /// decision in <see cref="CollectionResolver"/>, which is a value in and a value out.
    /// </remarks>
    IReadOnlyList<CollectionMatch> FindCollections(InternalItemsQuery lookup);

    /// <summary>
    /// Creates a collection carrying a mark.
    /// </summary>
    /// <param name="name">What the collection is called, as the rule declares it.</param>
    /// <param name="providerIds">The mark, from <see cref="CollectionStamp.For"/>.</param>
    /// <param name="cancellationToken">Cancels the create.</param>
    /// <returns>The identifier of the collection that was created.</returns>
    /// <remarks>
    /// The mark is written by the create rather than onto the collection afterwards. A create
    /// followed by a second call that stamps it can fail between the two, and what it leaves is a
    /// collection this plugin made and cannot recognise: the next run finds no mark, creates
    /// another, and an operator gets a second copy of their collection on every failure.
    /// </remarks>
    Task<Guid> CreateCollectionAsync(
        string name,
        IReadOnlyDictionary<string, string> providerIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gives a collection this rule already owns the title the rule declares.
    /// </summary>
    /// <param name="collectionId">The collection, as <see cref="FindCollections"/> answered it.</param>
    /// <param name="name">What the collection is to be called, as the rule declares it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the title has been written.</returns>
    /// <remarks>
    /// The name is the only thing this writes. It does not touch the provider dictionary, so the
    /// mark the collection was found by is the mark it still carries afterwards, and a run that
    /// dies between the lookup and this call leaves a collection with an old title rather than an
    /// unrecognisable one.
    ///
    /// It exists because the alternative is worse in a way an operator cannot see. Without it a
    /// rule whose declared name is edited resolves to the collection it already owned, under the
    /// title it was created with: no second collection appears, which is what the mark is for, and
    /// the edit simply never reaches the library. A plugin that silently ignores half of what a
    /// rule document says is harder to debug than one that gets it wrong loudly.
    ///
    /// The server's own rename is a property set and a metadata save rather than a call taking a
    /// cancellation token, so the token is carried here and the adapter checks it in front of the
    /// write, which is the same arrangement <see cref="CreateCollectionAsync"/> is under.
    /// </remarks>
    Task RenameCollectionAsync(Guid collectionId, string name, CancellationToken cancellationToken);
}
