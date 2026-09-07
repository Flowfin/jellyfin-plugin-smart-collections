using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The vocabulary is only closed if somebody can read what is in it. These tests hold
/// <c>docs/rule-fields.md</c> to the table in both directions: a field with no section, a section
/// for a field that does not exist, a type that has drifted, an operator list that has drifted, a
/// semantics sentence that has drifted and a page saying a field is read after the query while the
/// table narrows on it all red the suite.
/// </summary>
public class RuleFieldDocumentTests
{
    private const string Page = "docs/rule-fields.md";

    /// <summary>
    /// The word the page writes where the query answers none of a field's operators. An empty line
    /// would be indistinguishable from a line somebody deleted.
    /// </summary>
    private const string None = "none";

    /// <summary>
    /// The post-query list, which is the second thing this page is held to. A field reaches the
    /// library through a query property or it does not, and the ones that do not are what the
    /// post-query stage exists for, so the page carries them as a list with a reason each rather
    /// than only as one line inside each field's own section.
    /// </summary>
    private static readonly Regex PostQuerySection = new(
        @"^## Read after the query: (?<name>[A-Za-z]+)\r?\n\r?\nReason: (?<reason>.+?)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex Section = new(
        @"^## Field: (?<name>[A-Za-z]+)\r?\n\r?\nValue type: (?<type>.+?)\r?\n\r?\nOperators: (?<operators>.+?)\r?\n\r?\nKinds: (?<kinds>.+?)\r?\n\r?\nAnswered by the query: (?<reach>.+?)\r?\n\r?\nSemantics: (?<semantics>.+?)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private sealed record DocumentedField(string Type, string Operators, string Kinds, string Reach, string Semantics);

    private static IReadOnlyDictionary<string, DocumentedField> Documented()
        => Section.Matches(RepositoryFiles.ReadFromRoot(Page))
            .ToDictionary(
                section => section.Groups["name"].Value,
                section => new DocumentedField(
                    section.Groups["type"].Value,
                    section.Groups["operators"].Value,
                    section.Groups["kinds"].Value,
                    section.Groups["reach"].Value,
                    section.Groups["semantics"].Value),
                StringComparer.Ordinal);

    private static string WrittenOperators(RuleFieldRow row)
        => string.Join(", ", row.Operators.Select(@operator => RuleOperatorTable.Of(@operator).Name));

    private static string WrittenKinds(RuleFieldRow row)
        => string.Join(", ", row.Kinds.Select(kind => RuleItemKindTable.Of(kind).Name));

    /// <summary>
    /// The operators the query answers a field under, as the page writes them.
    /// </summary>
    /// <remarks>
    /// THIS LINE USED TO BE ONE QUERY PROPERTY OR THE WORDS "after the query", read off a column on
    /// the field row. #31 moved that mark onto the field and operator pair on 2026-09-04, so the
    /// line is a list rather than a name: a field the query narrows on almost always has operators
    /// it answers and operators it does not, and the old line said the first thing about all of
    /// them.
    /// </remarks>
    /// <param name="row">The field.</param>
    /// <returns>The line the page has to carry.</returns>
    private static string WrittenReach(RuleFieldRow row)
    {
        var answered = RuleQueryTable.OperatorsAnswered(row.Field);

        return answered.Count == 0
            ? None
            : string.Join(", ", answered.Select(@operator => RuleOperatorTable.Of(@operator).Name));
    }

    /// <summary>
    /// Without this the comparisons below pass on a page somebody emptied, because two empty sets
    /// agree and every documented field is then trivially correct.
    /// </summary>
    [Fact]
    public void ThePageCarriesASectionPerField()
    {
        Assert.True(File.Exists(Path.Join(RepositoryFiles.Root(), Page)), Page + " is not in the tree.");
        Assert.NotEmpty(Documented());
    }

    [Fact]
    public void EveryFieldHasASectionAndEverySectionNamesAField()
    {
        Assert.Equal(
            RuleFieldTable.Names,
            Documented().Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void EverySectionCarriesTheValueTypeTheTableDeclares()
    {
        var documented = Documented();

        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.Equal(row.ValueType.ToString(), documented[row.Name].Type);
        }
    }

    [Fact]
    public void EverySectionCarriesTheOperatorsTheTableDeclaresInTheOrderItDeclaresThem()
    {
        var documented = Documented();

        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.Equal(WrittenOperators(row), documented[row.Name].Operators);
        }
    }

    /// <summary>
    /// The kinds column, in the order the table declares them. A page saying a field applies to
    /// both kinds while the row narrows it to one would send an operator to write a rule the read
    /// refuses, with the page in their hand saying it should have been accepted.
    /// </summary>
    [Fact]
    public void EverySectionCarriesTheKindsTheTableDeclaresInTheOrderItDeclaresThem()
    {
        var documented = Documented();

        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.Equal(WrittenKinds(row), documented[row.Name].Kinds);
        }
    }

    /// <summary>
    /// The page says no document anybody can write reaches the refusal that reads the kinds
    /// column. That sentence is a statement about the vocabulary, so it goes stale on the day a
    /// field is narrowed and this is what says so.
    /// </summary>
    [Fact]
    public void ThePageSaysNoWritableDocumentReachesTheScopeRefusalAndThatIsStillTrue()
    {
        Assert.Contains(
            "Every field on this page names both kinds",
            RepositoryFiles.ReadFromRoot(Page),
            StringComparison.Ordinal);

        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.Equal(RuleItemKindTable.Rows.Count, row.Kinds.Count);
        }
    }

    /// <summary>
    /// This is where the mark is held to something a reader sees. It cannot contradict itself -
    /// the answer is the presence of a row in the compile table - but it can drift away from the
    /// page somebody consults, and a pair silently moving out of the query and into the stage is
    /// the change that turns a narrow query into a walk over the whole scope.
    /// </summary>
    [Fact]
    public void EverySectionSaysHowTheFieldReachesTheLibraryAndSaysWhatTheTableSays()
    {
        var documented = Documented();

        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.Equal(WrittenReach(row), documented[row.Name].Reach);
        }
    }

    [Fact]
    public void EverySectionThatSaysNoneDescribesAFieldNoPairAnswers()
    {
        foreach (var (name, documented) in Documented())
        {
            if (!string.Equals(documented.Reach, None, StringComparison.Ordinal))
            {
                continue;
            }

            var row = RuleFieldTable.Find(name);

            Assert.NotNull(row);
            Assert.False(RuleQueryTable.Narrows(row!.Field));
            Assert.Empty(RuleQueryTable.OperatorsAnswered(row.Field));
        }
    }

    [Fact]
    public void EveryFieldHasOneSentenceOfSemanticsAndThePageCarriesIt()
    {
        var documented = Documented();

        foreach (var row in RuleFieldTable.Rows)
        {
            Assert.Equal(row.Semantics, documented[row.Name].Semantics);
        }
    }

    /// <summary>
    /// The operator sentences and the value forms live on their own pages, so this reads that
    /// this one points at them rather than restating either.
    /// </summary>
    [Fact]
    public void ThePageSendsAReaderToTheOperatorSetAndToTheValueForms()
    {
        var page = RepositoryFiles.ReadFromRoot(Page);

        Assert.Contains("rule-operators.md", page, StringComparison.Ordinal);
        Assert.Contains("rule-values.md", page, StringComparison.Ordinal);
        Assert.Contains("rule-language.md", page, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> Listed()
        => PostQuerySection.Matches(RepositoryFiles.ReadFromRoot(Page))
            .ToDictionary(
                section => section.Groups["name"].Value,
                section => section.Groups["reason"].Value,
                StringComparer.Ordinal);

    /// <summary>
    /// Without this the comparison below passes on a page whose list somebody emptied, at the same
    /// time as a table with no post-query row, and two empty sets agree.
    /// </summary>
    [Fact]
    public void ThePageListsTheFieldsReadAfterTheQuery()
    {
        Assert.NotEmpty(Listed());
        Assert.Contains(RuleFieldTable.Rows, row => !RuleQueryTable.Narrows(row.Field));
    }

    /// <summary>
    /// The list and the table say the same thing in both directions. A field that moves into the
    /// post-query stage without a section here reds, and a section for a field the query narrows
    /// on reds too, which is what makes adding to the stage a visible decision rather than a
    /// nullable column somebody changed.
    /// </summary>
    [Fact]
    public void TheListIsExactlyTheFieldsNoPairAnswers()
        => Assert.Equal(
            RuleFieldTable.Rows
                .Where(row => !RuleQueryTable.Narrows(row.Field))
                .Select(row => row.Name)
                .OrderBy(name => name, StringComparer.Ordinal),
            Listed().Keys.OrderBy(name => name, StringComparer.Ordinal));

    /// <summary>
    /// Every entry says why the query cannot carry the field. A list of names with no reasons is
    /// the state this page was in before the list existed, one line per field and no argument.
    /// </summary>
    [Fact]
    public void EveryEntryOnTheListCarriesAReason()
    {
        foreach (var (name, reason) in Listed())
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), name + " is listed with no reason.");
            Assert.EndsWith(".", reason, StringComparison.Ordinal);
        }
    }
}
