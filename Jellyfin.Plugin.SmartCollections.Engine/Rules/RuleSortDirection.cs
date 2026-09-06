namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The directions a rule may sort in.
/// </summary>
/// <remarks>
/// Two members, written out rather than expressed as a flag beside the field, because a document
/// says what it means in words: a rule writing <c>descending</c> reads as a sentence and a rule
/// writing <c>reverse: true</c> reads as a setting on something.
///
/// There is no third member for the order the server would have answered in. That is what #39
/// exists to remove: a collection whose order is whatever the query returned changes every time
/// the plugin rewrites it, and an order nobody declared cannot be reproduced from the document.
/// </remarks>
public enum RuleSortDirection
{
    /// <summary>
    /// Smallest first: the earliest instant, the lowest number, the shortest length, and text in
    /// the order the ordinal comparison puts it.
    /// </summary>
    Ascending,

    /// <summary>
    /// Largest first, which is the reverse of <see cref="Ascending"/> over the items that carry a
    /// value. It does not move the items that carry none: those sort last in both directions.
    /// </summary>
    Descending
}
