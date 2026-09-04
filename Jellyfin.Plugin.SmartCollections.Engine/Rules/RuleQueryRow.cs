using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One pair of a field and an operator that the server's own item query answers, as the compile
/// table declares it.
/// </summary>
/// <remarks>
/// The field table names the property a field is ABOUT. This table is narrower: it names the
/// PAIRS the query answers, because a field the query knows still has operators the query cannot
/// express. <c>name</c> reaches the library through <c>Name</c> and the query offers no way to ask
/// for a title that ends with something, so the row for <c>name equals</c> is here and the row for
/// <c>name endsWith</c> is not.
///
/// A row carries its write rather than naming it, and that is a decision about proof rather than
/// about taste. A compiler switching over pairs has one arm per row plus an arm for a pair with no
/// arm, and that last one is unreachable while the table and the switch agree - so it is either
/// unexecuted code in a tree whose coverage is read on every run, or a fixture that has to corrupt
/// the table to reach it. A row that carries its own write has no such arm: the pair with no write
/// is the pair with no row.
///
/// A row names every property it writes, and most name one. A pair whose sentence is a span
/// between two instants writes the floor and the ceiling the server carries for that field, and
/// the compiler claims both, so a second condition writing either of them is refused the way a
/// second condition writing a single property is.
///
/// Every write is handed the instant the evaluation was given, whether or not it reads it. The
/// one that does is the pair whose value is a length of time rather than an instant: the span it
/// declares ends at that instant, and there is no other instant for it to end at, because the
/// engine reads no clock and <c>ambient-clock-in-the-engine</c> refuses one arriving.
/// </remarks>
public sealed class RuleQueryRow
{
    private readonly Func<InternalItemsQuery, IReadOnlyList<RuleValue>, DateTimeOffset, bool> _write;

    internal RuleQueryRow(
        RuleField field,
        RuleOperator @operator,
        string queryProperty,
        string semantics,
        Func<InternalItemsQuery, IReadOnlyList<RuleValue>, bool> write)
        : this(field, @operator, [queryProperty], semantics, (query, values, _) => write(query, values))
    {
    }

    internal RuleQueryRow(
        RuleField field,
        RuleOperator @operator,
        string[] queryProperties,
        string semantics,
        Func<InternalItemsQuery, IReadOnlyList<RuleValue>, DateTimeOffset, bool> write)
    {
        Field = field;
        Operator = @operator;
        QueryProperties = queryProperties;
        Semantics = semantics;
        _write = write;
    }

    /// <summary>
    /// Gets the field this row is for.
    /// </summary>
    public RuleField Field { get; }

    /// <summary>
    /// Gets the operator this row is for.
    /// </summary>
    public RuleOperator Operator { get; }

    /// <summary>
    /// Gets the names of the properties on the server's item query that this pair writes, in the
    /// order the row writes them.
    /// </summary>
    /// <remarks>
    /// Two rows may name one property, and that is the case the compiler decides rather than the
    /// case this table forbids: <c>officialRating equals</c> and <c>officialRating in</c> both
    /// write the same property, and a document writing either of them is ordinary. What is
    /// refused is one document writing both, because the query holds one value for that property
    /// and the second write would replace the first without saying so.
    ///
    /// A row naming two properties is the same case twice over: <c>premiereDate withinLast</c>
    /// writes the floor <c>premiereDate after</c> writes and the ceiling <c>premiereDate before</c>
    /// writes, so a document carrying it beside either of those is refused on the property they
    /// share.
    /// </remarks>
    public IReadOnlyList<string> QueryProperties { get; }

    /// <summary>
    /// Gets, in one sentence, what the query is asked once this row has written to it.
    /// </summary>
    public string Semantics { get; }

    /// <summary>
    /// Writes this pair's narrowing onto a query, where the values can be carried by the
    /// properties the row names.
    /// </summary>
    /// <param name="query">The query to narrow.</param>
    /// <param name="values">The values the condition wrote, already parsed.</param>
    /// <param name="evaluatedAt">
    /// The instant the evaluation was given. A pair whose value is a span ends the span here; every
    /// other pair is handed it and leaves it unread.
    /// </param>
    /// <returns>
    /// <see langword="true"/> where the narrowing was written, and <see langword="false"/> where
    /// this pair's properties cannot carry these particular values at this instant.
    /// </returns>
    /// <remarks>
    /// The false answer is about the VALUES and never about the pair. A production year outside
    /// the range the query's own year array holds, an instant with no room for the tick that
    /// turns an at-or-after comparison into an after one, and a span that reaches back past the
    /// first instant a date can name are all documents this plugin accepts and these properties
    /// cannot express. The compiler hands such a condition on rather than dropping it, because a
    /// narrowing that is quietly not applied is a rule that means something else.
    ///
    /// A row that answers false has written nothing. A row writing two properties decides before
    /// it writes either, so a query never carries half of a pair.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="values"/> is <see langword="null"/>.</exception>
    public bool TryWrite(InternalItemsQuery query, IReadOnlyList<RuleValue> values, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(values);

        return _write(query, values, evaluatedAt);
    }
}
