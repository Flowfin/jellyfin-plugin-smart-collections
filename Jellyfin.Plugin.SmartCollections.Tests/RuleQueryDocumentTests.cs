using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// Which comparisons the server's query answers is the boundary a reader of a rule most needs and
/// is least able to derive. These tests hold <c>docs/rule-queries.md</c> to the compile table in
/// both directions: a pair compiled with no section, a section for a pair that is not compiled, a
/// property name that has drifted and a semantics sentence that has drifted all red the suite.
/// </summary>
public class RuleQueryDocumentTests
{
    private const string Page = "docs/rule-queries.md";

    /// <summary>
    /// What the page prefixes a query property with, so the line names the type as well as the
    /// member.
    /// </summary>
    private const string QueryPrefix = "InternalItemsQuery.";

    private static readonly Regex Section = new(
        @"^## Pair: (?<field>[A-Za-z]+) (?<operator>[A-Za-z]+)\r?\n\r?\nWrites: (?<writes>.+?)\r?\n\r?\nSemantics: (?<semantics>.+?)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private sealed record DocumentedPair(string Writes, string Semantics);

    private static string Written(RuleQueryRow row)
        => RuleFieldTable.Of(row.Field).Name + " " + RuleOperatorTable.Of(row.Operator).Name;

    private static IReadOnlyDictionary<string, DocumentedPair> Documented()
        => Section.Matches(RepositoryFiles.ReadFromRoot(Page))
            .ToDictionary(
                section => section.Groups["field"].Value + " " + section.Groups["operator"].Value,
                section => new DocumentedPair(
                    section.Groups["writes"].Value,
                    section.Groups["semantics"].Value),
                StringComparer.Ordinal);

    /// <summary>
    /// Without this the comparisons below pass on a page somebody emptied, because two empty sets
    /// agree and every documented pair is then trivially correct.
    /// </summary>
    [Fact]
    public void ThePageCarriesASectionPerPair()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryFiles.Root(), Page)), Page + " is not in the tree.");
        Assert.NotEmpty(Documented());
    }

    [Fact]
    public void EveryCompiledPairHasASectionAndEverySectionNamesACompiledPair()
    {
        Assert.Equal(
            RuleQueryTable.Rows.Select(Written).OrderBy(pair => pair, StringComparer.Ordinal),
            Documented().Keys.OrderBy(pair => pair, StringComparer.Ordinal));
    }

    [Fact]
    public void EverySectionNamesThePropertyTheTableWrites()
    {
        var documented = Documented();

        foreach (var row in RuleQueryTable.Rows)
        {
            Assert.Equal(QueryPrefix + row.QueryProperty, documented[Written(row)].Writes);
        }
    }

    [Fact]
    public void EverySectionCarriesTheSemanticsTheTableDeclares()
    {
        var documented = Documented();

        foreach (var row in RuleQueryTable.Rows)
        {
            Assert.Equal(row.Semantics, documented[Written(row)].Semantics);
        }
    }

    /// <summary>
    /// The page's boundaries are the half a reader takes on trust, so the three that are decisions
    /// rather than descriptions are held to being written down. Each of these sentences is the
    /// reason a document this plugin accepts is not narrowed by the query, and a page that lost one
    /// would read as though the query answered everything.
    /// </summary>
    [Fact]
    public void ThePageStatesTheBoundariesTheCompilerHolds()
    {
        var document = RepositoryFiles.ReadFromRoot(Page);

        Assert.Contains(
            "THE COMPARISON IS THE SERVER'S AND NOT AN ORDINAL ONE.",
            document,
            StringComparison.Ordinal);
        Assert.Contains(
            "So `after` writes the value plus one tick and `before` writes the value minus",
            document,
            StringComparison.Ordinal);
        Assert.Contains(
            "Two conditions writing one property are refused",
            document,
            StringComparison.Ordinal);
    }
}
