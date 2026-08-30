using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The composition rules are only usable if somebody can read them. These tests hold
/// <c>docs/rule-composition.md</c> to the reader: a group with no section, a section for a group
/// the reader does not accept, and a nesting limit the page has stopped stating all red the
/// suite.
/// </summary>
public class RuleCompositionDocumentTests
{
    private const string Page = "docs/rule-composition.md";

    private static readonly Regex Group = new(
        @"^## Group: (?<name>[A-Za-z]+)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static string[] Documented()
        => Group.Matches(RepositoryFiles.ReadFromRoot(Page))
            .Select(match => match.Groups["name"].Value)
            .ToArray();

    /// <summary>
    /// Without this the comparison below passes on a page somebody emptied, because two empty
    /// sets agree.
    /// </summary>
    [Fact]
    public void ThePageCarriesASectionPerGroup()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryFiles.Root(), Page)), Page + " is not in the tree.");
        Assert.NotEmpty(Documented());
    }

    [Fact]
    public void EveryGroupHasASectionAndEverySectionNamesAGroup()
    {
        Assert.Equal(
            RuleCompositionReader.GroupNames.OrderBy(name => name, StringComparer.Ordinal),
            Documented().OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// The issue that asked for the bound asked for the number to be in the documentation rather
    /// than only in the parser, so the page carries it and this reads that the two agree. Without
    /// it the page can go on stating a bound the constant left behind, which is worse than no
    /// page: somebody writes to the number they read and meets a refusal.
    /// </summary>
    [Fact]
    public void ThePageStatesTheNestingLimitTheReaderHolds()
    {
        var page = RepositoryFiles.ReadFromRoot(Page);
        var limit = RuleCompositionReader.MaximumNestingDepth.ToString(CultureInfo.InvariantCulture);

        Assert.Contains("Nesting is bounded, and the bound is " + limit, page, StringComparison.Ordinal);
        Assert.Contains("RuleCompositionReader.MaximumNestingDepth", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page's own claim about what it does not read. A section explaining that a condition is
    /// somebody else's business is what stops the next reader wiring one in here.
    /// </summary>
    [Fact]
    public void ThePageSaysWhatThisStageDoesNotRead()
    {
        Assert.Contains("## What this stage does not read", RepositoryFiles.ReadFromRoot(Page), StringComparison.Ordinal);
    }
}
