using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The item kind list is only closed if somebody can read what is in it. These tests hold the
/// kind sections of <c>docs/rule-fields.md</c> to the table in both directions: a kind with no
/// section, a section for a kind that does not exist, a server member that has drifted and a
/// semantics sentence that has drifted all red the suite.
/// </summary>
public class RuleItemKindDocumentTests
{
    private const string Page = "docs/rule-fields.md";

    /// <summary>
    /// What the page prefixes the server member with, so the line names the enumeration as well as
    /// the member.
    /// </summary>
    private const string SelectsPrefix = "BaseItemKind.";

    private static readonly Regex Section = new(
        @"^## Item kind: (?<name>[a-z]+)\r?\n\r?\nSelects: (?<selects>.+?)\r?\n\r?\nSemantics: (?<semantics>.+?)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private sealed record DocumentedKind(string Selects, string Semantics);

    private static IReadOnlyDictionary<string, DocumentedKind> Documented()
        => Section.Matches(RepositoryFiles.ReadFromRoot(Page))
            .ToDictionary(
                section => section.Groups["name"].Value,
                section => new DocumentedKind(
                    section.Groups["selects"].Value,
                    section.Groups["semantics"].Value),
                StringComparer.Ordinal);

    /// <summary>
    /// Without this the comparisons below pass on a page somebody emptied, because two empty sets
    /// agree and every documented kind is then trivially correct.
    /// </summary>
    [Fact]
    public void ThePageCarriesASectionPerKind()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryFiles.Root(), Page)), Page + " is not in the tree.");
        Assert.NotEmpty(Documented());
    }

    [Fact]
    public void EveryKindHasASectionAndEverySectionNamesAKind()
        => Assert.Equal(
            RuleItemKindTable.Names.OrderBy(name => name, StringComparer.Ordinal),
            Documented().Keys.OrderBy(name => name, StringComparer.Ordinal));

    [Fact]
    public void EverySectionNamesTheServerMemberTheTableSelects()
    {
        var documented = Documented();

        foreach (var row in RuleItemKindTable.Rows)
        {
            Assert.Equal(SelectsPrefix + row.ServerKind.ToString(), documented[row.Name].Selects);
        }
    }

    [Fact]
    public void EverySectionCarriesTheSemanticsSentenceTheTableDeclares()
    {
        var documented = Documented();

        foreach (var row in RuleItemKindTable.Rows)
        {
            Assert.Equal(row.Semantics, documented[row.Name].Semantics);
        }
    }

    /// <summary>
    /// The page tells a reader the member is required and the plugin refuses a document without
    /// it. A page that stopped saying so while the stage kept refusing would send an operator
    /// looking for a fault in their document.
    /// </summary>
    [Fact]
    public void ThePageNamesTheMemberARuleDeclaresItsScopeIn()
        => Assert.Contains(
            "`" + RuleItemScopeReader.CollectsMember + "`",
            RepositoryFiles.ReadFromRoot(Page),
            StringComparison.Ordinal);
}
