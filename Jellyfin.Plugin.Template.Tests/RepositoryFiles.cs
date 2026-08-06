using System;
using System.IO;

namespace Jellyfin.Plugin.Template.Tests;

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
}
