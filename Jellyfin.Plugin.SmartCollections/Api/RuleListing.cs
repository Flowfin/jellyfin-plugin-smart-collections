using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// What the rules directory holds, in both of its states.
/// </summary>
/// <remarks>
/// Two lists rather than one list with a flag, because the two carry different things: a loaded
/// document has an identity and a name, and a refused file has neither and has reasons instead. A
/// single shape would give every entry both halves and leave half of each one empty.
/// </remarks>
/// <param name="Loaded">The documents that passed, in ordinal order by file name.</param>
/// <param name="Rejected">The files that were refused, in ordinal order by file name.</param>
public sealed record RuleListing(
    IReadOnlyList<LoadedRuleInfo> Loaded,
    IReadOnlyList<RejectedRuleInfo> Rejected);
