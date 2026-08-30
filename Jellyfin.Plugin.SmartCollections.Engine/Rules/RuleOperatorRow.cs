using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One operator, as the table declares it.
/// </summary>
/// <remarks>
/// Four things and no more: which operator this is, what a document writes to name it, which
/// value types it accepts, and what it means in one sentence.
///
/// The written name is declared rather than derived from the member. Deriving it would make the
/// wire format a property of a C# identifier, so renaming the member for a compiler warning would
/// silently break every rule document on every server.
///
/// A row whose <see cref="ValueTypes"/> is empty is an operator that takes no value at all. That
/// is not a gap in the table: <c>isEmpty</c> and <c>isNotEmpty</c> ask about the field alone, and
/// an empty set is how the table says so in a form something can refuse.
/// </remarks>
public sealed class RuleOperatorRow
{
    internal RuleOperatorRow(
        RuleOperator @operator,
        string name,
        IReadOnlyList<RuleValueType> valueTypes,
        string semantics)
    {
        Operator = @operator;
        Name = name;
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
    /// Gets the value types it accepts, in the order a refusal lists them. Empty where the
    /// operator takes no value.
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
