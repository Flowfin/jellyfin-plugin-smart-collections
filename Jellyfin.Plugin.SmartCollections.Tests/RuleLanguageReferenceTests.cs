using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The reference is one page that gathers the pages each part of the rule language is written
/// out on. Each of those pages is held to its own table by its own test class; what nothing held
/// was the gathering. A page could arrive beside the others with no line sending a reader to it,
/// and a line could go on naming a page that had been renamed away, and the suite stayed green
/// for both. These tests hold the reference to the directory the pages sit in, in both
/// directions, and hold the claim the reference makes that every page it names is held by a
/// test.
/// </summary>
/// <remarks>
/// What is read is the bare file name, relative to <c>docs/</c>, which is how every page under
/// that directory refers to its neighbours. The reference writes each name twice, as the text of
/// a link and as its target, and both are read, so a link whose text and target disagree fails
/// in the direction that is wrong rather than passing on the one that is right.
/// </remarks>
public class RuleLanguageReferenceTests
{
    private const string Reference = "docs/rule-language.md";

    private const string GatheringHeading = "## What a rule is made of";

    private const string TestProject = "Jellyfin.Plugin.SmartCollections.Tests";

    private static readonly Regex NamedPage = new(
        @"(?<=`|\()(?<name>rule-[a-z-]+\.md)(?=`|\))",
        RegexOptions.CultureInvariant);

    private static readonly Regex AnyHeading = new(
        @"^## ",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// The section holding the list is found by its heading, so a reference that lost the
    /// section is a message naming the heading rather than a comparison against an empty set
    /// that happens to agree with something.
    /// </summary>
    [Fact]
    public void TheReferenceCarriesTheGatheringSection()
    {
        var document = RepositoryFiles.ReadFromRoot(Reference);

        Assert.True(
            document.Contains(GatheringHeading, StringComparison.Ordinal),
            Reference + " carries no section headed '" + GatheringHeading + "'.");
    }

    /// <summary>
    /// Both directions over the gathering section. The pages are read off the directory rather
    /// than off a list, so a page added under <c>docs/</c> tomorrow is owed a line on the day it
    /// exists, and a line naming a page that is gone reds here rather than sending a reader to
    /// nothing. The reference itself is outside the set, because a page does not gather itself.
    /// </summary>
    [Fact]
    public void TheGatheringSectionNamesEveryRulePageInTheTreeAndNoOther()
    {
        var pages = PagesInTheTree();

        Assert.NotEmpty(pages);

        Assert.Equal(pages, NamesIn(GatheringSection()));
    }

    /// <summary>
    /// Wider than the section above: a page named anywhere in the reference, under a refusal as
    /// much as in the list, has to be a file in the tree. A pointer at a file that is not there
    /// is worse than no pointer.
    /// </summary>
    [Fact]
    public void EveryPageTheReferenceNamesIsInTheTree()
    {
        var named = NamesIn(RepositoryFiles.ReadFromRoot(Reference));

        Assert.NotEmpty(named);

        foreach (var page in named)
        {
            Assert.True(
                File.Exists(Path.Combine(DocsDirectory(), page)),
                Reference + " names " + page + " and no such file is under docs/.");
        }
    }

    /// <summary>
    /// The reference says every page it gathers is held to its table by a test class. That
    /// sentence is read here rather than trusted: a test source in this project has to name the
    /// page by its path, which is how every document test in the suite declares what it holds.
    /// A page gathered with nothing holding it is the drift the reference exists against, one
    /// level up.
    /// </summary>
    [Fact]
    public void EveryPageTheReferenceGathersIsHeldByATest()
    {
        var sources = Directory.GetFiles(Path.Combine(RepositoryFiles.Root(), TestProject), "*.cs")
            .Where(path => !string.Equals(Path.GetFileName(path), "RuleLanguageReferenceTests.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .ToArray();

        Assert.NotEmpty(sources);

        foreach (var page in NamesIn(GatheringSection()))
        {
            var held = "\"docs/" + page + "\"";

            Assert.True(
                sources.Any(source => source.Contains(held, StringComparison.Ordinal)),
                Reference + " gathers " + page + " and no test in " + TestProject + " names " + held + ".");
        }
    }

    private static string DocsDirectory() => Path.Combine(RepositoryFiles.Root(), "docs");

    /// <summary>
    /// The rule pages in the tree, by file name, sorted ordinal so the comparison above is over
    /// two lists in one order rather than over whatever order the file system answered in.
    /// </summary>
    private static string[] PagesInTheTree()
    {
        var pages = Directory.GetFiles(DocsDirectory(), "rule-*.md")
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .Where(name => !string.Equals(name, Path.GetFileName(Reference), StringComparison.Ordinal))
            .ToArray();

        Array.Sort(pages, StringComparer.Ordinal);

        return pages;
    }

    /// <summary>
    /// The distinct page names a text writes, sorted ordinal.
    /// </summary>
    private static string[] NamesIn(string text)
    {
        var names = NamedPage.Matches(text)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Array.Sort(names, StringComparer.Ordinal);

        return names;
    }

    /// <summary>
    /// The text of the gathering section, from its heading to the next heading of any kind.
    /// </summary>
    private static string GatheringSection()
    {
        var document = RepositoryFiles.ReadFromRoot(Reference);
        var start = document.IndexOf(GatheringHeading, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException(
                Reference + " carries no section headed '" + GatheringHeading + "'.");
        }

        start += GatheringHeading.Length;
        var next = AnyHeading.Match(document, start);

        return next.Success ? document[start..next.Index] : document[start..];
    }
}
