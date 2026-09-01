using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The declared item kind vocabulary, one row per kind a rule may collect.
/// </summary>
/// <remarks>
/// The table is the authority for which kinds exist and which member of the server's own
/// enumeration each one selects. It is declared for the reason <see cref="RuleFieldTable"/> is:
/// nothing here reflects over a framework enumeration, so the legal set can be listed back to the
/// person writing a rule and does not move when a server line adds a kind.
///
/// The comparisons here are ordinal. A kind name is a wire token rather than a word in a
/// language, so a server's locale cannot decide whether a document names one.
///
/// WHICH KINDS A FIELD APPLIES TO IS NOT THIS TABLE. This one says what a RULE may collect; a
/// column saying which kinds a FIELD means anything for belongs on the field table, and every
/// field in the first vocabulary applies to both rows here. <see cref="RuleField"/> records that
/// absence and names the issue that owns it.
/// </remarks>
public static class RuleItemKindTable
{
    private static readonly RuleItemKindRow[] Table =
    [
        new(
            RuleItemKind.Movie,
            "movie",
            BaseItemKind.Movie,
            "A film."),
        new(
            RuleItemKind.Series,
            "series",
            BaseItemKind.Series,
            "A series, which is the show rather than any of its seasons or episodes.")
    ];

    private static readonly Dictionary<string, RuleItemKindRow> ByName = BuildIndex();

    /// <summary>
    /// Gets every row, in the order the table declares them.
    /// </summary>
    /// <remarks>
    /// This order is what a compiled query is built in, so two documents naming the same kinds in
    /// different orders narrow a query identically. A rule's own order is not carried anywhere,
    /// because it means nothing: a scope is a set.
    /// </remarks>
    public static IReadOnlyList<RuleItemKindRow> Rows => Table;

    /// <summary>
    /// Gets every kind name a document may write, in the order a refusal lists them.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = DeclaredNames();

    /// <summary>
    /// Gets the legal names as a refusal writes them, comma separated in the table's own order.
    /// </summary>
    public static string WrittenNames => string.Join(", ", Names);

    /// <summary>
    /// Returns the row for a kind.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <returns>Its row.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> has no row.</exception>
    public static RuleItemKindRow Of(RuleItemKind kind)
    {
        foreach (var row in Table)
        {
            if (row.Kind == kind)
            {
                return row;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(kind), kind, "No row is declared for this item kind.");
    }

    /// <summary>
    /// Finds the row a document's kind name refers to.
    /// </summary>
    /// <param name="name">The name as the document wrote it.</param>
    /// <returns>The row, or <see langword="null"/> where no kind has that name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static RuleItemKindRow? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByName.TryGetValue(name, out var row) ? row : null;
    }

    /// <summary>
    /// The refusal for a kind name no kind has.
    /// </summary>
    /// <param name="name">The name as the document wrote it.</param>
    /// <param name="pointer">Where the name is, as a JSON Pointer.</param>
    /// <returns>The refusal, naming the name and every legal one.</returns>
    /// <remarks>
    /// Every legal name, rather than the nearest one, for the reason
    /// <see cref="RuleFieldTable.RefuseUnknownField"/> gives: a list is what somebody repairing a
    /// document needs, and a nearest match is a guess that reads as an instruction.
    ///
    /// The list is short enough that this refusal is also where an operator learns what the first
    /// version collects, which is why the same sentence is what the absent-member refusal writes.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static RuleValidationError RefuseUnknownKind(string name, string pointer)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new RuleValidationError(
            pointer,
            string.Create(
                CultureInfo.InvariantCulture,
                $"There is no item kind called \"{name}\". The kinds a rule may collect are {WrittenNames}."));
    }

    private static Dictionary<string, RuleItemKindRow> BuildIndex()
    {
        var index = new Dictionary<string, RuleItemKindRow>(Table.Length, StringComparer.Ordinal);

        foreach (var row in Table)
        {
            index.Add(row.Name, row);
        }

        return index;
    }

    private static string[] DeclaredNames()
    {
        var names = new string[Table.Length];

        for (var index = 0; index < Table.Length; index++)
        {
            names[index] = Table[index].Name;
        }

        return names;
    }
}
