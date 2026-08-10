using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// A file the scan refused, and every reason it was refused.
/// </summary>
/// <remarks>
/// A rejection is reported rather than repaired. Nothing rewrites the file, moves it or renames
/// it, so what the operator wrote is still on disk exactly as they wrote it while this record
/// describes why it was not loaded.
///
/// Every error the validator produced is carried rather than the first one. An operator fixing a
/// document one message at a time is an operator making one edit per round trip through the
/// server.
/// </remarks>
/// <param name="Name">The name the file was read under, without its extension.</param>
/// <param name="Errors">Every reason it was refused, in the order they were found.</param>
public sealed record RejectedRuleDocument(string Name, IReadOnlyList<RuleValidationError> Errors);
