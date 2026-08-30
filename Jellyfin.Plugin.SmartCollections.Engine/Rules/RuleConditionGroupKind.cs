namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The three ways a group combines what it holds.
/// </summary>
/// <remarks>
/// Three and no more. Conjunction, disjunction and negation cover what a rule needs, and putting
/// the negation on the group rather than on each condition keeps it in one place a reader can
/// see: a document with a <c>not</c> on individual conditions hides the negation among the
/// conditions, and a reader checking what a rule collects has to hold every one of them in their
/// head at once.
///
/// A member added here owes a name in <see cref="RuleCompositionReader"/> and a section in
/// <c>docs/rule-composition.md</c>, and the suite refuses one that has neither.
/// </remarks>
public enum RuleConditionGroupKind
{
    /// <summary>
    /// Everything the group holds matches.
    /// </summary>
    All,

    /// <summary>
    /// At least one of the things the group holds matches.
    /// </summary>
    Any,

    /// <summary>
    /// Nothing the group holds matches.
    /// </summary>
    None
}
