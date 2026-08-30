using System;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The operator set is closed, and these are the properties that make it closed rather than
/// merely written down: every operator has exactly one row, one name no other operator answers
/// to, and one answer about every type at each of the two ends of a condition.
///
/// THE TWO ENDS ARE ASKED SEPARATELY HERE BECAUSE THEY ARE TWO QUESTIONS. A condition names a
/// field and writes a value beside it, and until 2026-08-30 one column answered for both. The
/// tests below ask <c>AcceptsField</c> about the field and <c>AcceptsValue</c> about the value,
/// and the row where the two answers differ is the row that column made unreachable.
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
    [InlineData(RuleOperator.WithinLast, RuleValueType.Date, true)]
    [InlineData(RuleOperator.WithinLast, RuleValueType.Duration, false)]
    [InlineData(RuleOperator.IsEmpty, RuleValueType.String, true)]
    [InlineData(RuleOperator.IsEmpty, RuleValueType.Boolean, true)]
    public void AnOperatorAnswersForEveryFieldType(RuleOperator @operator, RuleValueType fieldType, bool accepted)
    {
        Assert.Equal(accepted, RuleOperatorTable.AcceptsField(@operator, fieldType));
    }

    [Theory]
    [InlineData(RuleOperator.Equals, RuleValueType.Boolean, true)]
    [InlineData(RuleOperator.Equals, RuleValueType.Date, true)]
    [InlineData(RuleOperator.In, RuleValueType.Enumeration, true)]
    [InlineData(RuleOperator.Contains, RuleValueType.String, true)]
    [InlineData(RuleOperator.Contains, RuleValueType.Integer, false)]
    [InlineData(RuleOperator.GreaterThan, RuleValueType.Integer, true)]
    [InlineData(RuleOperator.GreaterThan, RuleValueType.String, false)]
    [InlineData(RuleOperator.Before, RuleValueType.Date, true)]
    [InlineData(RuleOperator.Before, RuleValueType.Duration, false)]
    [InlineData(RuleOperator.WithinLast, RuleValueType.Duration, true)]
    [InlineData(RuleOperator.WithinLast, RuleValueType.Date, false)]
    [InlineData(RuleOperator.IsEmpty, RuleValueType.String, false)]
    public void AnOperatorAnswersForEveryValueType(RuleOperator @operator, RuleValueType valueType, bool accepted)
    {
        Assert.Equal(accepted, RuleOperatorTable.AcceptsValue(@operator, valueType));
    }

    /// <summary>
    /// The row the two columns exist for, asserted as the pair rather than as two separate
    /// answers: <c>withinLast</c> applies to a field holding an instant and takes a length of time
    /// beside it, and the single column said the second of those about both ends.
    /// </summary>
    [Fact]
    public void WithinLastAppliesToADateFieldAndTakesADurationBesideIt()
    {
        Assert.True(RuleOperatorTable.AcceptsField(RuleOperator.WithinLast, RuleValueType.Date));
        Assert.True(RuleOperatorTable.AcceptsValue(RuleOperator.WithinLast, RuleValueType.Duration));
        Assert.False(RuleOperatorTable.AcceptsField(RuleOperator.WithinLast, RuleValueType.Duration));
        Assert.False(RuleOperatorTable.AcceptsValue(RuleOperator.WithinLast, RuleValueType.Date));
    }

    /// <summary>
    /// An operator applying to no field type at all is one no rule could ever name. The two
    /// operators that take no value are not that case: their value end is empty on purpose and
    /// their field end is every declared type, which the test below reads rather than assumes.
    /// </summary>
    [Fact]
    public void EveryOperatorAppliesToAtLeastOneFieldType()
    {
        foreach (var row in RuleOperatorTable.Rows)
        {
            Assert.NotEmpty(row.FieldTypes);
        }
    }

    [Fact]
    public void TheOperatorsThatTakeNoValueApplyToAFieldOfEveryDeclaredType()
    {
        foreach (var row in RuleOperatorTable.Rows.Where(row => !row.TakesAValue))
        {
            Assert.Equal(
                Enum.GetValues<RuleValueType>().OrderBy(type => type.ToString(), StringComparer.Ordinal),
                row.FieldTypes.OrderBy(type => type.ToString(), StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The refusal the closed set exists for, at the value end: it names the operator and the
    /// type, and it names what the operator does take, because that is what repairing the value
    /// needs.
    /// </summary>
    [Fact]
    public void AValueOfATypeAnOperatorDoesNotTakeIsRefusedNamingBoth()
    {
        var error = RuleOperatorTable.RefuseValueType(RuleOperator.Contains, RuleValueType.Integer, Pointer);

        Assert.Equal(Pointer, error.Pointer);
        Assert.Equal(
            "The operator \"contains\" does not take a value of type Integer. It takes String.",
            error.Message);
    }

    /// <summary>
    /// The same question at the other end, and it is a different sentence for that reason. A
    /// reader meeting one of the two has to be able to say which end it is about without opening
    /// the table, which is what one column answering for both could not offer.
    /// </summary>
    [Fact]
    public void AFieldOfATypeAnOperatorDoesNotApplyToIsRefusedNamingBoth()
    {
        var error = RuleOperatorTable.RefuseFieldType(RuleOperator.WithinLast, RuleValueType.String, Pointer);

        Assert.Equal(Pointer, error.Pointer);
        Assert.Equal(
            "The operator \"withinLast\" does not apply to a field of type String. It applies to a field of type Date.",
            error.Message);
    }

    /// <summary>
    /// The other repair, and it is a different sentence because deleting a value and writing it in
    /// another form are different things to do.
    /// </summary>
    [Fact]
    public void AnOperatorThatTakesNoValueSaysSoRatherThanListingAnEmptySet()
    {
        Assert.Equal(
            "The operator \"isEmpty\" takes no value, and this condition writes one of type String.",
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
    /// then looked at it would report every rule as broken. Both ends refuse it, because a
    /// validator asking the wrong one of the two is the mistake the split is against.
    /// </summary>
    [Fact]
    public void ThereIsNoRefusalForAPairTheTableAccepts()
    {
        var value = Assert.Throws<ArgumentException>(
            () => RuleOperatorTable.RefuseValueType(RuleOperator.Contains, RuleValueType.String, Pointer));

        Assert.Equal("operator", value.ParamName);

        var field = Assert.Throws<ArgumentException>(
            () => RuleOperatorTable.RefuseFieldType(RuleOperator.WithinLast, RuleValueType.Date, Pointer));

        Assert.Equal("operator", field.ParamName);
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
