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

    /// <summary>
    /// Gets the assemblies a package has to carry, taken from a type in each rather than from a
    /// list of names. The solution holds three projects and the test project is not shipped, so
    /// the two named here are the two the package contains.
    /// </summary>
    /// <remarks>
    /// Derived from types rather than written down, because a name written down is a second
    /// declaration of what the build produces and the two drift. Renaming either assembly moves
    /// this list on the next build, which is the whole point of reading it off a type.
    /// </remarks>
    /// <returns>The shipped assembly file names, in ordinal order.</returns>
    private static string[] ShippedAssemblies()
    {
        var names = new[]
        {
            typeof(Plugin).Assembly.GetName().Name + ".dll",
            typeof(Rules.RuleDocument).Assembly.GetName().Name + ".dll"
        };

        Array.Sort(names, StringComparer.Ordinal);

        return names;
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

            // Sorted on both sides. Which order a manifest lists its artefacts in is not something
            // the package cares about, and a test that insisted on one would fail for a reordering
            // that changed nothing while saying an assembly was missing.
            var declared = listed.ToArray();
            Array.Sort(declared, StringComparer.Ordinal);

            Assert.Equal(ShippedAssemblies(), declared);
        }
    }

    [Fact]
    public void TheEngineIsItsOwnAssembly()
    {
        // The boundary #68 exists for. Four issues reason about "the engine assembly", and the
        // phrase resolves to nothing while the engine and the host compile into one. This asserts
        // the split rather than the manifest, and it fails the day someone folds the projects back
        // together, which would otherwise show up only as those four issues quietly meaning less.
        Assert.NotEqual(
            typeof(Plugin).Assembly.GetName().Name,
            typeof(Rules.RuleDocument).Assembly.GetName().Name);
    }
}
