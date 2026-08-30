namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The operators a rule may name.
/// </summary>
/// <remarks>
/// The set is closed and it is declared here. Both existing plugins in this space derive part of
/// their operator set from a .NET enum at runtime, so whatever
/// <c>System.Linq.Expressions.ExpressionType</c> parses is an operator: the legal set is then a
/// framework detail, it changes when the framework does, it cannot be documented because nobody
/// wrote it, and it cannot be held stable across versions because nobody chose it.
///
/// A member added here owes a row in <see cref="RuleOperatorTable"/> and a section in
/// <c>docs/rule-operators.md</c>, and the suite refuses one that has neither. That is what makes
/// the set closed in practice rather than in a sentence.
///
/// <c>matchRegex</c> is deliberately absent. <c>docs/rule-language.md</c> carries the refusal and
/// its reason, and the replacements it names are <see cref="Contains"/>, <see cref="StartsWith"/>,
/// <see cref="EndsWith"/>, <see cref="Equals"/> and <see cref="In"/>.
/// </remarks>
public enum RuleOperator
{
    /// <summary>
    /// The field is exactly the value.
    /// </summary>
    Equals,

    /// <summary>
    /// The field is anything other than the value.
    /// </summary>
    NotEquals,

    /// <summary>
    /// The field holds the value somewhere inside it.
    /// </summary>
    Contains,

    /// <summary>
    /// The field holds the value nowhere inside it.
    /// </summary>
    NotContains,

    /// <summary>
    /// The field begins with the value.
    /// </summary>
    StartsWith,

    /// <summary>
    /// The field ends with the value.
    /// </summary>
    EndsWith,

    /// <summary>
    /// The field is one of the values in the list.
    /// </summary>
    In,

    /// <summary>
    /// The field is none of the values in the list.
    /// </summary>
    NotIn,

    /// <summary>
    /// The field is above the value.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// The field is the value or above it.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// The field is below the value.
    /// </summary>
    LessThan,

    /// <summary>
    /// The field is the value or below it.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// The field holds nothing.
    /// </summary>
    IsEmpty,

    /// <summary>
    /// The field holds something.
    /// </summary>
    IsNotEmpty,

    /// <summary>
    /// The field is earlier than the value.
    /// </summary>
    Before,

    /// <summary>
    /// The field is later than the value.
    /// </summary>
    After,

    /// <summary>
    /// The field is inside the span that ends at the instant the evaluation was given.
    /// </summary>
    WithinLast
}
