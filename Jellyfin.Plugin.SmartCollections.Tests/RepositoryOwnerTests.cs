using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// Every reference to this repository names an account, and the tree has to name one account
/// rather than several. A reference left on a previous location keeps working only while the
/// forwarding GitHub set up when the repository moved is still there, and GitHub deletes that
/// forwarding permanently the moment a repository or a fork is created at the old path.
/// </summary>
/// <remarks>
/// The failure this refuses is a repair that reached most of the sites and stopped: ten
/// references stood on a previous account beside twelve on the current one, two of them the
/// route somebody uses to report a vulnerability, and no route here read the repository's own
/// name (#171).
///
/// The account is derived rather than declared. Nothing below says which account is right, so
/// a later move is a repair this leg demands rather than one it passes over, and no constant
/// here goes stale on the day of the move.
/// </remarks>
public class RepositoryOwnerTests
{
    /// <summary>
    /// A reference to this repository, with the account it names captured.
    /// </summary>
    /// <remarks>
    /// The pattern does not match its own source. An account run has to sit immediately before
    /// the slash and this literal has a closing parenthesis there, so this file is read along
    /// with every other one rather than excusing itself.
    /// </remarks>
    private static readonly Regex Reference = new(
        @"(?<owner>[A-Za-z0-9._-]+)/jellyfin-plugin-smart-collections",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The shapes a local filesystem path takes. A checkout directory carries the repository's
    /// own name, so the path to it matches the pattern above with the parent directory standing
    /// where an account would be. That is a path on somebody's machine and not a reference to
    /// anything on GitHub, so it is skipped rather than counted.
    /// </summary>
    private static readonly Regex[] LocalPathShapes =
    {
        new(@"\b[A-Za-z]:[\\/]", RegexOptions.CultureInvariant),
        new(@"(?<![\w.])/(home|Users|mnt|media|var|tmp|opt|srv|root)/", RegexOptions.CultureInvariant),
    };

    /// <summary>
    /// Directory names holding build output, a tool's own state, or a checkout of something
    /// else. Nobody writes what is under them, so a reference found there reports a machine
    /// rather than this tree.
    /// </summary>
    private static readonly string[] NotAuthored =
    {
        ".git", ".vs", "bin", "obj", "node_modules", "TestResults", "packages",
    };

    /// <summary>
    /// Refuses a tree that refers to this repository under more than one account.
    /// </summary>
    [Fact]
    public void TheTreeNamesOneAccountForThisRepository()
    {
        var byOwner = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (path, line, number) in AuthoredLines())
        {
            foreach (Match match in Reference.Matches(line))
            {
                if (IsInsideALocalPath(line, match))
                {
                    continue;
                }

                var owner = match.Groups["owner"].Value;

                if (!byOwner.TryGetValue(owner, out var sites))
                {
                    sites = new List<string>();
                    byOwner[owner] = sites;
                }

                sites.Add(path + " line " + number.ToString(CultureInfo.InvariantCulture));
            }
        }

        Assert.True(
            byOwner.Count <= 1,
            "This repository is referred to under more than one account. A reference left on a "
            + "previous account resolves only while GitHub's forwarding survives, and creating a "
            + "repository or a fork at the old path deletes that forwarding permanently."
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                byOwner.Select(entry => entry.Key + ": " + string.Join(", ", entry.Value))));
    }

    /// <summary>
    /// The leg above passes on an empty enumeration, which is what a scan that reached nothing
    /// looks like from the outside. This is the leg that says it reached the tree.
    /// </summary>
    [Fact]
    public void TheScanReadsTheFilesThatCarryAReference()
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, line, _) in AuthoredLines())
        {
            foreach (Match match in Reference.Matches(line))
            {
                if (!IsInsideALocalPath(line, match))
                {
                    files.Add(path);
                }
            }
        }

        Assert.Contains("SECURITY.md", files);
        Assert.Contains("README.md", files);
        Assert.True(
            files.Count >= 10,
            "The scan found a reference in " + files.Count.ToString(CultureInfo.InvariantCulture)
            + " files, which is fewer than this tree carries.");
    }

    /// <summary>
    /// Every line of every authored text file, with the path relative to the repository root so
    /// a failure reads the same on any machine.
    /// </summary>
    /// <returns>The relative path, the line, and the one-based line number.</returns>
    private static IEnumerable<(string Path, string Line, int Number)> AuthoredLines()
    {
        var root = RepositoryFiles.Root();

        var paths = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsUnderAnUnauthoredDirectory(root, path))
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            string[] lines;

            try
            {
                if (LooksBinary(path))
                {
                    continue;
                }

                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            for (var number = 0; number < lines.Length; number++)
            {
                yield return (relative, lines[number], number + 1);
            }
        }
    }

    private static bool IsUnderAnUnauthoredDirectory(string root, string path)
        => Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .SkipLast(1)
            .Any(segment => NotAuthored.Contains(segment, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Whether a file holds bytes rather than text, decided by the one byte text does not carry.
    /// </summary>
    /// <param name="path">The file to look at.</param>
    /// <returns><see langword="true"/> when a zero byte appears near the start.</returns>
    private static bool LooksBinary(string path)
    {
        using var stream = File.OpenRead(path);

        var window = new byte[4096];
        var read = stream.Read(window, 0, window.Length);

        return Array.IndexOf(window, (byte)0, 0, read) >= 0;
    }

    /// <summary>
    /// Whether a match sits inside a path on the machine running this rather than inside a
    /// reference to GitHub. The whitespace-delimited run around the match is what is judged,
    /// because that run is the whole of the path or the whole of the URL.
    /// </summary>
    /// <param name="line">The line the match was found on.</param>
    /// <param name="match">The match.</param>
    /// <returns><see langword="true"/> when the surrounding run is a local path.</returns>
    private static bool IsInsideALocalPath(string line, Match match)
    {
        var start = match.Index;
        var end = match.Index + match.Length;

        while (start > 0 && !char.IsWhiteSpace(line[start - 1]))
        {
            start--;
        }

        while (end < line.Length && !char.IsWhiteSpace(line[end]))
        {
            end++;
        }

        var token = line[start..end];

        return token.Contains('\\', StringComparison.Ordinal)
            || LocalPathShapes.Any(shape => shape.IsMatch(token));
    }
}
