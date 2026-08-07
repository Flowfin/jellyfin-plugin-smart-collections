using System;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// Locates files that live at the repository root rather than inside a project, so a test can
/// read the shipping manifest as the catalogue reads it instead of reading a copy.
/// </summary>
internal static class RepositoryFiles
{
    /// <summary>
    /// Gets the directory holding <c>build.yaml</c>, found by walking up from the test assembly.
    /// </summary>
    /// <returns>The repository root.</returns>
    /// <exception cref="DirectoryNotFoundException">No ancestor directory holds build.yaml.</exception>
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "build.yaml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "No ancestor of " + AppContext.BaseDirectory + " holds build.yaml.");
    }

    /// <summary>
    /// Reads a file at the repository root.
    /// </summary>
    /// <param name="name">The file name, relative to the root.</param>
    /// <returns>The file's text.</returns>
    public static string ReadFromRoot(string name)
        => File.ReadAllText(Path.Combine(Root(), name));

    /// <summary>
    /// Gets the shipping manifests, one per supported server line, found by pattern rather than
    /// by a list a test would have to be told about. A manifest added for a third line is
    /// covered by every manifest test the moment the file exists.
    /// </summary>
    /// <returns>The manifest file names at the repository root, sorted.</returns>
    public static string[] ManifestNames()
    {
        var names = Directory.GetFiles(Root(), "build*.yaml")
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();

        Array.Sort(names, StringComparer.Ordinal);

        return names;
    }
}
