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
/// section for an operator that does not exist, either type list having drifted and a semantics
/// sentence that has drifted all red the suite.
///
/// EITHER LIST, RATHER THAN THE LIST. A section carries a field-type line and a value-type line,
/// the two differ on three rows, and a page holding one of them to the table while reading the
/// other off nothing would leave exactly the rows the second column exists for unread.
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
        @"^## Operator: (?<name>[A-Za-z]+)\r?\n\r?\nField types: (?<fieldTypes>.+?)\r?\n\r?\nValue types: (?<valueTypes>.+?)\r?\n\r?\nValues written: (?<values>.+?)\r?\n\r?\nSemantics: (?<semantics>.+?)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private sealed record DocumentedOperator(string FieldTypes, string ValueTypes, string Values, string Semantics);

    private static IReadOnlyDictionary<string, DocumentedOperator> Documented()
        => Section.Matches(RepositoryFiles.ReadFromRoot(Page))
            .ToDictionary(
                section => section.Groups["name"].Value,
                section => new DocumentedOperator(
                    section.Groups["fieldTypes"].Value,
                    section.Groups["valueTypes"].Value,
                    section.Groups["values"].Value,
                    section.Groups["semantics"].Value),
                StringComparer.Ordinal);

    private static string WrittenFieldTypes(RuleOperatorRow row)
        => string.Join(", ", row.FieldTypes);

    private static string WrittenValueTypes(RuleOperatorRow row)
        => row.TakesAValue ? string.Join(", ", row.ValueTypes) : NoValueTypes;

    /// <summary>
    /// The words the page writes for how many values an operator is written with. Words rather
    /// than a numeral, because two of the three answers are not numbers: an operator taking none
    /// carries no member at all, and one taking a list carries an array whose length the document
    /// chooses.
    /// </summary>
    private static string WrittenValues(RuleOperatorRow row)
    {
        if (!row.TakesAValue)
        {
            return NoValueTypes;
        }

        return row.TakesAList ? "a list" : "one";
    }

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
    public void EverySectionCarriesTheFieldTypesTheTableDeclares()
    {
        var documented = Documented();

        foreach (var row in RuleOperatorTable.Rows)
        {
            Assert.Equal(WrittenFieldTypes(row), documented[row.Name].FieldTypes);
        }
    }

    [Fact]
    public void EverySectionCarriesTheValueTypesTheTableDeclares()
    {
        var documented = Documented();

        foreach (var row in RuleOperatorTable.Rows)
        {
            Assert.Equal(WrittenValueTypes(row), documented[row.Name].ValueTypes);
        }
    }

    /// <summary>
    /// The third column, held the same way as the two above it. Without this the page could say
    /// that <c>in</c> is written with one value while the table says it is written with a list,
    /// and the two would disagree about the one thing a reader consults this page for before they
    /// write an <c>in</c> condition.
    /// </summary>
    [Fact]
    public void EverySectionCarriesHowManyValuesTheTableDeclares()
    {
        var documented = Documented();

        foreach (var row in RuleOperatorTable.Rows)
        {
            Assert.Equal(WrittenValues(row), documented[row.Name].Values);
        }
    }

    /// <summary>
    /// The comparison above passes on a page that wrote one answer on all seventeen sections,
    /// because a table declaring one answer on all seventeen would agree with it. This is the
    /// reading that says the page separates the three answers exactly where the table does, and it
    /// names the rows rather than counting them.
    /// </summary>
    [Fact]
    public void ThePageSeparatesTheThreeAnswersWhereTheTableDoes()
    {
        var documented = Documented();

        foreach (var answer in new[] { "one", "a list", NoValueTypes })
        {
            var onThePage = documented
                .Where(section => string.Equals(section.Value.Values, answer, StringComparison.Ordinal))
                .Select(section => section.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var inTheTable = RuleOperatorTable.Rows
                .Where(row => string.Equals(WrittenValues(row), answer, StringComparison.Ordinal))
                .Select(row => row.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(inTheTable, onThePage);
            Assert.NotEmpty(onThePage);
        }

        Assert.Equal("a list", documented["in"].Values);
        Assert.Equal("a list", documented["notIn"].Values);
        Assert.Equal("one", documented["equals"].Values);
        Assert.Equal(NoValueTypes, documented["isEmpty"].Values);
    }

    /// <summary>
    /// The two comparisons above would both pass on a page that wrote one list twice, because the
    /// table it is compared against writes one list twice on fourteen of the seventeen rows. This
    /// is the reading that says the page separates the two lines exactly where the table does, and
    /// it names the row the second column was added for rather than counting to three.
    /// </summary>
    [Fact]
    public void ThePageSeparatesTheTwoListsWhereTheTableDoes()
    {
        var documented = Documented();

        var differingOnThePage = documented
            .Where(section => !string.Equals(section.Value.FieldTypes, section.Value.ValueTypes, StringComparison.Ordinal))
            .Select(section => section.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var differingInTheTable = RuleOperatorTable.Rows
            .Where(row => !string.Equals(WrittenFieldTypes(row), WrittenValueTypes(row), StringComparison.Ordinal))
            .Select(row => row.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(differingInTheTable, differingOnThePage);
        Assert.Contains("withinLast", differingOnThePage);
        Assert.Equal("Date", documented["withinLast"].FieldTypes);
        Assert.Equal("Duration", documented["withinLast"].ValueTypes);
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
