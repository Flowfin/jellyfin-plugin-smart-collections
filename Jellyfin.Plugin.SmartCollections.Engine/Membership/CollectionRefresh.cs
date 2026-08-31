using System;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// One collection and the change a refresh has to make to it.
/// </summary>
/// <remarks>
/// A refresh covers several collections at once, and the pairing has to survive the run rather than
/// being carried alongside it, because a fault is recorded against a collection and a list of
/// diffs on its own cannot say which collection the third one belonged to.
///
/// The rule is named as well as the collection, and the two are not interchangeable. A collection
/// an operator deletes comes back under the same mark with a new identifier, which
/// <c>CollectionResolverTests.ARuleWhoseCollectionWasDeletedComesBackUnderTheSameMark</c> asserts,
/// so a record keyed on the collection loses that rule's past at exactly that moment. The rule's
/// identity is what a run is remembered under; the collection identifier is what the writes go to.
/// </remarks>
public sealed class CollectionRefresh
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionRefresh"/> class.
    /// </summary>
    /// <param name="ruleId">The rule this change was derived from, as its document declares it.</param>
    /// <param name="collectionId">The collection to change.</param>
    /// <param name="diff">What changing it means.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="ruleId"/> or <paramref name="diff"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The identity is refused for being absent and for nothing else, which is the same bound
    /// <see cref="CollectionStamp.For(string)"/> keeps: what an id may be made of is decided by the
    /// validator, on the document, and a second opinion here would disagree with it the day that
    /// set moves.
    /// </remarks>
    public CollectionRefresh(string ruleId, Guid collectionId, MembershipDiff diff)
    {
        ArgumentNullException.ThrowIfNull(ruleId);
        ArgumentNullException.ThrowIfNull(diff);

        RuleId = ruleId;
        CollectionId = collectionId;
        Diff = diff;
    }

    /// <summary>
    /// Gets the identity of the rule this change was derived from.
    /// </summary>
    public string RuleId { get; }

    /// <summary>
    /// Gets the collection this change is about.
    /// </summary>
    public Guid CollectionId { get; }

    /// <summary>
    /// Gets what the refresh has to change about it.
    /// </summary>
    public MembershipDiff Diff { get; }
}
