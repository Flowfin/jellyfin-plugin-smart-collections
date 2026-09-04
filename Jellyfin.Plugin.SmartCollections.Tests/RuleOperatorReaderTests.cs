using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The stage that reads the operator each condition applies. The two done conditions this file
/// carries are the operator issue's last two: an operator applied to a type it does not accept is
/// refused with a message naming both, and an unknown operator is refused with a message listing
/// the legal ones for the field it was written against rather than all seventeen.
/// </summary>
public class RuleOperatorReaderTests
{
    private static RuleOperatorRead ReadRule(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");

        Assert.True(composition.IsAccepted, string.Join("; ", composition.Errors.Select(error => error.ToString())));

        var fields = RuleFieldReader.Read(root, composition.Group!, RuleItemKindTable.Rows);

        Assert.True(fields.IsAccepted, string.Join("; ", fields.Errors.Select(error => error.ToString())));

        return RuleOperatorReader.Read(root, fields.Fields);
    }

    private static string Rule(string conditions)
        => "{\"schemaVersion\":1,\"id\":\"a\",\"name\":\"A\",\"match\":{\"allOf\":[" + conditions + "]}}";

    [Fact]
    public void EveryConditionApplyingAnAcceptedOperatorIsResolvedToItsRow()
    {
        var read = ReadRule(Rule(
            "{\"field\":\"genres\",\"operator\":\"contains\",\"value\":\"Thriller\"},"
            + "{\"field\":\"productionYear\",\"operator\":\"greaterThanOrEqual\",\"value\":1990},"
            + "{\"field\":\"dateAdded\",\"operator\":\"withinLast\",\"value\":\"P30D\"}"));

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));
        Assert.Empty(read.Errors);
        Assert.Equal(
            [RuleOperator.Contains, RuleOperator.GreaterThanOrEqual, RuleOperator.WithinLast],
            read.Operators.Select(entry => entry.Operator.Operator).ToArray());
        Assert.Equal(
            [RuleField.Genres, RuleField.ProductionYear, RuleField.DateAdded],
            read.Operators.Select(entry => entry.Field.Field).ToArray());
        Assert.Equal(
            ["/match/allOf/0", "/match/allOf/1", "/match/allOf/2"],
            read.Operators.Select(entry => entry.Pointer).ToArray());
    }

    /// <summary>
    /// The third condition above is the one worth having its own test. It is the first rule this
    /// engine can express that writes a value of a type its field does not hold, and it was
    /// unwritable until the operator table declared its two ends separately.
    /// </summary>
    [Fact]
    public void ADateFieldTakesWithinLastNowThatTheTableDeclaresBothEnds()
    {
        var read = ReadRule(Rule("{\"field\":\"premiereDate\",\"operator\":\"withinLast\",\"value\":\"P365D\"}"));

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));

        var entry = Assert.Single(read.Operators);

        Assert.Equal(RuleOperator.WithinLast, entry.Operator.Operator);
        Assert.Equal(RuleValueType.Date, entry.Field.ValueType);
        Assert.Equal([RuleValueType.Duration], entry.Operator.ValueTypes);
    }

    /// <summary>
    /// The first of the two done conditions, watched rather than reasoned about: the message names
    /// the operator and the type, and it names what the operator does apply to.
    /// </summary>
    [Fact]
    public void AnOperatorAppliedToATypeItDoesNotAcceptIsRefusedNamingBoth()
    {
        var read = ReadRule(Rule("{\"field\":\"productionYear\",\"operator\":\"contains\",\"value\":\"5\"}"));

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Operators);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/operator", error.Pointer);
        Assert.Equal(
            "The operator \"contains\" does not apply to a field of type Integer. It applies to a field of type String.",
            error.Message);
    }

    /// <summary>
    /// The other half of that clause, and the reason the two refusals are not one. The operator is
    /// defined over strings and this field holds strings, so nothing about the TYPE is wrong; what
    /// is wrong is that a prefix test over a list of genres means nothing. A single message would
    /// tell whoever wrote this that the operator does not work on text, which is false.
    /// </summary>
    [Fact]
    public void AnOperatorItsFieldDoesNotOfferIsRefusedWithWhatThatFieldDoesAccept()
    {
        var read = ReadRule(Rule("{\"field\":\"genres\",\"operator\":\"startsWith\",\"value\":\"Thr\"}"));

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/operator", error.Pointer);
        Assert.Equal(
            "The field \"genres\" does not accept the operator \"startsWith\". It accepts contains, notContains, isEmpty, isNotEmpty.",
            error.Message);
    }

    /// <summary>
    /// The second done condition. It lists less than the clause asks for - the operators this
    /// FIELD accepts rather than the ones its type allows - and the assertions below say so in
    /// both directions, because a list that is merely shorter proves nothing.
    /// </summary>
    [Fact]
    public void AnUnknownOperatorIsRefusedWithTheOnesTheFieldAcceptsRatherThanAllOfThem()
    {
        var read = ReadRule(Rule("{\"field\":\"genres\",\"operator\":\"matchRegex\",\"value\":\".*\"}"));

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/operator", error.Pointer);
        Assert.Equal(
            "There is no operator called \"matchRegex\". The operators for a \"genres\" field are contains, notContains, isEmpty, isNotEmpty.",
            error.Message);

        var genres = RuleFieldTable.Of(RuleField.Genres);

        foreach (var @operator in genres.Operators)
        {
            Assert.Contains(RuleOperatorTable.Of(@operator).Name, error.Message, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("greaterThan", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("withinLast", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name that differs from a declared one only by case is a different name here, so the
    /// server's locale cannot decide whether a document named an operator.
    /// </summary>
    [Fact]
    public void AnOperatorNameSpelledWithTheWrongCaseIsUnknownRatherThanFolded()
    {
        var read = ReadRule(Rule("{\"field\":\"genres\",\"operator\":\"Contains\",\"value\":\"Thriller\"}"));

        Assert.Contains("\"Contains\"", Assert.Single(read.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConditionCarryingNoOperatorMemberIsRefusedAndTheRefusalListsWhatTheFieldAccepts()
    {
        var read = ReadRule(Rule("{\"field\":\"genres\",\"value\":\"Thriller\"}"));

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0", error.Pointer);
        Assert.Equal(
            "This condition applies no operator. A condition carries an \"operator\" member, and the operators for a \"genres\" field are contains, notContains, isEmpty, isNotEmpty.",
            error.Message);
    }

    /// <summary>
    /// Refused rather than read through ToString, for the reason the field stage gives about its
    /// own member: a number in the place a name goes is somebody writing something else there.
    /// </summary>
    [Fact]
    public void AnOperatorThatIsNotAStringIsRefusedRatherThanConverted()
    {
        var read = ReadRule(Rule("{\"field\":\"genres\",\"operator\":7,\"value\":\"Thriller\"}"));

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/operator", error.Pointer);
        Assert.Equal(
            "An operator is written as a string naming one of contains, notContains, isEmpty, isNotEmpty.",
            error.Message);
    }

    /// <summary>
    /// Every reason rather than the first, so repairing a rule is one pass rather than a sequence
    /// of edits and re-reads.
    /// </summary>
    [Fact]
    public void EveryConditionIsReadAndEveryRefusalIsReported()
    {
        var read = ReadRule(Rule(
            "{\"field\":\"genres\",\"operator\":\"matchRegex\",\"value\":\".*\"},"
            + "{\"field\":\"productionYear\",\"operator\":\"contains\",\"value\":\"5\"},"
            + "{\"field\":\"genres\",\"operator\":\"contains\",\"value\":\"Thriller\"}"));

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Operators);
        Assert.Equal(
            ["/match/allOf/0/operator", "/match/allOf/1/operator"],
            read.Errors.Select(error => error.Pointer).ToArray());
    }

    /// <summary>
    /// A nested group is walked the same way, because the stage reads what the field stage handed
    /// it and the field stage flattened the tree.
    /// </summary>
    [Fact]
    public void AConditionInsideANestedGroupIsReadTheSameWay()
    {
        var read = ReadRule(
            "{\"schemaVersion\":1,\"id\":\"a\",\"name\":\"A\",\"match\":{\"allOf\":[{\"anyOf\":["
            + "{\"field\":\"name\",\"operator\":\"startsWith\",\"value\":\"A\"}]}]}}");

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));
        Assert.Equal("/match/allOf/0/anyOf/0", Assert.Single(read.Operators).Pointer);
    }

    /// <summary>
    /// The two operators that take no value are accepted where their field declares them, which is
    /// what the operator table's field end being every declared type is for. Whether a value was
    /// written beside one is the next stage's question and not this one's.
    /// </summary>
    [Fact]
    public void AnOperatorThatTakesNoValueIsAcceptedWhereItsFieldDeclaresIt()
    {
        var read = ReadRule(Rule("{\"field\":\"overview\",\"operator\":\"isEmpty\"}"));

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));
        Assert.Equal(RuleOperator.IsEmpty, Assert.Single(read.Operators).Operator.Operator);
    }

    [Fact]
    public void AReadWithNoFieldsIsRefusedAtTheCall()
    {
        using var document = JsonDocument.Parse("{}");

        Assert.Throws<ArgumentNullException>(() => RuleOperatorReader.Read(document.RootElement, null!));
    }

    /// <summary>
    /// A field read taken against another document is a caller mistake rather than a bad rule, so
    /// it throws rather than producing a validation error somebody would show an operator.
    /// </summary>
    [Fact]
    public void AFieldReadFromAnotherDocumentThrowsRatherThanRefusing()
    {
        using var other = JsonDocument.Parse("{}");

        var thrown = Assert.Throws<ArgumentException>(
            () => RuleOperatorReader.Read(
                other.RootElement,
                [new RuleConditionField("/match/allOf/0", RuleFieldTable.Of(RuleField.Genres))]));

        Assert.Equal("document", thrown.ParamName);
    }
}
