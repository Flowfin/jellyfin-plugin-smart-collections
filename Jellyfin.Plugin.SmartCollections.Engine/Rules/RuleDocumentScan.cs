using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What one pass over the rules directory found: the documents that loaded, and the files that
/// were refused.
/// </summary>
/// <remarks>
/// Two lists rather than one list of results, because the two are read by different callers for
/// different reasons. Evaluation walks the loaded documents and never has to ask whether an entry
/// is one; the administrator surface shows the rejections so a collection that stopped updating
/// has a reason next to it rather than being a file nobody knows is broken.
///
/// A rejection removes one document from the scan and nothing else. That is the property this
/// whole shape exists for: a directory of rules is not a single document, so a missing brace in
/// one file costs that file's collection and leaves every other collection refreshing.
///
/// Both lists are in ordinal order by name, which is the order the store lists the directory in.
/// The same directory therefore produces the same scan on every run and on either server line,
/// rather than whatever order the file system happened to return.
/// </remarks>
/// <param name="Loaded">The documents that passed, in ordinal order by name.</param>
/// <param name="Rejected">The files that were refused, in ordinal order by name.</param>
public sealed record RuleDocumentScan(
    IReadOnlyList<LoadedRuleDocument> Loaded,
    IReadOnlyList<RejectedRuleDocument> Rejected);
