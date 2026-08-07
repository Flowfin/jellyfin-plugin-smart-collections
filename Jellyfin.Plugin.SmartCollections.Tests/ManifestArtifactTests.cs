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
    /// <remarks>
    /// An entry's indentation is not read. YAML admits a sequence at the key's own column and
    /// indented under it, both mean the same list, and which one this file carries is the
    /// formatter's choice rather than the manifest's meaning. A parser that insisted on column
    /// zero would fail the day the file is reformatted and report it as a missing artefact, which
    /// is the wrong failure with the wrong name on it.
    /// </remarks>
    /// <param name="manifest">The manifest file name at the repository root.</param>
    /// <returns>The artefact names the manifest lists, in the order it lists them.</returns>
    private static List<string> ListedArtifacts(string manifest)
    {
        var lines = RepositoryFiles.ReadFromRoot(manifest)
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

            var entry = line.TrimStart();

            if (!entry.StartsWith("- ", StringComparison.Ordinal))
            {
                break;
            }

            listed.Add(entry[2..].Trim().Trim('"'));
        }

        return listed;
    }

    [Fact]
    public void ManifestArtifactsAreExactlyTheAssembliesTheBuildProduces()
    {
        var manifests = RepositoryFiles.ManifestNames();

        Assert.NotEmpty(manifests);

        foreach (var manifest in manifests)
        {
            var listed = ListedArtifacts(manifest);

            Assert.True(listed.Count > 0, manifest + " declares no artifacts: entries this test can read.");

            Assert.Equal(
                new[] { typeof(Plugin).Assembly.GetName().Name + ".dll" },
                listed);
        }
    }
}
