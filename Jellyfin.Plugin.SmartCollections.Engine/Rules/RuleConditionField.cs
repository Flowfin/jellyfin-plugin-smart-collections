namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One condition, and the field row its <c>field</c> member names.
/// </summary>
/// <remarks>
/// The row rather than the name it was written with. A stage after this one asks what type the
/// value beside the condition holds and which operators mean anything for it, and both of those
/// are columns of the row; carrying the string instead would make every later stage look the
/// field up again and give each of them a chance to look it up differently.
/// </remarks>
/// <param name="Pointer">Where the condition is in the document, as a JSON Pointer.</param>
/// <param name="Row">The field the condition names.</param>
public sealed record RuleConditionField(string Pointer, RuleFieldRow Row);
