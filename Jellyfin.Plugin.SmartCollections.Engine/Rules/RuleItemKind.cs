namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The kinds of item a rule may collect.
/// </summary>
/// <remarks>
/// Two members rather than the thirty-seven the server's own enumeration declares, and the
/// narrowness is the point. A rule says what it collects, and the accepted set is a declared list
/// in this plugin rather than whatever the server's enumeration happens to parse: a legal set
/// derived from a framework enumeration changes when the framework does, cannot be documented,
/// and cannot be validated against.
///
/// Which kinds the first version accepts was decided on 2026-08-24 as question 10 of #67, and the
/// answer is films and series. Widening it later is one member here, one row in
/// <see cref="RuleItemKindTable"/>, one section in <c>docs/rule-fields.md</c> and one line in the
/// expected file the suite compares the server's enumeration against - which is what the table
/// exists to make cheap.
///
/// A member here is this plugin's own name for a kind and never the server's number. The number a
/// package compiles into a query is the position of a member in the server's enumeration, so the
/// row carries the server's member and the suite holds the whole of that enumeration to a
/// checked-in ordered list. A server line that inserts a member above the ones named here moves
/// every value after it while every name is still present.
/// </remarks>
public enum RuleItemKind
{
    /// <summary>
    /// A film.
    /// </summary>
    Movie,

    /// <summary>
    /// A series, which is the show rather than any of its seasons or episodes.
    /// </summary>
    Series
}
