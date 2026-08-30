using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One operator, as the table declares it.
/// </summary>
/// <remarks>
/// Five things and no more: which operator this is, what a document writes to name it, which types
/// of field it applies to, which types of value it takes beside it, and what it means in one
/// sentence.
///
/// The written name is declared rather than derived from the member. Deriving it would make the
/// wire format a property of a C# identifier, so renaming the member for a compiler warning would
/// silently break every rule document on every server.
///
/// THE TWO TYPE COLUMNS ARE TWO QUESTIONS AND THEY WERE ONE COLUMN UNTIL 2026-08-30. A condition
/// has two ends - the field it names and the value written beside it - and for sixteen of the
/// seventeen rows those two hold the same type, which is how one column carried both meanings for
/// as long as it did. <c>withinLast</c> is the row where they differ: it applies to a field
/// holding an instant and takes a length of time beside it, which is what its own semantics
/// sentence describes. Under one column it declared <see cref="RuleValueType.Duration"/>, no date
/// field could declare it without the cross-table check refusing the row, and the operator was
/// unreachable from every rule anyone could write.
///
/// A row whose <see cref="ValueTypes"/> is empty is an operator that takes no value at all. That
/// is not a gap in the table: <c>isEmpty</c> and <c>isNotEmpty</c> ask about the field alone, and
/// an empty set is how the table says so in a form something can refuse. Their
/// <see cref="FieldTypes"/> is every declared type rather than empty, because they apply to a
/// field of any type; the empty set belongs on the value end alone, and putting it on both was the
/// same conflation one column along.
/// </remarks>
public sealed class RuleOperatorRow
{
    internal RuleOperatorRow(
        RuleOperator @operator,
        string name,
        IReadOnlyList<RuleValueType> fieldTypes,
        IReadOnlyList<RuleValueType> valueTypes,
        string semantics)
    {
        Operator = @operator;
        Name = name;
        FieldTypes = fieldTypes;
        ValueTypes = valueTypes;
        Semantics = semantics;
    }

    /// <summary>
    /// Gets the operator this row declares.
    /// </summary>
    public RuleOperator Operator { get; }

    /// <summary>
    /// Gets the name a rule document writes to name it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the types of field it applies to, in the order a refusal lists them. Never empty: an
    /// operator applying to no field at all is one no rule could ever name.
    /// </summary>
    public IReadOnlyList<RuleValueType> FieldTypes { get; }

    /// <summary>
    /// Gets the types of value it takes beside it, in the order a refusal lists them. Empty where
    /// the operator takes no value.
    /// </summary>
    public IReadOnlyList<RuleValueType> ValueTypes { get; }

    /// <summary>
    /// Gets what it means, in one sentence.
    /// </summary>
    public string Semantics { get; }

    /// <summary>
    /// Gets a value indicating whether the operator is written with a value beside it.
    /// </summary>
    public bool TakesAValue => ValueTypes.Count > 0;
}
