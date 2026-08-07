using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The suite is meant to run on a machine with no display, under an unprivileged account, and
/// without touching anything machine-wide. <c>docs/testing.md</c> is where that rule and its
/// consequences are written down.
/// </summary>
/// <remarks>
/// An absolute system path in a test source is the cheapest way for that rule to stop being
/// true without anyone noticing. The test still passes for whoever has the path, and it reports
/// nothing at all to everyone else, so the suite quietly measures one machine. The shapes below
/// are the ones that carry that meaning: a drive-rooted path, a path under a system root, and a
/// Windows environment folder.
///
/// A test needing a directory takes one it created itself, which is what
/// <see cref="RuleDocumentStoreTests"/> does, or asks for the repository root, which is what
/// <see cref="RepositoryFiles"/> does by walking up from the assembly.
/// </remarks>
public class SuitePortabilityTests
{
    /// <summary>
    /// The shapes an absolute system path takes, with the name used when one is reported.
    /// </summary>
    /// <remarks>
    /// Each pattern is written so that it does not match its own source text, which is why the
    /// scan below can read this file along with every other one rather than excusing itself.
    /// The drive pattern needs a letter immediately before the colon and this table has a
    /// bracket there; the two others need a literal name between their delimiters and this
    /// table has an opening parenthesis there.
    ///
    /// The share notation Windows uses for a network host is deliberately absent. Written as a
    /// pattern it would be two backslashes, which is also what an ordinary escaped backslash in
    /// a C# string looks like, so it would match its own source and a good deal of innocent
    /// code besides. That shape is not refused here and this sentence is the whole of what is
    /// claimed about it.
    /// </remarks>
    private static readonly (string Shape, Regex Pattern)[] AbsolutePathShapes =
    {
        (
            "a drive-rooted path",
            new Regex(@"\b[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)),
        (
            "a path under a system root",
            new Regex(@"(?<![\w.])/(etc|usr|var|opt|sbin|home|root|proc|sys|tmp|Applications|Library)/", RegexOptions.CultureInvariant)),
        (
            "a Windows environment folder",
            new Regex(@"%(ProgramFiles|ProgramData|SystemRoot|SystemDrive|WINDIR|APPDATA|LOCALAPPDATA)%", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)),
    };

    /// <summary>
    /// Every C# source in the test project, build output excluded. The generated files under
    /// <c>obj</c> hold paths from the machine that built them and are not written by anybody, so
    /// scanning them would report the build rather than the suite.
    /// </summary>
    /// <returns>The full path of each source file, ordered so a failure reads the same twice.</returns>
    private static IEnumerable<string> TestSources()
    {
        var project = Path.Combine(RepositoryFiles.Root(), "Jellyfin.Plugin.SmartCollections.Tests");

        return Directory
            .EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(project, path))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string project, string path)
    {
        var relative = Path.GetRelativePath(project, path);

        return relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoTestInTheSuiteReferencesAnAbsoluteSystemPath()
    {
        var found = new List<string>();

        foreach (var path in TestSources())
        {
            var lines = File.ReadAllLines(path);

            for (var number = 0; number < lines.Length; number++)
            {
                foreach (var (shape, pattern) in AbsolutePathShapes)
                {
                    var match = pattern.Match(lines[number]);

                    if (match.Success)
                    {
                        found.Add(
                            Path.GetFileName(path) + " line " + (number + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + " carries " + shape + ": " + match.Value);
                    }
                }
            }
        }

        Assert.True(
            found.Count == 0,
            "A test may not name an absolute system path, because it then measures one machine "
            + "and reports nothing to anyone else. See docs/testing.md."
            + Environment.NewLine
            + string.Join(Environment.NewLine, found));
    }

    /// <summary>
    /// The scan is worth nothing if it reads no files, and an empty enumeration passes the test
    /// above silently. This is the leg that says the scan reached the suite.
    /// </summary>
    [Fact]
    public void TheScanReadsEveryTestSourceInTheProject()
    {
        var scanned = TestSources().Select(Path.GetFileName).ToList();

        Assert.Contains(nameof(SuitePortabilityTests) + ".cs", scanned);
        Assert.Contains(nameof(RuleDocumentStoreTests) + ".cs", scanned);
        Assert.True(
            scanned.Count >= 10,
            "The scan found " + scanned.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " sources in the test project, which is fewer than the suite has.");
    }
}
