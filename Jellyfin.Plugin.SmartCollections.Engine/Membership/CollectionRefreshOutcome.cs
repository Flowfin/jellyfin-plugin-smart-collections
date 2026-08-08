using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// What a refresh did to one collection, and where it stopped if it stopped.
/// </summary>
/// <remarks>
/// A refresh over several collections has no single answer. One collection can be brought to the
/// state its rule describes while the next one throws, and an administrator asking why a
/// collection is wrong needs the reason recorded against that collection rather than a run that
/// reports one verdict for all of them.
/// </remarks>
public sealed class CollectionRefreshOutcome
{
    internal CollectionRefreshOutcome(
        Guid collectionId,
        IReadOnlyList<Guid> added,
        IReadOnlyList<Guid> removed,
        IReadOnlyList<Guid> dropped,
        Exception? fault)
    {
        CollectionId = collectionId;
        Added = added;
        Removed = removed;
        Dropped = dropped;
        Fault = fault;
    }

    /// <summary>
    /// Gets the collection this outcome is about.
    /// </summary>
    public Guid CollectionId { get; }

    /// <summary>
    /// Gets the items that reached the collection, in ascending order.
    /// </summary>
    /// <remarks>
    /// Empty where the add threw, because the server's add resolves the whole batch before it
    /// assigns anything and a batch that throws has written nothing.
    /// </remarks>
    public IReadOnlyList<Guid> Added { get; }

    /// <summary>
    /// Gets the items taken out of the collection, in ascending order.
    /// </summary>
    public IReadOnlyList<Guid> Removed { get; }

    /// <summary>
    /// Gets the items the rule matched that no longer resolve to an item, in ascending order.
    /// </summary>
    /// <remarks>
    /// An item deleted between the query and the write is a normal event on a live server rather
    /// than a fault, so it is dropped and named here instead of failing the collection. It is
    /// named rather than silently discarded because a rule whose matches keep vanishing is worth
    /// seeing.
    /// </remarks>
    public IReadOnlyList<Guid> Dropped { get; }

    /// <summary>
    /// Gets what stopped the refresh of this collection, or <see langword="null"/> where nothing did.
    /// </summary>
    /// <remarks>
    /// The reason an administrator reads is this exception's message. It is carried whole rather
    /// than reduced to a string here, because the surface that reports it decides how much of it
    /// to show and cannot recover what a string threw away.
    /// </remarks>
    public Exception? Fault { get; }

    /// <summary>
    /// Gets a value indicating whether the collection reached the state its rule describes.
    /// </summary>
    public bool Succeeded => Fault is null;
}
