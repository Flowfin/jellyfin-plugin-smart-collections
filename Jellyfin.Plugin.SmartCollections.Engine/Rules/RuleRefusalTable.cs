using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The constructs this rule language refuses, and the names a document writes that reach for one.
/// </summary>
/// <remarks>
/// ABSENT AND REFUSED ARE DIFFERENT ANSWERS, AND THIS TABLE IS THE ONLY PLACE THAT CAN TELL THEM
/// APART. A field the vocabulary does not hold is absent: adding it is a row and a test, and the
/// refusal a document gets for writing it says so by listing the fields that do exist. A refused
/// construct is one this plugin has decided against, with the reason written down, and a document
/// writing it gets the same list plus a sentence naming what it ran into. Without the second
/// sentence the two are one message, and somebody who wrote <c>isPlayed</c> reads it as an
/// omission and opens a request for a row that will not be added.
///
/// The refusals themselves live in <c>docs/rule-language.md</c>, one heading each with its
/// reason. This table adds nothing to that set and takes nothing from it: what it holds is the
/// vocabulary a person reaches for when they want one, which that page does not carry because it
/// is written for a reader rather than for a lookup.
///
/// THE NAME LIST IS A FLOOR AND NEVER A SET. A refused construct written under a spelling nobody
/// has written down here is refused exactly as before, as an unknown field or an unknown
/// operator, with no refusal named. That is the failure mode this table degrades into, and it is
/// the safe one: the document is refused either way and what is lost is a sentence of
/// explanation. Adding a spelling is one entry and no other change.
///
/// The comparisons are ordinal, for the reason the field table gives about its own: a name in a
/// document is a wire token rather than a word in a language, so a server's locale cannot decide
/// whether one was written.
/// </remarks>
public static class RuleRefusalTable
{
    /// <summary>
    /// Where the refusals are argued, named in every message this table produces.
    /// </summary>
    public const string Reference = "docs/rule-language.md";

    private static readonly RuleRefusalRow[] TableRows =
    [
        new(
            "regular expressions",
            ["matches", "notMatches", "regex", "regexMatches", "like", "notLike"],
            "Matching by regular expression is refused: a pattern with catastrophic backtracking runs on a server task thread and stops the server doing anything else. Write contains, startsWith, endsWith, equals or in instead."),
        new(
            "arbitrary expressions",
            ["expression", "expr", "eval", "script", "code", "lambda"],
            "A condition that carries code is refused: nothing here compiles a document into a delegate, because a compiled expression can be inspected only by running it."),
        new(
            "cross-item aggregates",
            ["count", "countOf", "countGreaterThan", "countLessThan", "countEquals", "groupBy", "having", "aggregate"],
            "Counting or grouping across items is refused for this version: it needs a second pass over the library for each candidate item, which turns one refresh into a quadratic walk."),
        new(
            "references between collections",
            ["inCollection", "notInCollection", "collection", "collections", "inList", "notInList"],
            "Reading another collection is refused: collections are outputs, and a rule that read one would make the order collections refresh in significant and let two of them oscillate."),
        new(
            "fields describing one person's viewing",
            ["isPlayed", "played", "isWatched", "watched", "playCount", "isFavorite", "isFavourite", "favorite", "favourite", "userRating", "lastPlayedDate", "userData"],
            "A field describing one person's viewing is refused for this version: a Jellyfin collection is server-wide, so a list built from one account's state is a surprise for every other account that sees it."),
        new(
            "pinning an item into a collection",
            ["pinned", "pin", "pins", "alwaysInclude", "alwaysExclude"],
            "Pinning an item into a collection is refused: membership comes from the rule, so a second source of membership would take away the one answer to why an item is here.")
    ];

    private static readonly Dictionary<string, RuleRefusalRow> ByName = BuildIndex();

    /// <summary>
    /// Gets every refusal this table declares, in the order <c>docs/rule-language.md</c> heads
    /// them.
    /// </summary>
    /// <remarks>
    /// SIX ROWS RATHER THAN THE SEVEN THAT PAGE CARRIES, and the missing one is deliberate. The
    /// wall clock as an implicit input is refused of the ENGINE rather than of a document: there
    /// is no member, name or value a rule document can write to ask for it, because relative
    /// dates are allowed and the thing refused is reading the clock during a match. What holds
    /// that refusal is the compiler taking the instant as an argument, so a table of names has
    /// nothing to hold for it and carrying an empty row would suggest otherwise.
    /// </remarks>
    public static IReadOnlyList<RuleRefusalRow> Rows => TableRows;

    /// <summary>
    /// Finds the refusal a written name reaches for.
    /// </summary>
    /// <param name="name">The name as the document wrote it.</param>
    /// <returns>The refusal, or <see langword="null"/> where the name reaches for none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static RuleRefusalRow? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByName.TryGetValue(name, out var row) ? row : null;
    }

    /// <summary>
    /// The sentence a refusal message adds where the written name reaches for a refused construct.
    /// </summary>
    /// <param name="name">The name as the document wrote it.</param>
    /// <returns>
    /// The sentence, with a leading space so it appends to an existing message, or the empty
    /// string where the name reaches for no refusal.
    /// </returns>
    /// <remarks>
    /// A suffix rather than a replacement, because the message it appends to is the one that says
    /// what the document may write instead, and somebody repairing a document needs both: what
    /// they ran into, and the list they are choosing from.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static string Note(string name)
    {
        var row = Find(name);

        return row is null
            ? string.Empty
            : " " + row.Message + " The refusal is \"" + row.Refusal + "\" in " + Reference + ".";
    }

    private static Dictionary<string, RuleRefusalRow> BuildIndex()
    {
        var index = new Dictionary<string, RuleRefusalRow>(StringComparer.Ordinal);

        foreach (var row in TableRows)
        {
            foreach (var name in row.Names)
            {
                index.Add(name, row);
            }
        }

        return index;
    }
}
