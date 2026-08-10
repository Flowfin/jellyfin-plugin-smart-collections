namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// A rule document that passed validation, and the name it was read under.
/// </summary>
/// <remarks>
/// The name is what an operator sees on the administrator surface and what a later identity will
/// be checked against, so it travels with the document rather than being recovered from a path by
/// whoever needs it next.
/// </remarks>
/// <param name="Name">The document's name, without its extension.</param>
/// <param name="Document">The document.</param>
public sealed record LoadedRuleDocument(string Name, RuleDocument Document);
