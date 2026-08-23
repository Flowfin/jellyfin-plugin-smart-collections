using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.SmartCollections.Membership;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// <c>docs/testing.md</c> refuses four kinds of test and names what replaces each one. A
/// replacement that is not in the tree turns the refusal into a hole, and nothing about the
/// document is read by the build, so it can go a whole change out of date in silence.
/// </summary>
/// <remarks>
/// Three of that document's replacements pointed at issue numbers instead of at the tree, and
/// the three issues had closed, so a reader could not tell from the page whether the thing had
/// arrived or been dropped. The page names the artefacts now, and the tests below are what stops
/// a name there from outliving the thing it names.
///
/// Only what a reading of this tree can settle is held here. Whether the prose is right about
/// why a test is refused is a judgement, and no test here makes it.
/// </remarks>
public class TestingDocumentTests
{
    /// <summary>
    /// The type a library query would be composed against. The document states that neither
    /// assembly composes one, which is what makes the missing fake a gap rather than an
    /// oversight, so the day a query appears that sentence has to be rewritten.
    /// </summary>
    private const string QueryType = "Internal" + "ItemsQuery";

    private static readonly string[] ProductProjects =
    {
        "Jellyfin.Plugin.SmartCollections",
        "Jellyfin.Plugin.SmartCollections.Engine",
    };

    private static string Document() => Path.Combine(RepositoryFiles.Root(), "docs", "testing.md");

    private static string DocumentText() => File.ReadAllText(Document());

    /// <summary>
    /// The page tells a reader that one method decides which directory the running plugin hands
    /// the store, and that this is why a test can point the store somewhere it owns. If that
    /// method is renamed or removed, the sentence sends them looking for something that is not
    /// there.
    /// </summary>
    [Fact]
    public void TheOnePlaceTheRulesDirectoryIsDecidedIsNamedAndIsInTheTree()
    {
        Assert.Contains(
            "`PluginServiceRegistrator.RulesDirectory`",
            DocumentText(),
            StringComparison.Ordinal);

        var method = typeof(PluginServiceRegistrator).GetMethod(
            "RulesDirectory",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(typeof(string), method!.ReturnType);
        Assert.Single(method.GetParameters());
    }

    /// <summary>
    /// The refusal of a booted server is replaced by narrow ports with fakes, and the page now
    /// says which of the two is built. The write port and a stand-in for it are what that half
    /// rests on, so both are asserted here rather than left to the reader.
    /// </summary>
    [Fact]
    public void TheWriteSurfaceThePageNamesIsAPortWithAStandIn()
    {
        var document = DocumentText();

        Assert.Contains("`ICollectionMembershipWriter`", document, StringComparison.Ordinal);
        Assert.Contains("`FakeCollectionWriter`", document, StringComparison.Ordinal);

        Assert.True(typeof(ICollectionMembershipWriter).IsInterface);

        var standIns = typeof(TestingDocumentTests).Assembly
            .GetTypes()
            .Where(type => typeof(ICollectionMembershipWriter).IsAssignableFrom(type) && !type.IsInterface)
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("FakeCollectionWriter", standIns);
    }

    /// <summary>
    /// The other half of that replacement is a negative statement: no query surface, so no fake
    /// of one. It is the half a reader is most likely to take on trust, and it is the half that
    /// stops being true without anybody editing this document.
    /// </summary>
    [Fact]
    public void TheQuerySurfaceThePageReportsAbsentIsAbsent()
    {
        Assert.Contains(
            "The library query surface is not built and has no fake.",
            DocumentText(),
            StringComparison.Ordinal);

        var composing = ProductSources()
            .Where(path => File.ReadAllText(path).Contains(QueryType, StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            composing.Length == 0,
            "docs/testing.md states that neither assembly composes a library query, and these "
            + "files do. Rewrite that paragraph rather than this test."
            + Environment.NewLine
            + string.Join(Environment.NewLine, composing));
    }

    /// <summary>
    /// A scan over no files passes the test above without reading anything, so this is the leg
    /// that says it reached both product projects. A project directory that is not there yields
    /// nothing rather than throwing, which is what makes that silence the failure this leg
    /// reports rather than a stack trace from the other one.
    /// </summary>
    [Fact]
    public void TheScanReadsBothProductProjects()
    {
        var counts = ProductProjects
            .Select(project => SourcesUnder(project).Count())
            .ToArray();

        Assert.All(
            counts,
            count => Assert.True(
                count > 0,
                "A product project contributed no source to the scan: "
                + string.Join(", ", counts.Select(c => c.ToString(CultureInfo.InvariantCulture)))));
    }

    private static IEnumerable<string> ProductSources()
        => ProductProjects.SelectMany(SourcesUnder);

    private static IEnumerable<string> SourcesUnder(string project)
    {
        var directory = Path.Combine(RepositoryFiles.Root(), project);

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
