using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// The three server calls a refresh makes to change what one collection holds.
/// </summary>
/// <remarks>
/// Narrower than <c>ICollectionManager</c> on purpose. The server's own interface carries a create,
/// a folder lookup and a per-user collapse that a refresh never touches, and one of its members
/// exists on 12.0 and not on 10.11, so an engine written against it would carry a surface it does
/// not use and a shape that differs between the two packages. What a refresh needs is an add, a
/// remove and a way to ask which identifiers a server would still resolve, and those three are the
/// whole of this port.
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
