using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// What a refresh has to change to turn a collection's current membership into the membership a
/// rule asks for.
/// </summary>
/// <remarks>
/// A refresh is not a rebuild. Emptying a collection and filling it again is simpler to write and
/// is wrong: every mutation writes the item to the repository and queues a metadata refresh, so
/// rebuilding a thousand-item collection costs a thousand writes to change nothing. What a refresh
/// needs is the difference, and the difference is what this type is.
///
/// Three lists rather than two, because the third is not the leftovers. An item that stays is an
/// item nothing is done to, and naming it is what lets a caller report a refresh that changed
/// nothing without inferring it from two empty lists.
///
/// The lists are sorted, so the same two memberships produce the same diff whatever order either
/// arrived in. The order is <see cref="Guid"/>'s own, which is a total order over the value and
/// reads no culture and no clock, so a server in one locale and a server in another derive the
/// same lists from the same inputs. That is what makes a diff comparable against a checked-in
/// expected file rather than only against itself.
///
/// Membership is a SET. An identifier repeated in either input appears once in the answer, because
/// a collection either holds an item or does not, and a duplicate in a caller's list is a fact
/// about the list rather than about the collection.
/// </remarks>
public sealed class MembershipDiff
{
    private MembershipDiff(
        IReadOnlyList<Guid> added,
        IReadOnlyList<Guid> removed,
        IReadOnlyList<Guid> unchanged)
    {
        Added = added;
        Removed = removed;
        Unchanged = unchanged;
    }

    /// <summary>
    /// Gets the items the rule matches that the collection does not hold yet, in ascending order.
    /// </summary>
    public IReadOnlyList<Guid> Added { get; }

    /// <summary>
    /// Gets the items the collection holds that the rule no longer matches, in ascending order.
    /// </summary>
    /// <remarks>
    /// An item here is taken out of the collection and is otherwise untouched: never retagged,
    /// never edited and never deleted. Only the membership of the collection changes.
    ///
    /// An item an administrator added to the collection by hand is in this list too, because the
    /// rule is the only source of membership. That is a consequence of the design rather than a
    /// fault in it, and it belongs in the documentation rather than in a surprise.
    /// </remarks>
    public IReadOnlyList<Guid> Removed { get; }

    /// <summary>
    /// Gets the items the collection holds that the rule still matches, in ascending order.
    /// </summary>
    public IReadOnlyList<Guid> Unchanged { get; }

    /// <summary>
    /// Gets a value indicating whether applying this diff would change the collection.
    /// </summary>
    /// <remarks>
    /// A diff over an unchanged collection is empty however many items that collection holds, so
    /// this reads the two lists that describe work rather than the size of the membership.
    /// </remarks>
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    /// <summary>
    /// Derives what a refresh has to change.
    /// </summary>
    /// <param name="current">
    /// The items the collection holds now, in any order and with any repetition.
    /// </param>
    /// <param name="target">
    /// The items the rule matches, in any order and with any repetition.
    /// </param>
    /// <returns>The three lists a refresh acts on.</returns>
    /// <exception cref="ArgumentNullException">
    /// Either argument is <see langword="null"/>.
    /// </exception>
    public static MembershipDiff Between(IEnumerable<Guid> current, IEnumerable<Guid> target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);

        var held = new HashSet<Guid>(current);
        var wanted = new HashSet<Guid>(target);

        var added = wanted.Where(id => !held.Contains(id)).ToList();

        var removed = new List<Guid>();
        var unchanged = new List<Guid>();
        foreach (var id in held)
        {
            if (wanted.Contains(id))
            {
                unchanged.Add(id);
            }
            else
            {
                removed.Add(id);
            }
        }

        // Sorted here rather than left in the order the sets happened to enumerate in. A hash set
        // enumerates in insertion order only by accident of its current implementation, so a diff
        // that did not sort would depend on the order a caller's query returned its rows, which is
        // the one thing a rule engine claiming determinism may not do.
        added.Sort();
        removed.Sort();
        unchanged.Sort();

        return new MembershipDiff(added, removed, unchanged);
    }
}
