using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// The server calls a refresh makes to read and change what one collection holds.
/// </summary>
/// <remarks>
/// Narrower than <c>ICollectionManager</c> on purpose. The server's own interface carries a create,
/// a folder lookup and a per-user collapse that a refresh never touches, and one of its members
/// exists on 12.0 and not on 10.11, so an engine written against it would carry a surface it does
/// not use and a shape that differs between the two packages. What a refresh needs is an add, a
/// remove, a way to ask which identifiers a server would still resolve, and a way to ask what the
/// collection holds at this moment, and those four are the whole of this port.
///
/// <para>
/// THE MEMBERSHIP READ IS HERE RATHER THAN ON <see cref="ICollectionOwnership"/>, decided on #263.
/// Both placements work and the argument is about what each port is a port OF. Ownership answers
/// which collection a rule owns and what it is called, which are questions about a collection's
/// identity; this one answers what a collection holds, which is the subject the diff is computed
/// over and then applied to. Putting the read on ownership would make a caller hold two ports to
/// build one <see cref="MembershipDiff"/> and then the second to apply it, so the two halves of
/// one operation would be split across the seam rather than at it.
/// </para>
///
/// <para>
/// The objection worth writing down is that a port named for writing now carries two reads. It
/// carried one already: <see cref="ItemsThatStillResolve"/> asks the server a question and writes
/// nothing. So this is a port about a collection's membership that happens to be the only one that
/// writes, and the second read is the smaller move.
/// </para>
///
/// The engine therefore never holds a <c>BaseItem</c>. Everything here is a
/// <see cref="Guid"/>, which is also what makes a fake of this port a class with three methods and
/// a dictionary rather than a stand-in for the server's item model.
/// </remarks>
public interface ICollectionMembershipWriter
{
    /// <summary>
    /// Asks which of these identifiers the server still resolves to an item.
    /// </summary>
    /// <param name="itemIds">The identifiers to ask about.</param>
    /// <returns>
    /// The subset that still resolves, in the order it was given. An implementation answers this
    /// with one query rather than a lookup per identifier, because a refresh over a large library
    /// may not issue a number of calls that grows with the size of what it matched.
    /// </returns>
    IReadOnlyList<Guid> ItemsThatStillResolve(IReadOnlyList<Guid> itemIds);

    /// <summary>
    /// Asks what a collection holds at this moment.
    /// </summary>
    /// <param name="collectionId">The collection to read.</param>
    /// <returns>
    /// The identifiers the collection holds, in whatever order the server answered in, and empty
    /// where the collection holds nothing or is not there at all.
    /// </returns>
    /// <remarks>
    /// This is the <c>current</c> side of <see cref="MembershipDiff.Between"/>. Without it a caller
    /// building a diff has to reach past this port for the one thing the diff is computed against,
    /// which is how a second way of reading a collection's membership gets written.
    ///
    /// The order is explicitly not promised, for the reason <see cref="ICollectionOwnership"/> does
    /// not promise the order of a lookup: it is the order the server's own store produced, and a
    /// diff is a comparison of two sets. An implementation that sorted here would be adding a
    /// property no caller asked for and every caller would then be free to depend on.
    ///
    /// A collection that is not there answers empty rather than throwing. A run whose collection
    /// was deleted between the resolve and the read is a run whose diff adds everything, which is
    /// what the resolve's own remarks describe happening under a new identifier, and it is a
    /// better answer than a fault for a state the server is entitled to be in.
    /// </remarks>
    IReadOnlyList<Guid> ItemsInCollection(Guid collectionId);

    /// <summary>
    /// Adds items to a collection.
    /// </summary>
    /// <param name="collectionId">The collection to add to.</param>
    /// <param name="itemIds">The items to add, none of which the collection already holds.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the collection holds the items.</returns>
    /// <remarks>
    /// The server's add resolves every identifier before it assigns anything, so it either adds
    /// the whole batch or throws having written nothing. An implementation that loses that
    /// property, by looping over the batch one call at a time, turns a failure into a partly
    /// written collection and defeats what <see cref="MembershipApplier"/> is for.
    /// </remarks>
    Task AddToCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken);

    /// <summary>
    /// Removes items from a collection.
    /// </summary>
    /// <param name="collectionId">The collection to remove from.</param>
    /// <param name="itemIds">The items to remove, all of which the collection holds.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the collection no longer holds the items.</returns>
    /// <remarks>
    /// The items are taken out of the collection and are otherwise untouched. Nothing here deletes
    /// an item, edits one, or reaches the files behind one.
    /// </remarks>
    Task RemoveFromCollectionAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, CancellationToken cancellationToken);
}
