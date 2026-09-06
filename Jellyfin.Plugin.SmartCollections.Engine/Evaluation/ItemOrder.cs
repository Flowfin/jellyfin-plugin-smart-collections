using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Evaluation;

/// <summary>
/// Puts the items a rule collected in the order the rule declared, ending on the identifier so the
/// order is total.
/// </summary>
/// <remarks>
/// THE IDENTIFIER IS THE LAST TERM AND IT IS NOT OPTIONAL. Every declared term can tie: two films
/// released on one day, two series with one name. An order that stops at the declared terms is
/// therefore partial, and a partial order over a set the server may answer in any sequence
/// produces a different collection on two runs of one rule against one library. #41 asks for the
/// determinism property to be stated precisely enough to test and #44 compares an expected file
/// across two frameworks; neither is possible while the order can tie, which is why the tie-break
/// is here rather than in whatever asserts against it.
///
/// AN ITEM THE LIBRARY HOLDS NO VALUE FOR SORTS LAST, IN BOTH DIRECTIONS. The alternative is to
/// treat absence as a value smaller than every other, which reverses with the direction and puts
/// every film with no premiere date at the top of a descending order - a collection whose first
/// screenful is the items the rule knows least about. Absence is not a value here for the same
/// reason <see cref="ItemFieldReading.IsPresent"/> exists: a comparison against something the
/// library does not hold has no answer, and the answer this file gives instead is a position at
/// the end.
///
/// THE COMPARISON PER SHAPE IS ORDINAL WHERE IT IS TEXT. A collection ordered by name has to come
/// out the same on a server in Ankara as on one in Reykjavik, and a culture-aware comparison is
/// the one thing in the framework that guarantees it does not. That is the same comparison
/// <see cref="ConditionMatcher"/> makes for the same reason.
/// </remarks>
public static class ItemOrder
{
    /// <summary>
    /// Orders the items a rule collected, and takes the first of them where the rule declared a
    /// cap.
    /// </summary>
    /// <param name="items">The items the rule collected, in whatever order they arrived.</param>
    /// <param name="terms">The rule's declared order. May be empty.</param>
    /// <param name="limit">The rule's cap, or <see langword="null"/> where it declared none.</param>
    /// <returns>The identifiers, ordered, and cut to the cap.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="items"/> or <paramref name="terms"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The cap is applied AFTER the order and after everything that decides membership, which is
    /// the only place it can be applied and mean what the document says. A cap pushed into the
    /// server's query would cut the answer before the conditions the query could not carry had
    /// been compared, so a rule asking for fifty films would collect however many of the server's
    /// first fifty survived the post-query stage.
    /// </remarks>
    public static IReadOnlyList<Guid> Take(
        IReadOnlyList<BaseItem> items,
        IReadOnlyList<RuleSortTerm> terms,
        int? limit)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(terms);

        var ordered = new List<BaseItem>(items);
        ordered.Sort((left, right) => Compare(left, right, terms));

        var count = limit is null || limit.Value > ordered.Count ? ordered.Count : limit.Value;
        var taken = new Guid[count];

        for (var index = 0; index < count; index++)
        {
            taken[index] = ordered[index].Id;
        }

        return taken;
    }

    /// <summary>
    /// Compares two items by the rule's declared order, then by the identifier.
    /// </summary>
    /// <param name="left">One item.</param>
    /// <param name="right">The other.</param>
    /// <param name="terms">The rule's declared order.</param>
    /// <returns>The comparison.</returns>
    private static int Compare(BaseItem left, BaseItem right, IReadOnlyList<RuleSortTerm> terms)
    {
        foreach (var term in terms)
        {
            var answer = Compare(
                ItemFieldReader.Read(left, term.Field.Field),
                ItemFieldReader.Read(right, term.Field.Field),
                term.Direction);

            if (answer != 0)
            {
                return answer;
            }
        }

        return string.CompareOrdinal(Key(left.Id), Key(right.Id));
    }

    /// <summary>
    /// Compares two readings of one field.
    /// </summary>
    /// <param name="left">The reading off one item.</param>
    /// <param name="right">The reading off the other.</param>
    /// <param name="direction">The direction the term declares.</param>
    /// <returns>The comparison, with absence sorting after every value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The readings carry a shape no order is declared over.
    /// </exception>
    /// <remarks>
    /// The absence arm is ahead of the dispatch on purpose: it answers for every shape at once, so
    /// a shape added to the vocabulary cannot arrive with an absence rule of its own by accident.
    ///
    /// THE DIRECTION IS APPLIED HERE AND IT IS APPLIED AFTER THE ABSENCE ARM RATHER THAN OVER IT.
    /// Reversing the whole answer reverses the absence rule with it, which puts every item the
    /// library knows nothing about at the top of a descending order. That is what this method
    /// looked like when it was written, and the case that caught it is the one asserting an absent
    /// value sorts last in BOTH directions.
    ///
    /// THE LAST ARM THROWS RATHER THAN ANSWERING, AND IT IS REACHABLE ONLY THROUGH THIS METHOD'S
    /// OWN VISIBILITY. A list-shaped field has no order and <see cref="RuleSortTable"/> refuses a
    /// term naming one, so no document reaches it; a caller that built a term some other way is a
    /// fault in the caller rather than in a document. Internal rather than private for exactly
    /// that: the suite is the only caller outside this file, and what it needs is a reading built
    /// through the constructor <see cref="ItemFieldReading"/> opens wide enough for this proof.
    /// </remarks>
    internal static int Compare(ItemFieldReading left, ItemFieldReading right, RuleSortDirection direction)
    {
        if (!left.IsPresent || !right.IsPresent)
        {
            // Equal where neither holds a value, so two items the rule knows nothing about are
            // separated by the next term rather than by which of them the server named first.
            return left.IsPresent == right.IsPresent ? 0 : (left.IsPresent ? -1 : 1);
        }

        var answer = left.Shape switch
        {
            ItemFieldShape.Text => string.CompareOrdinal(left.Text, right.Text),
            ItemFieldShape.Number => decimal.Compare(left.Number, right.Number),
            ItemFieldShape.Instant => DateTimeOffset.Compare(left.Instant, right.Instant),
            ItemFieldShape.Span => TimeSpan.Compare(left.Span, right.Span),
            _ => throw new ArgumentOutOfRangeException(
                nameof(left),
                left.Shape,
                "No order is declared over this shape. A field whose value is a list is refused by RuleSortTable rather than ordered here.")
        };

        return direction == RuleSortDirection.Descending ? -answer : answer;
    }

    /// <summary>
    /// The sort key of an identifier.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The key.</returns>
    /// <remarks>
    /// The text form rather than the value, because the comparison a reader can reproduce is the
    /// one over the string an expected file would hold. Every identifier renders to the same
    /// thirty-two characters from the same alphabet, so an ordinal comparison over them is total
    /// and is the same on every platform.
    /// </remarks>
    private static string Key(Guid id) => id.ToString("N", CultureInfo.InvariantCulture);
}
