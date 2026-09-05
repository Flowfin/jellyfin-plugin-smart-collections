using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// What the validator said about a document, without anything being written.
/// </summary>
/// <param name="Valid">Whether the document was accepted.</param>
/// <param name="Errors">Every reason it was refused, empty where it was accepted.</param>
public sealed record RuleValidationResult(bool Valid, IReadOnlyList<RuleErrorInfo> Errors);
