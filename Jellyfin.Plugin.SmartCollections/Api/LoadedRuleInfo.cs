namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// A rule document the store holds and the validator accepted.
/// </summary>
/// <remarks>
/// The file name and the rule's own identity are both here and are different things: the file is
/// what the store reads and writes, and the id is what the collection this rule owns is stamped
/// with. A page showing one of them would leave an operator unable to say which document owns a
/// collection.
///
/// The document's text is NOT here. A listing is read to draw a list, and carrying every document
/// in it would make the cost of drawing that list the size of the rules directory. The read
/// endpoint returns one document's bytes exactly as they sit on disk.
/// </remarks>
/// <param name="File">The document's file name, without its extension.</param>
/// <param name="Id">The identity the document declares.</param>
/// <param name="Name">The name the collection this rule owns carries.</param>
/// <param name="SchemaVersion">The format version the document declares.</param>
public sealed record LoadedRuleInfo(string File, string Id, string Name, int SchemaVersion);
