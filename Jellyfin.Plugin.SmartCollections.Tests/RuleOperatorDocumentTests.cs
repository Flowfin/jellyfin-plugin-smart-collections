using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The operator set is only closed if somebody can read what is in it. These tests hold
/// <c>docs/rule-operators.md</c> to the table in both directions: an operator with no section, a
/// section for an operator that does not exist, a type list that has drifted and a semantics
/// sentence that has drifted all red the suite.
/// </summary>
public class RuleOperatorDocumentTests
{
    private const string Page = "docs/rule-operators.md";

    /// <summary>
    /// The word the page writes where an operator takes no value. An empty line would be
    /// indistinguishable from a line somebody deleted.
    /// </summary>
    private const string NoValueTypes = "none";

    private static readonly Regex Section = new(
        @"^## Operator: (?<name>[A-Za-z]+)\r?\n\r?\nValue types: (?<types>.+?)\r?\n\r?\nSemantics: (?<semantics>.+?)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private sealed record DocumentedOperator(string Types, string Semantics);

    private static IReadOnlyDictionary<string, DocumentedOperator> Documented()
        => Section.Matches(RepositoryFiles.ReadFromRoot(Page))
            .ToDictionary(
                section => section.Groups["name"].Value,
                section => new DocumentedOperator(section.Groups["types"].Value, section.Groups["semantics"].Value),
                StringComparer.Ordinal);

    private static string WrittenTypes(RuleOperatorRow row)
        => row.TakesAValue ? string.Join(", ", row.ValueTypes) : NoValueTypes;

    /// <summary>
    /// Without this the comparisons below pass on a page somebody emptied, because two empty sets
    /// agree and every documented operator is then trivially correct.
    /// </summary>
    [Fact]
    public void ThePageCarriesASectionPerOperator()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryFiles.Root(), Page)), Page + " is not in the tree.");
        Assert.NotEmpty(Documented());
    }

    [Fact]
    public void EveryOperatorHasASectionAndEverySectionNamesAnOperator()
    {
        Assert.Equal(
            RuleOperatorTable.Names,
            Documented().Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void EverySectionCarriesTheValueTypesTheTableDeclares()
    {
        var documented = Documented();

        foreach (var row in RuleOperatorTable.Rows)
        {
            Assert.Equal(WrittenTypes(row), documented[row.Name].Types);
        }
    }

    [Fact]
    public void EveryOperatorHasOneSentenceOfSemanticsAndThePageCarriesIt()
    {
        var documented = Documented();

        foreach (var row in RuleOperatorTable.Rows)
        {
            Assert.Equal(row.Semantics, documented[row.Name].Semantics);
        }
    }

    /// <summary>
    /// The refusal lives in <c>rule-language.md</c> and the replacements live in the table, so
    /// this reads that the operator page points at the first rather than restating it.
    /// </summary>
    [Fact]
    public void ThePageSendsAReaderLookingForRegularExpressionsToTheRefusal()
    {
        var page = RepositoryFiles.ReadFromRoot(Page);

        Assert.Contains("matchRegex", page, StringComparison.Ordinal);
        Assert.Contains("rule-language.md", page, StringComparison.Ordinal);
    }
}
