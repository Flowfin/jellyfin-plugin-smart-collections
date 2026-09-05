using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// A file in the rules directory the validator refused.
/// </summary>
/// <remarks>
/// It carries the file name and nothing the document declares, because a refused document has
/// declared nothing this plugin will act on: an id read out of a document that did not validate is
/// a string from a file whose shape was rejected.
/// </remarks>
/// <param name="File">The file's name, without its extension.</param>
/// <param name="Errors">Every reason it was refused, in the order the read produced them.</param>
public sealed record RejectedRuleInfo(string File, IReadOnlyList<RuleErrorInfo> Errors);
