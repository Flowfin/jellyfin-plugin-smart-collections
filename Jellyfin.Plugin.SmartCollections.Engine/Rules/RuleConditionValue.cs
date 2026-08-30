using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One condition, with the field row its <c>field</c> member names, the operator row its
/// <c>operator</c> member names, and the value or values its <c>value</c> member writes, parsed.
/// </summary>
/// <remarks>
/// The rows are carried through rather than looked up again, for the reason
/// <see cref="RuleConditionOperator"/> gives about its own: the stage before this one has already
/// resolved them, and resolving them twice would give one condition two chances to be resolved
/// differently.
///
/// <see cref="Values"/> IS A LIST ON EVERY CONDITION AND ITS LENGTH IS THE ONE THING THAT VARIES.
/// An operator taking no value has none, <c>in</c> and <c>notIn</c> have one per member of the
/// list the document wrote, and the other thirteen have exactly one. A shape carrying an optional
/// single value beside an optional list would let a reader downstream take the wrong one of the
/// two, and every reader of this type wants the same thing from it: the values to compare the
/// field against, in the order the document wrote them.
///
/// The order is the document's. Two rules that name the same values in two orders are two
/// documents rather than one, and a stage that sorted them here would make the compiled form of a
/// rule depend on something the operator did not write.
/// </remarks>
/// <param name="Pointer">Where the condition is in the document, as a JSON Pointer.</param>
/// <param name="Field">The field the condition names.</param>
/// <param name="Operator">The operator the condition applies.</param>
/// <param name="Values">
/// The parsed values, in the order the document wrote them. Empty where the operator takes none.
/// </param>
public sealed record RuleConditionValue(
    string Pointer,
    RuleFieldRow Field,
    RuleOperatorRow Operator,
    IReadOnlyList<RuleValue> Values);
