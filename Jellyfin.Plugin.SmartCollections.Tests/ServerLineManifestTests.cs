using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// This plugin ships one package per server line and the two lines host different runtimes. The
/// only thing that stops a package reaching the wrong server is the pair of scalars in its
/// manifest, and nothing in a build reads them: a manifest naming a framework the project does
/// not build produces a package the server refuses to load, and the build stays green.
/// </summary>
public class ServerLineManifestTests
{
    /// <summary>
    /// The project the packages are built from. The manifests are checked against what this
    /// file declares rather than against a list repeated here, so adding a line is one edit.
    /// </summary>
    private const string PluginProject =
        "Jellyfin.Plugin.SmartCollections/Jellyfin.Plugin.SmartCollections.csproj";

    /// <summary>
    /// The scalars that are allowed to differ between manifests. Everything else describes one
    /// plugin and has to agree, or a server crossing the line boundary sees a second entry in
    /// the catalogue rather than an update to the one it has.
    /// </summary>
    private static readonly HashSet<string> PerLineScalars =
        new(StringComparer.Ordinal) { "targetAbi", "framework" };

    private static readonly Regex Scalar = new(
        "^(?<key>[A-Za-z][A-Za-z0-9_]*):[ \t]*\"?(?<value>[^\"\r\n]*?)\"?[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex TopLevelKey = new(
        "^(?<key>[A-Za-z][A-Za-z0-9_]*):(?<rest>.*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex ProjectTargetFrameworks = new(
        "<TargetFrameworks>(?<frameworks>[^<]+)</TargetFrameworks>",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads the top-level scalars out of a flat manifest. Block values and lists are skipped,
    /// which is what keeps the test project free of a YAML dependency; the fields this file
    /// reasons about are all scalars.
    /// </summary>
    /// <param name="manifest">The manifest file name at the repository root.</param>
    /// <returns>The scalar keys and their values.</returns>
    private static Dictionary<string, string> Scalars(string manifest)
    {
        var read = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in Scalar.Matches(RepositoryFiles.ReadFromRoot(manifest)))
        {
            var value = match.Groups["value"].Value;

            // A key introducing a block or a list carries no value on its own line.
            if (value.Length == 0 || value == ">" || value == "|")
            {
                continue;
            }

            read[match.Groups["key"].Value] = value;
        }

        return read;
    }

    /// <summary>
    /// Reads the frameworks the plugin project builds.
    /// </summary>
    /// <returns>The target frameworks, in the order the project file lists them.</returns>
    private static string[] TargetFrameworks()
    {
        var project = File.ReadAllText(
            Path.Combine(RepositoryFiles.Root(), PluginProject.Replace('/', Path.DirectorySeparatorChar)));
        var match = ProjectTargetFrameworks.Match(project);

        Assert.True(match.Success, PluginProject + " declares no TargetFrameworks.");

        return match.Groups["frameworks"].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [Fact]
    public void EveryFrameworkTheProjectBuildsHasExactlyOneManifest()
    {
        var declared = RepositoryFiles.ManifestNames()
            .Select(manifest => Scalars(manifest)["framework"])
            .OrderBy(framework => framework, StringComparer.Ordinal);

        Assert.Equal(
            TargetFrameworks().OrderBy(framework => framework, StringComparer.Ordinal),
            declared);
    }

    /// <summary>
    /// Reads every top-level entry of a manifest as its key and everything written under it, with
    /// comments and the document marker removed.
    /// </summary>
    /// <remarks>
    /// The scalar reader above skips a block value and a list on purpose, and that is the right
    /// answer for the two questions it is asked. It is the wrong answer for asking whether two
    /// manifests agree: the catalogue text an operator reads is <c>description</c>, which is a
    /// block, and what a package actually ships is <c>artifacts</c>, which is a list. Both were
    /// outside the comparison, so a manifest could name one assembly while its neighbour named
    /// two and every test here stayed green.
    ///
    /// Still no YAML dependency. Nothing here interprets a block or a list; a continuation line is
    /// carried through as written, which compares two manifests more strictly than parsing them
    /// would, since a difference in indentation is a difference in the text as well.
    ///
    /// A comment is dropped rather than compared, because the two manifests explain their own
    /// per-line fields in their own words and are meant to.
    /// </remarks>
    /// <param name="manifest">The manifest file name at the repository root.</param>
    /// <returns>The top-level keys and the text under each.</returns>
    private static Dictionary<string, string> Entries(string manifest)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var value = new StringBuilder();
        string? key = null;

        var lines = RepositoryFiles.ReadFromRoot(manifest)
            .Split('\n')
            .Select(raw => raw.TrimEnd('\r'));

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith('#') || trimmed == "---")
            {
                continue;
            }

            var match = TopLevelKey.Match(line);

            if (!match.Success)
            {
                Assert.True(
                    key is not null,
                    manifest + " opens with a line that is neither a comment nor a top-level key: " + line);

                value.Append(line).Append('\n');
                continue;
            }

            if (key is not null)
            {
                entries[key] = value.ToString().Trim();
            }

            key = match.Groups["key"].Value;
            value.Clear();
            value.Append(match.Groups["rest"].Value.Trim()).Append('\n');
        }

        if (key is not null)
        {
            entries[key] = value.ToString().Trim();
        }

        return entries;
    }

    [Fact]
    public void TheManifestsDifferOnlyWhereTheServerLineDiffers()
    {
        var manifests = RepositoryFiles.ManifestNames();

        Assert.True(manifests.Length > 1, "There is only one manifest, so there is nothing to compare.");

        var first = Entries(manifests[0]);

        foreach (var manifest in manifests.Skip(1))
        {
            var other = Entries(manifest);

            Assert.Equal(first.Keys.OrderBy(key => key, StringComparer.Ordinal), other.Keys.OrderBy(key => key, StringComparer.Ordinal));

            foreach (var (key, value) in first)
            {
                if (PerLineScalars.Contains(key))
                {
                    Assert.NotEqual(value, other[key]);
                    continue;
                }

                Assert.True(
                    value == other[key],
                    manifests[0] + " and " + manifest + " disagree on " + key + ": " + value + " against " + other[key] + ".");
            }
        }
    }

    [Fact]
    public void EveryManifestClaimsTheAbiFloorOfTheLineItsFrameworkBelongsTo()
    {
        // Which server generation hosts which runtime. Read from the servers' own project files
        // and recorded in README.md; the pairing is what makes a package loadable at all.
        var floorForFramework = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["net9.0"] = "10.11",
            ["net10.0"] = "12.0",
        };

        foreach (var manifest in RepositoryFiles.ManifestNames())
        {
            var scalars = Scalars(manifest);
            var framework = scalars["framework"];

            Assert.True(
                floorForFramework.TryGetValue(framework, out var line),
                manifest + " targets " + framework + ", and no server line is recorded as hosting it.");

            Assert.StartsWith(line + ".", scalars["targetAbi"], StringComparison.Ordinal);
        }
    }
}
