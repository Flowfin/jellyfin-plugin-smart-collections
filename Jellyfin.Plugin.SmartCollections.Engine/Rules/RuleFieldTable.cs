using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The declared field vocabulary, one row per field.
/// </summary>
/// <remarks>
/// The table is the authority for which fields exist, what type each one holds, which operators
/// each one accepts and how each one reaches the library. Nothing derives any of that by
/// reflecting over a class, which is what both existing plugins in this space do and is why
/// neither of them can list its own legal field set back to the person writing a rule.
///
/// The comparisons here are ordinal. A field name is a wire token rather than a word in a
/// language, so a server's locale cannot decide whether a document names one.
///
/// No row declares <see cref="RuleValueType.Enumeration"/>. A field of that type owes a column
/// naming the values it accepts, because <c>RuleValueParser.ReadEnumeration</c> is handed that
/// list, and no field in this first vocabulary has one. The column arrives with the first field
/// that needs it rather than being carried empty by ten rows that do not.
///
/// THE TWO DATE FIELDS DECLARE <see cref="RuleOperator.WithinLast"/>, AND NO ROW COULD DECLARE IT
/// UNTIL 2026-08-30. The operator table carried one type column, read as the type the FIELD
/// declares, and <c>withinLast</c> put <see cref="RuleValueType.Duration"/> in it while its own
/// semantics sentence describes a field holding an instant. So a date field declaring it was
/// refused by the cross-table check in the suite, a duration field declaring it would have asked
/// whether a length of time is inside a span ending now, and the operator was unreachable from
/// every rule anyone could write. The repair was the operator table's rather than this one's: it
/// declares the field end and the value end separately now, <c>withinLast</c> applies to a
/// <see cref="RuleValueType.Date"/> field and takes a <see cref="RuleValueType.Duration"/> beside
/// it, and <c>dateAdded withinLast P30D</c> is a condition this vocabulary can express.
///
/// The cross-table check reads the field end, so a row declaring an operator that does not apply
/// to the type the row holds still reds the suite. What changed is which of the two ends it asks
/// about, not that it asks.
/// </remarks>
public static class RuleFieldTable
{
    /// <summary>
    /// The operators an ordered scalar accepts.
    /// </summary>
    private static readonly RuleOperator[] Ordering =
    [
        RuleOperator.GreaterThan,
        RuleOperator.GreaterThanOrEqual,
        RuleOperator.LessThan,
        RuleOperator.LessThanOrEqual
    ];

    /// <summary>
    /// The operators a date accepts.
    /// </summary>
    /// <remarks>
    /// <c>withinLast</c> is the one of the three that does not take a date beside it. It applies
    /// to a field holding an instant and takes a length of time, which is the row the operator
    /// table declares two type columns for.
    /// </remarks>
    private static readonly RuleOperator[] Instant =
    [
        RuleOperator.Before,
        RuleOperator.After,
        RuleOperator.WithinLast
    ];

    /// <summary>
    /// The operators a field holding several strings accepts.
    /// </summary>
    /// <remarks>
    /// Membership rather than substring. <c>contains</c> over a list asks whether the list holds
    /// the value, which is what the operator's own sentence says and what somebody writing
    /// <c>genres contains Thriller</c> means. <c>in</c> and <c>notIn</c> are absent for the
    /// opposite reason: over a list they would ask whether the whole list is one of several
    /// values, which nobody means and which reads as though it asked the useful question.
    /// </remarks>
    private static readonly RuleOperator[] Membership =
    [
        RuleOperator.Contains,
        RuleOperator.NotContains,
        RuleOperator.IsEmpty,
        RuleOperator.IsNotEmpty
    ];

    /// <summary>
    /// The operators a single string the library may leave unset accepts.
    /// </summary>
    private static readonly RuleOperator[] OptionalText =
    [
        RuleOperator.Equals,
        RuleOperator.NotEquals,
        RuleOperator.In,
        RuleOperator.NotIn,
        RuleOperator.IsEmpty,
        RuleOperator.IsNotEmpty
    ];

    /// <summary>
    /// The operators free text accepts.
    /// </summary>
    /// <remarks>
    /// No equality. Two paragraphs of prose are never compared for being exactly one string by
    /// anybody who meant it, and offering the comparison would make a rule that silently matches
    /// nothing look like a rule that was written correctly.
    /// </remarks>
    private static readonly RuleOperator[] FreeText =
    [
        RuleOperator.Contains,
        RuleOperator.NotContains,
        RuleOperator.StartsWith,
        RuleOperator.EndsWith,
        RuleOperator.IsEmpty,
        RuleOperator.IsNotEmpty
    ];

    /// <summary>
    /// The operators a title accepts.
    /// </summary>
    private static readonly RuleOperator[] Title =
    [
        RuleOperator.Equals,
        RuleOperator.NotEquals,
        RuleOperator.Contains,
        RuleOperator.NotContains,
        RuleOperator.StartsWith,
        RuleOperator.EndsWith,
        RuleOperator.In,
        RuleOperator.NotIn
    ];

    /// <summary>
    /// The operators a whole number accepts.
    /// </summary>
    private static readonly RuleOperator[] Counted =
    [
        RuleOperator.Equals,
        RuleOperator.NotEquals,
        RuleOperator.In,
        RuleOperator.NotIn,
        RuleOperator.GreaterThan,
        RuleOperator.GreaterThanOrEqual,
        RuleOperator.LessThan,
        RuleOperator.LessThanOrEqual
    ];

    private static readonly RuleFieldRow[] Table =
    [
        new(
            RuleField.CommunityRating,
            "communityRating",
            RuleValueType.Decimal,
            Ordering,
            "MinCommunityRating",
            "The rating the community gives the item, out of ten."),
        new(
            RuleField.DateAdded,
            "dateAdded",
            RuleValueType.Date,
            Instant,
            "MinDateCreated",
            "When the server first saw the item."),
        new(
            RuleField.Genres,
            "genres",
            RuleValueType.String,
            Membership,
            "Genres",
            "The genres the item carries."),
        new(
            RuleField.Name,
            "name",
            RuleValueType.String,
            Title,
            "Name",
            "The title the library holds for the item."),
        new(
            RuleField.OfficialRating,
            "officialRating",
            RuleValueType.String,
            OptionalText,
            "OfficialRatings",
            "The age classification the item carries."),
        new(
            RuleField.Overview,
            "overview",
            RuleValueType.String,
            FreeText,
            null,
            "The description the library holds for the item."),
        new(
            RuleField.PremiereDate,
            "premiereDate",
            RuleValueType.Date,
            Instant,
            "MinPremiereDate",
            "When the item was first released."),
        new(
            RuleField.ProductionYear,
            "productionYear",
            RuleValueType.Integer,
            Counted,
            "Years",
            "The year the item was produced."),
        new(
            RuleField.Runtime,
            "runtime",
            RuleValueType.Duration,
            Ordering,
            null,
            "How long the item runs for."),
        new(
            RuleField.Tags,
            "tags",
            RuleValueType.String,
            Membership,
            "Tags",
            "The tags the item carries.")
    ];

    private static readonly Dictionary<string, RuleFieldRow> ByName = BuildIndex();

    /// <summary>
    /// Gets every row, in the order the table declares them.
    /// </summary>
    public static IReadOnlyList<RuleFieldRow> Rows => Table;

    /// <summary>
    /// Gets every field name a document may write, sorted as a refusal lists them.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = SortedNames();

    /// <summary>
    /// Returns the row for a field.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>Its row.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="field"/> has no row.</exception>
    public static RuleFieldRow Of(RuleField field)
    {
        foreach (var row in Table)
        {
            if (row.Field == field)
            {
                return row;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(field), field, "No row is declared for this field.");
    }

    /// <summary>
    /// Finds the row a document's field name refers to.
    /// </summary>
    /// <param name="name">The name as the document wrote it.</param>
    /// <returns>The row, or <see langword="null"/> where no field has that name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static RuleFieldRow? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByName.TryGetValue(name, out var row) ? row : null;
    }

    /// <summary>
    /// Answers whether a field accepts an operator.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="operator">The operator.</param>
    /// <returns><see langword="true"/> where the field accepts it.</returns>
    public static bool Accepts(RuleField field, RuleOperator @operator)
    {
        foreach (var accepted in Of(field).Operators)
        {
            if (accepted == @operator)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The refusal for a field name no field has.
    /// </summary>
    /// <param name="name">The name as the document wrote it.</param>
    /// <param name="pointer">Where the name is, as a JSON Pointer.</param>
    /// <returns>The refusal, naming the name and every legal one.</returns>
    /// <remarks>
    /// Every legal name, rather than the nearest one. A list is what somebody repairing a
    /// document needs and it is the same list every time, where a nearest match is a guess that
    /// reads as an instruction and is wrong exactly when the writer had a different field in mind.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static RuleValidationError RefuseUnknownField(string name, string pointer)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new RuleValidationError(
            pointer,
            string.Create(
                CultureInfo.InvariantCulture,
                $"There is no field called \"{name}\". The fields are {string.Join(", ", Names)}."));
    }

    private static Dictionary<string, RuleFieldRow> BuildIndex()
    {
        var index = new Dictionary<string, RuleFieldRow>(StringComparer.Ordinal);

        foreach (var row in Table)
        {
            index.Add(row.Name, row);
        }

        return index;
    }

    private static string[] SortedNames()
    {
        var names = new string[Table.Length];

        for (var i = 0; i < Table.Length; i++)
        {
            names[i] = Table[i].Name;
        }

        Array.Sort(names, StringComparer.Ordinal);

        return names;
    }
}
