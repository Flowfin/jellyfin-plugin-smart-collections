using System;
using System.Collections.Generic;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The catalogue installs whatever <c>build.yaml</c> lists under <c>artifacts</c> and the build
/// produces whatever the project file is called. Nothing connects the two, so renaming the
/// assembly leaves a manifest naming a file the package does not contain, and that failure shows
/// up on somebody's server rather than in a build.
/// </summary>
public class ManifestArtifactTests
{
    /// <summary>
    /// Reads the <c>artifacts</c> block of the flat manifest: the key at column zero, then one
    /// <c>- name</c> entry per line until a line that is not an entry. Reading it this way keeps
    /// the test project free of a YAML dependency carried for one list.
    /// </summary>
    /// <returns>The artefact names the manifest lists, in the order it lists them.</returns>
    private static List<string> ListedArtifacts()
    {
        var lines = RepositoryFiles.ReadFromRoot("build.yaml")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');

        var listed = new List<string>();
        var inBlock = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("artifacts:", StringComparison.Ordinal))
            {
                inBlock = true;
                continue;
            }

            if (!inBlock)
            {
                continue;
            }

            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            listed.Add(line[2..].Trim().Trim('"'));
        }

        return listed;
    }

    [Fact]
    public void ManifestArtifactsAreExactlyTheAssembliesTheBuildProduces()
    {
        var listed = ListedArtifacts();

        Assert.True(listed.Count > 0, "build.yaml declares no artifacts: entries this test can read.");

        Assert.Equal(
            new[] { typeof(Plugin).Assembly.GetName().Name + ".dll" },
            listed);
    }
}
