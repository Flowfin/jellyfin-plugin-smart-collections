using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The order and the cap are only usable if somebody can read them. These tests hold
/// <c>docs/rule-sort.md</c> to the sort table in both directions: a direction with no section, a
/// section for a direction the reader does not accept, and a field the table refuses to order by
/// that the page does not name all red the suite.
/// </summary>
public class RuleSortDocumentTests
{
    private const string Page = "docs/rule-sort.md";

    private static readonly Regex Direction = new(
        @"^## Direction: (?<name>[a-z]+)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex WithoutAnOrder = new(
        @"^### No order: (?<name>[a-zA-Z]+)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Without this the comparisons below pass on a page somebody emptied, because two empty sets
    /// agree.
    /// </summary>
    [Fact]
    public void ThePageCarriesASectionPerDirectionAndPerFieldWithNoOrder()
    {
        Assert.True(File.Exists(Path.Join(RepositoryFiles.Root(), Page)), Page + " is not in the tree.");
        Assert.NotEmpty(Documented(Direction));
        Assert.NotEmpty(Documented(WithoutAnOrder));
    }

    /// <summary>
    /// Every direction the vocabulary declares has a section, and every section names one.
    /// </summary>
    [Fact]
    public void EveryDirectionHasASectionAndEverySectionNamesADirection()
        => Assert.Equal(
            Enum.GetValues<RuleSortDirection>()
                .Select(RuleSortTable.NameOf)
                .OrderBy(name => name, StringComparer.Ordinal),
            Documented(Direction).OrderBy(name => name, StringComparer.Ordinal));

    /// <summary>
    /// The fields the table refuses to order by are the ones the page names, in both directions.
    /// </summary>
    /// <remarks>
    /// This is the half a reader is most likely to meet as a refusal, and it is the half the table
    /// declares by hand: everything else is sortable by default, so a field added to the
    /// vocabulary whose value is a list owes a line in the table and a section here, and neither
    /// of them is a line anybody would think to write without a red gate.
    /// </remarks>
    [Fact]
    public void EveryFieldWithNoOrderHasASectionAndEverySectionNamesOne()
        => Assert.Equal(
            RuleSortTable.UnsortableFields
                .Select(row => row.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
            Documented(WithoutAnOrder).OrderBy(name => name, StringComparer.Ordinal));

    /// <summary>
    /// The page names both members a document writes, so somebody reading it writes the names the
    /// reader reads rather than the ones the page invented.
    /// </summary>
    [Fact]
    public void ThePageNamesTheMembersADocumentWrites()
    {
        var page = RepositoryFiles.ReadFromRoot(Page);

        Assert.Contains("`" + RuleSortReader.SortMember + "`", page, StringComparison.Ordinal);
        Assert.Contains("`" + RuleSortReader.LimitMember + "`", page, StringComparison.Ordinal);
        Assert.Contains("\"" + RuleSortTable.FieldMember + "\"", page, StringComparison.Ordinal);
        Assert.Contains("\"" + RuleSortTable.DirectionMember + "\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page says a cap written without an order is refused, which is the one rule on this page
    /// that is about a pair of members and the one a reader is most likely to meet.
    /// </summary>
    [Fact]
    public void ThePageStatesThatACapWithNoOrderIsRefused()
    {
        var page = RepositoryFiles.ReadFromRoot(Page);

        Assert.Contains("A cap written without a sort is refused", page, StringComparison.Ordinal);
    }

    private static string[] Documented(Regex marker)
        => marker.Matches(RepositoryFiles.ReadFromRoot(Page))
            .Select(match => match.Groups["name"].Value)
            .ToArray();
}
