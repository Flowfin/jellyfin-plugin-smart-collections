using System;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The operator set is closed, and these are the properties that make it closed rather than
/// merely written down: every operator has exactly one row, one name no other operator answers
/// to, and one answer about every value type.
/// </summary>
public class RuleOperatorTableTests
{
    private const string Pointer = "/conditions/0";

    [Fact]
    public void EveryDeclaredOperatorHasExactlyOneRow()
    {
        Assert.Equal(
            Enum.GetValues<RuleOperator>().OrderBy(o => o.ToString(), StringComparer.Ordinal),
            RuleOperatorTable.Rows.Select(row => row.Operator).OrderBy(o => o.ToString(), StringComparer.Ordinal));
    }

    /// <summary>
    /// Two operators answering to one name would make a document's meaning depend on which row
    /// the lookup reached first.
    /// </summary>
    [Fact]
    public void NoTwoOperatorsShareAName()
    {
        var names = RuleOperatorTable.Rows.Select(row => row.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The written name is what a rule document holds, so it is declared rather than derived from
    /// the member. This is the reading that says the two currently agree up to the first letter,
    /// and it is deliberately not the mechanism: renaming a member does not rename the token.
    /// </summary>
    [Fact]
    public void EveryNameIsTheMemberWithALowerCaseFirstLetter()
    {
        foreach (var row in RuleOperatorTable.Rows)
        {
            var member = row.Operator.ToString();
            Assert.Equal(char.ToLowerInvariant(member[0]) + member[1..], row.Name);
        }
    }

    [Fact]
    public void EveryOperatorHasASentenceOfSemanticsEndingInAStop()
    {
        foreach (var row in RuleOperatorTable.Rows)
        {
            Assert.NotEmpty(row.Semantics);
            Assert.EndsWith(".", row.Semantics, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ADocumentsNameFindsItsRow()
    {
        var row = RuleOperatorTable.Find("greaterThanOrEqual");

        Assert.NotNull(row);
        Assert.Equal(RuleOperator.GreaterThanOrEqual, row!.Operator);
    }

    /// <summary>
    /// Ordinal, because an operator name is a wire token rather than a word in a language. A
    /// culture-sensitive lookup would let a server's locale decide whether a document names an
    /// operator, which is the failure the whole engine is held against.
    /// </summary>
    [Fact]
    public void ANameThatDiffersInCaseIsNotAnOperator()
    {
        Assert.Null(RuleOperatorTable.Find("GreaterThan"));
        Assert.Null(RuleOperatorTable.Find("greaterthan"));
    }

    [Fact]
    public void AnUnknownNameIsRefusedWithEveryLegalOne()
    {
        var error = RuleOperatorTable.RefuseUnknownOperator("matchRegex", Pointer);

        Assert.Equal(Pointer, error.Pointer);
        Assert.StartsWith("There is no operator called \"matchRegex\".", error.Message, StringComparison.Ordinal);

        foreach (var row in RuleOperatorTable.Rows)
        {
            Assert.Contains(row.Name, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRefusalListsTheNamesInOneDeclaredOrder()
    {
        Assert.Equal(
            RuleOperatorTable.Rows.Select(row => row.Name).OrderBy(name => name, StringComparer.Ordinal),
            RuleOperatorTable.Names);
    }

    [Theory]
    [InlineData(RuleOperator.Equals, RuleValueType.Boolean, true)]
    [InlineData(RuleOperator.Equals, RuleValueType.Date, true)]
    [InlineData(RuleOperator.In, RuleValueType.Enumeration, true)]
    [InlineData(RuleOperator.Contains, RuleValueType.String, true)]
    [InlineData(RuleOperator.Contains, RuleValueType.Integer, false)]
    [InlineData(RuleOperator.GreaterThan, RuleValueType.Integer, true)]
    [InlineData(RuleOperator.GreaterThan, RuleValueType.String, false)]
    [InlineData(RuleOperator.GreaterThan, RuleValueType.Boolean, false)]
    [InlineData(RuleOperator.GreaterThan, RuleValueType.Enumeration, false)]
    [InlineData(RuleOperator.Before, RuleValueType.Date, true)]
    [InlineData(RuleOperator.Before, RuleValueType.Duration, false)]
    [InlineData(RuleOperator.WithinLast, RuleValueType.Duration, true)]
    [InlineData(RuleOperator.WithinLast, RuleValueType.Date, false)]
    [InlineData(RuleOperator.IsEmpty, RuleValueType.String, false)]
    public void AnOperatorAnswersForEveryValueType(RuleOperator @operator, RuleValueType type, bool accepted)
    {
        Assert.Equal(accepted, RuleOperatorTable.Accepts(@operator, type));
    }

    /// <summary>
    /// The refusal the closed set exists for: it names the operator and the type, and it names
    /// what the operator does take, because that is what choosing another operator needs.
    /// </summary>
    [Fact]
    public void AnOperatorAppliedToATypeItDoesNotAcceptIsRefusedNamingBoth()
    {
        var error = RuleOperatorTable.RefuseValueType(RuleOperator.Contains, RuleValueType.Integer, Pointer);

        Assert.Equal(Pointer, error.Pointer);
        Assert.Equal(
            "The operator \"contains\" does not accept a value of type Integer. It accepts String.",
            error.Message);
    }

    /// <summary>
    /// The other repair, and it is a different sentence because deleting a value and choosing
    /// another operator are different things to do.
    /// </summary>
    [Fact]
    public void AnOperatorThatTakesNoValueSaysSoRatherThanListingAnEmptySet()
    {
        Assert.Equal(
            "The operator \"isEmpty\" takes no value, and this field declares String.",
            RuleOperatorTable.RefuseValueType(RuleOperator.IsEmpty, RuleValueType.String, Pointer).Message);
    }

    [Fact]
    public void ExactlyTwoOperatorsTakeNoValue()
    {
        Assert.Equal(
            new[] { "isEmpty", "isNotEmpty" },
            RuleOperatorTable.Rows.Where(row => !row.TakesAValue).Select(row => row.Name).ToArray());
    }

    /// <summary>
    /// Asked for a refusal that is not owed, the table throws rather than manufacturing a message
    /// saying an accepted pair is not accepted. A validator that built one for every condition and
    /// then looked at it would report every rule as broken.
    /// </summary>
    [Fact]
    public void ThereIsNoRefusalForAPairTheTableAccepts()
    {
        var thrown = Assert.Throws<ArgumentException>(
            () => RuleOperatorTable.RefuseValueType(RuleOperator.Contains, RuleValueType.String, Pointer));

        Assert.Equal("operator", thrown.ParamName);
    }

    [Fact]
    public void AnOperatorWithNoRowThrowsRatherThanAnsweringForOne()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => RuleOperatorTable.Of((RuleOperator)(-1)));

        Assert.Equal("operator", thrown.ParamName);
    }

    [Fact]
    public void ALookupWithNoNameIsRefusedAtTheCall()
    {
        Assert.Throws<ArgumentNullException>(() => RuleOperatorTable.Find(null!));
        Assert.Throws<ArgumentNullException>(() => RuleOperatorTable.RefuseUnknownOperator(null!, Pointer));
    }

    /// <summary>
    /// The refusal recorded in <c>rule-language.md</c>, read from the operator set rather than
    /// from the page: the name a person reaches for is not in the table, and the five the page
    /// names as its replacements are.
    /// </summary>
    [Fact]
    public void ThereIsNoRegularExpressionOperatorAndItsReplacementsAreAllHere()
    {
        Assert.Null(RuleOperatorTable.Find("matchRegex"));

        foreach (var name in new[] { "contains", "startsWith", "endsWith", "equals", "in" })
        {
            Assert.NotNull(RuleOperatorTable.Find(name));
        }
    }
}
