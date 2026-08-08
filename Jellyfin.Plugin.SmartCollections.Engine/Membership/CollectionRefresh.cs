using System;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// One collection and the change a refresh has to make to it.
/// </summary>
/// <remarks>
/// A refresh covers several collections at once, and the pairing has to survive the run rather than
/// being carried alongside it, because a fault is recorded against a collection and a list of
/// diffs on its own cannot say which collection the third one belonged to.
/// </remarks>
public sealed class CollectionRefresh
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionRefresh"/> class.
    /// </summary>
    /// <param name="collectionId">The collection to change.</param>
    /// <param name="diff">What changing it means.</param>
    /// <exception cref="ArgumentNullException"><paramref name="diff"/> is <see langword="null"/>.</exception>
    public CollectionRefresh(Guid collectionId, MembershipDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);

        CollectionId = collectionId;
        Diff = diff;
    }

    /// <summary>
    /// Gets the collection this change is about.
    /// </summary>
    public Guid CollectionId { get; }

    /// <summary>
    /// Gets what the refresh has to change about it.
    /// </summary>
    public MembershipDiff Diff { get; }
}
