namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// One item kind a rule may collect, as the vocabulary endpoint hands it to the page.
/// </summary>
/// <remarks>
/// The server's own enumeration member is not here. What a rule document writes is this name, and
/// which member of the server's enumeration it selects is the engine's business and moves with the
/// server line rather than with the format.
/// </remarks>
/// <param name="Name">The name a rule document writes.</param>
/// <param name="Semantics">What the kind is, in one sentence.</param>
public sealed record VocabularyItemKind(string Name, string Semantics);
