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
/// </remarks>
public sealed class RuleQueryRow
{
    private readonly Func<InternalItemsQuery, IReadOnlyList<RuleValue>, bool> _write;

    internal RuleQueryRow(
        RuleField field,
        RuleOperator @operator,
        string queryProperty,
        string semantics,
        Func<InternalItemsQuery, IReadOnlyList<RuleValue>, bool> write)
    {
        Field = field;
        Operator = @operator;
        QueryProperty = queryProperty;
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
    /// Gets the name of the property on the server's item query that this pair writes.
    /// </summary>
    /// <remarks>
    /// Two rows may name one property, and that is the case the compiler decides rather than the
    /// case this table forbids: <c>officialRating equals</c> and <c>officialRating in</c> both
    /// write the same property, and a document writing either of them is ordinary. What is
    /// refused is one document writing both, because the query holds one value for that property
    /// and the second write would replace the first without saying so.
    /// </remarks>
    public string QueryProperty { get; }

    /// <summary>
    /// Gets, in one sentence, what the query is asked once this row has written to it.
    /// </summary>
    public string Semantics { get; }

    /// <summary>
    /// Writes this pair's narrowing onto a query, where the values can be carried by the property
    /// the row names.
    /// </summary>
    /// <param name="query">The query to narrow.</param>
    /// <param name="values">The values the condition wrote, already parsed.</param>
    /// <returns>
    /// <see langword="true"/> where the narrowing was written, and <see langword="false"/> where
    /// this pair's property cannot carry these particular values.
    /// </returns>
    /// <remarks>
    /// The false answer is about the VALUES and never about the pair. A production year outside
    /// the range the query's own year array holds, and an instant with no room for the tick that
    /// turns an at-or-after comparison into an after one, are both documents this plugin accepts
    /// and this property cannot express. The compiler hands such a condition on rather than
    /// dropping it, because a narrowing that is quietly not applied is a rule that means something
    /// else.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> or <paramref name="values"/> is <see langword="null"/>.</exception>
    public bool TryWrite(InternalItemsQuery query, IReadOnlyList<RuleValue> values)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(values);

        return _write(query, values);
    }
}
