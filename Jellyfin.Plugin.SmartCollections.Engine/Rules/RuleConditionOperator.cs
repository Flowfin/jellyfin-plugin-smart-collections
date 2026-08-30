namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One condition, with the field row its <c>field</c> member names and the operator row its
/// <c>operator</c> member names.
/// </summary>
/// <remarks>
/// The rows rather than the names they were written with, for the reason
/// <see cref="RuleConditionField"/> gives about its own: the stage after this one asks what type
/// the value beside the condition is written as and whether there is a value at all, and both of
/// those are columns of these two rows.
///
/// The field row is carried through rather than looked up again. The stage before this one has
/// already resolved it, and resolving it twice would give one condition two chances to be resolved
/// differently.
/// </remarks>
/// <param name="Pointer">Where the condition is in the document, as a JSON Pointer.</param>
/// <param name="Field">The field the condition names.</param>
/// <param name="Operator">The operator the condition applies.</param>
public sealed record RuleConditionOperator(string Pointer, RuleFieldRow Field, RuleOperatorRow Operator);
