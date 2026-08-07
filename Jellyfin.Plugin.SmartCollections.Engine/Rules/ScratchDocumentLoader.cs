using System.IO;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Scratch, not for merge. A file read placed inside the engine project so the invariant lint has
/// the shape it refuses, at the path #68 moved the engine to. The result goes in #68's pull
/// request body and this branch is not merged.
/// </summary>
internal static class ScratchDocumentLoader
{
    /// <summary>
    /// Reads a rule document off disk from inside the engine.
    /// </summary>
    /// <param name="path">The document's path.</param>
    /// <returns>The document's text.</returns>
    public static string Load(string path) => File.ReadAllText(path);
}
