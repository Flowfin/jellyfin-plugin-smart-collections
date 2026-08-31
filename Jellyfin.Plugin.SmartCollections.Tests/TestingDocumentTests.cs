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
    /// The type a library query is composed against. The document names the one question this
    /// tree composes one for, so a query appearing anywhere else is a sentence on that page that
    /// has stopped being true.
    /// </summary>
    private const string QueryType = "Internal" + "ItemsQuery";

    /// <summary>
    /// The files the page accounts for, which are the mark's own query, the port it is passed
    /// through and the adapter that hands it to the server. Written as file names rather than as
    /// a directory, because what the page claims is that ONE question composes a query, and a
    /// directory would let a second one in beside it.
    /// </summary>
    private static readonly string[] ComposeAQuery =
    {
        "CollectionStamp.cs",
        "ICollectionOwnership.cs",
        "RuleQueryCompilation.cs",
        "RuleQueryCompiler.cs",
        "RuleQueryRow.cs",
        "RuleQueryTable.cs",
    };

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
    /// The other half of that replacement is the half a reader is most likely to take on trust,
    /// and the half that stops being true without anybody editing this document. The page has said
    /// in turn that no query was composed at all, that one question composed one, and now that a
    /// rule's own query is composed beside it; what is held here is that no further question
    /// quietly joined those two.
    /// </summary>
    /// <remarks>
    /// The page's claim about the surface an evaluation reads is still a negative one, and the
    /// scan below does not prove it: a file composing an evaluation's query would have to be one
    /// of the three named above to pass, which is a name rather than a purpose. What the scan
    /// refuses is the cheap version of that failure, a query composed in a fourth file with the
    /// page left where it was.
    /// </remarks>
    [Fact]
    public void TheOnlyQuerySurfaceIsTheOneThePageNames()
    {
        var document = DocumentText();

        Assert.Contains("`CollectionStamp.LookupQuery`", document, StringComparison.Ordinal);
        Assert.Contains("`ICollectionOwnership`", document, StringComparison.Ordinal);
        Assert.Contains("`FakeCollectionOwnership`", document, StringComparison.Ordinal);
        Assert.Contains("`RuleQueryCompiler`", document, StringComparison.Ordinal);
        Assert.Contains(
            "WHAT IS STILL ABSENT IS THE PORT AND ITS FAKE.",
            document,
            StringComparison.Ordinal);

        var composing = ProductSources()
            .Where(path => File.ReadAllText(path).Contains(QueryType, StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            composing.SequenceEqual(ComposeAQuery.OrderBy(name => name, StringComparer.Ordinal)),
            "docs/testing.md accounts for the files that compose a library query, and these are "
            + "the files that do. Rewrite that paragraph rather than this test."
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
