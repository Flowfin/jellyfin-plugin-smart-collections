using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// Every path this repository composes is composed with <see cref="Path.Join(string, string)"/>,
/// and the scan below refuses the combining call it replaced.
/// </summary>
/// <remarks>
/// The call this refuses drops everything before an argument that is rooted, so
/// <c>combine(store, name)</c> returns <c>name</c> alone the moment <c>name</c> is an absolute
/// path, and the caller writes outside the directory it thought it was writing in. Nothing about
/// the call site says so: it reads as a join and behaves as one until the day an argument is
/// rooted. <see cref="Path.Join(string, string)"/> concatenates its arguments and roots nothing,
/// so the same site cannot escape.
///
/// Twenty-six sites in the test project were reported for it, and every later argument at those
/// sites is a literal in the source or a string read out of a tracked file in this repository, so
/// no escape was demonstrated at any of them. That is the reason this is a scan rather than a
/// repair of the reachable ones: what makes the hazard expensive is that a site becomes reachable
/// when somebody changes what an argument holds, a long way from the line that composes the path.
///
/// The pattern is written so that it does not match its own source text, which is why the scan
/// reads this file along with every other one rather than excusing itself: it needs a literal dot
/// between the two names, and the pattern below has an escape there. No prose in this file writes
/// the refused name either, for the same reason. That is the convention
/// <see cref="SuitePortabilityTests"/> already follows, and the failure it avoids is a scan that
/// reports itself and is then taught to skip a file.
/// </remarks>
public class PathCompositionTests
{
    /// <summary>
    /// The call that may silently drop its earlier arguments.
    /// </summary>
    private static readonly Regex Refused = new Regex(
        @"\bPath\s*\.\s*Combine\s*\(",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The projects the scan reads. Build output is excluded where each one is walked, because the
    /// generated files under <c>obj</c> and <c>bin</c> are not written by anybody and scanning them
    /// would report the build rather than the tree.
    /// </summary>
    private static readonly string[] Projects =
    {
        "Jellyfin.Plugin.SmartCollections",
        "Jellyfin.Plugin.SmartCollections.Engine",
        "Jellyfin.Plugin.SmartCollections.Tests",
    };

    [Fact]
    public void NothingInTheTreeComposesAPathWithTheCallThatCanDropItsEarlierArguments()
    {
        var found = new List<string>();

        foreach (var path in Sources())
        {
            var lines = File.ReadAllLines(path);

            for (var number = 0; number < lines.Length; number++)
            {
                var match = Refused.Match(lines[number]);

                if (match.Success)
                {
                    found.Add(
                        Path.GetFileName(path)
                        + " line "
                        + (number + 1).ToString(CultureInfo.InvariantCulture)
                        + ": "
                        + match.Value);
                }
            }
        }

        Assert.True(
            found.Count == 0,
            "A path is composed with Path.Join, which roots nothing, rather than with the call "
            + "that returns its last argument alone when that argument is rooted. See "
            + "docs/testing.md."
            + Environment.NewLine
            + string.Join(Environment.NewLine, found));
    }

    /// <summary>
    /// A scan over no files passes the test above without reading anything, so this is the leg that
    /// says it reached all three projects and this file among them.
    /// </summary>
    [Fact]
    public void TheScanReadsEverySourceInEveryProject()
    {
        var counts = Projects.Select(project => SourcesUnder(project).Count()).ToArray();

        Assert.All(
            counts,
            count => Assert.True(
                count > 0,
                "A project contributed no source to the scan: "
                + string.Join(", ", counts.Select(c => c.ToString(CultureInfo.InvariantCulture)))));

        var scanned = Sources().Select(Path.GetFileName).ToList();

        Assert.Contains(nameof(PathCompositionTests) + ".cs", scanned);
        Assert.Contains("RuleDocumentStore.cs", scanned);
    }

    /// <summary>
    /// The scan is worth nothing if the pattern does not match the shape it names, and a pattern
    /// that matched nothing would pass the first leg on every tree. This is the leg that says the
    /// pattern still bites, and it holds the shape as data rather than as source so that the file
    /// the scan reads carries no site for it to find.
    /// </summary>
    [Fact]
    public void ThePatternMatchesTheShapeItRefuses()
    {
        var name = "Path" + "." + "Combine";

        Assert.Matches(Refused, "        return " + name + "(_directory, name);");
        Assert.Matches(Refused, "        return " + name + " (a, b);");
        Assert.DoesNotMatch(Refused, "        return Path.Join(_directory, name);");
        Assert.DoesNotMatch(Refused, "        return Path.GetFileName(name);");
    }

    private static IEnumerable<string> Sources() => Projects.SelectMany(SourcesUnder);

    private static IEnumerable<string> SourcesUnder(string project)
    {
        var directory = Path.Join(RepositoryFiles.Root(), project);

        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(directory, path))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string project, string path)
        => Path.GetRelativePath(project, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
}
