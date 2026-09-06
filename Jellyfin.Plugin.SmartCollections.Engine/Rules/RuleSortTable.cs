using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The sort vocabulary: the directions a term may be written in, and which fields a rule may
/// order by.
/// </summary>
/// <remarks>
/// THE SORTABLE SET IS DECLARED AS ITS COMPLEMENT, and that is the choice this table is about. A
/// field can be ordered where the value read off an item is a single comparable thing, and cannot
/// where it is a LIST: an item carrying three genres has no place in an order over genres unless
/// something invents one, and the inventions available - the first member, the shortest, the list
/// joined into one string - are each a different order and none of them is what a document says.
/// So the exceptions are named here and everything else is sortable, which means a field added to
/// the vocabulary is sortable by default. That default is wrong for a list-shaped field, and what
/// catches it is not this file: the suite reads the shape <c>ItemFieldReader</c> produces for every
/// declared field and compares it against this table in both directions, so a list-shaped field
/// added without a line here reds rather than silently entering an order nothing can produce.
///
/// The direction names are here rather than in an enumeration attribute for the reason every other
/// vocabulary in this tree gives: a document writes names, a refusal lists names, and
/// <c>docs/rule-sort.md</c> carries a section per name, all read from one place.
/// </remarks>
public static class RuleSortTable
{
    /// <summary>
    /// The member a sort term names its field in.
    /// </summary>
    public const string FieldMember = "field";

    /// <summary>
    /// The member a sort term names its direction in.
    /// </summary>
    public const string DirectionMember = "direction";

    private static readonly RuleField[] ListShaped = [RuleField.Genres, RuleField.Tags];

    private static readonly (RuleSortDirection Direction, string Name)[] Directions =
    [
        (RuleSortDirection.Ascending, "ascending"),
        (RuleSortDirection.Descending, "descending")
    ];

    /// <summary>
    /// Gets the fields a rule may order by, in the order the field table declares them.
    /// </summary>
    public static IReadOnlyList<RuleFieldRow> Fields { get; } =
        RuleFieldTable.Rows.Where(row => Array.IndexOf(ListShaped, row.Field) < 0).ToArray();

    /// <summary>
    /// Gets the fields a rule may not order by, in the order the field table declares them.
    /// </summary>
    /// <remarks>
    /// The list the page and the suite compare against, and the reason this table declares the
    /// complement rather than the set.
    /// </remarks>
    public static IReadOnlyList<RuleFieldRow> UnsortableFields { get; } =
        RuleFieldTable.Rows.Where(row => Array.IndexOf(ListShaped, row.Field) >= 0).ToArray();

    /// <summary>
    /// Gets the names a sort term may write as its field, comma separated, in the field table's
    /// order.
    /// </summary>
    public static string WrittenFieldNames => string.Join(", ", Fields.Select(row => row.Name));

    /// <summary>
    /// Gets the names a sort term may write as its direction, comma separated.
    /// </summary>
    public static string WrittenDirections => string.Join(", ", Directions.Select(row => row.Name));

    /// <summary>
    /// Answers whether a rule may order by a field.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> where a term may name it.</returns>
    public static bool CanSortBy(RuleField field) => Array.IndexOf(ListShaped, field) < 0;

    /// <summary>
    /// Returns the direction a name writes, or <see langword="null"/> where no direction has it.
    /// </summary>
    /// <param name="name">The name, as a document wrote it.</param>
    /// <returns>The direction, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Ordinal and case sensitive, like every other name this format reads. A document is written
    /// once and read by a machine, and folding case here would make the fold's rules part of the
    /// format on the day somebody writes a name in a language whose fold is not the invariant one.
    /// </remarks>
    public static RuleSortDirection? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var row in Directions)
        {
            if (string.Equals(row.Name, name, StringComparison.Ordinal))
            {
                return row.Direction;
            }
        }

        return null;
    }

    /// <summary>
    /// The name a direction is written under.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <returns>Its name.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> has no name.</exception>
    public static string NameOf(RuleSortDirection direction)
    {
        foreach (var row in Directions)
        {
            if (row.Direction == direction)
            {
                return row.Name;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(direction),
            direction,
            "No row names this direction. A direction added to the vocabulary owes one here.");
    }

    /// <summary>
    /// Refuses a term naming a field whose value is a list.
    /// </summary>
    /// <param name="field">The field's row.</param>
    /// <param name="pointer">Where the term names it.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is <see langword="null"/>.</exception>
    public static RuleValidationError RefuseUnsortableField(RuleFieldRow field, string pointer)
    {
        ArgumentNullException.ThrowIfNull(field);

        return new RuleValidationError(
            pointer,
            string.Create(
                CultureInfo.InvariantCulture,
                $"A rule cannot be ordered by \"{field.Name}\", because an item holds a list of them rather than one value, and every order over a list is an order somebody invented. The fields a rule may order by are {WrittenFieldNames}."));
    }
}
