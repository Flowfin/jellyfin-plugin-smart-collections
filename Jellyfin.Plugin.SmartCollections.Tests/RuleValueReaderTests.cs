using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The stage that reads the value each condition writes. The done condition this file carries is
/// the value-types issue's second: a value that does not parse is refused at validation with a
/// message naming the field, the value and the expected form.
///
/// The three sentences of that clause come from two places on purpose. The field is this stage's
/// to name, because it is the only one holding the row; the value and the form are the parser's,
/// so the words are the same wherever a value is parsed and the same words the reference page
/// carries.
/// </summary>
public class RuleValueReaderTests
{
    private static RuleValueRead ReadRule(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");

        Assert.True(composition.IsAccepted, string.Join("; ", composition.Errors.Select(error => error.ToString())));

        var fields = RuleFieldReader.Read(root, composition.Group!);

        Assert.True(fields.IsAccepted, string.Join("; ", fields.Errors.Select(error => error.ToString())));

        var operators = RuleOperatorReader.Read(root, fields.Fields);

        Assert.True(operators.IsAccepted, string.Join("; ", operators.Errors.Select(error => error.ToString())));

        return RuleValueReader.Read(root, operators.Operators);
    }

    private static string Rule(string conditions)
        => "{\"schemaVersion\":1,\"id\":\"a\",\"name\":\"A\",\"match\":{\"allOf\":[" + conditions + "]}}";

    [Fact]
    public void EveryConditionWritingAValueItsFieldHoldsIsParsedOnce()
    {
        var read = ReadRule(Rule(
            "{\"field\":\"genres\",\"operator\":\"contains\",\"value\":\"Thriller\"},"
            + "{\"field\":\"productionYear\",\"operator\":\"greaterThanOrEqual\",\"value\":1990},"
            + "{\"field\":\"communityRating\",\"operator\":\"greaterThan\",\"value\":8.1},"
            + "{\"field\":\"dateAdded\",\"operator\":\"after\",\"value\":\"2024-01-01\"},"
            + "{\"field\":\"runtime\",\"operator\":\"lessThan\",\"value\":\"PT90M\"}"));

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));
        Assert.Empty(read.Errors);
        Assert.Equal(
            ["/match/allOf/0", "/match/allOf/1", "/match/allOf/2", "/match/allOf/3", "/match/allOf/4"],
            read.Conditions.Select(entry => entry.Pointer).ToArray());
        Assert.Equal(
            [
                RuleValueType.String,
                RuleValueType.Integer,
                RuleValueType.Decimal,
                RuleValueType.Date,
                RuleValueType.Duration
            ],
            read.Conditions.Select(entry => Assert.Single(entry.Values).Type).ToArray());
        Assert.Equal(
            ["Thriller", 1990L, 8.1m, new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), TimeSpan.FromMinutes(90)],
            read.Conditions.Select(entry => entry.Values[0].Value).ToArray());
    }

    /// <summary>
    /// The row where the two ends of a condition hold different types. The field holds an instant
    /// and the value beside it is a length of time, so the value is parsed as a duration and not
    /// as a date.
    /// </summary>
    [Fact]
    public void WithinLastParsesItsValueAgainstTheOperatorsTypeRatherThanTheFieldsOwn()
    {
        var read = ReadRule(Rule("{\"field\":\"dateAdded\",\"operator\":\"withinLast\",\"value\":\"P30D\"}"));

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));

        var entry = Assert.Single(read.Conditions);
        var value = Assert.Single(entry.Values);

        Assert.Equal(RuleValueType.Date, entry.Field.ValueType);
        Assert.Equal(RuleValueType.Duration, value.Type);
        Assert.Equal(TimeSpan.FromDays(30), value.Value);
    }

    /// <summary>
    /// The done condition, watched rather than reasoned about: three things in one message, and
    /// the pointer at the value rather than at the condition.
    /// </summary>
    [Fact]
    public void AValueThatDoesNotParseIsRefusedNamingTheFieldTheValueAndTheForm()
    {
        var read = ReadRule(Rule("{\"field\":\"productionYear\",\"operator\":\"equals\",\"value\":\"1994\"}"));

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Conditions);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/value", error.Pointer);
        Assert.Contains("\"productionYear\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("\"1994\"", error.Message, StringComparison.Ordinal);
        Assert.Contains(RuleValueForm.Of(RuleValueType.Integer), error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The form in the message above is the parser's own sentence and not a second wording, which
    /// is what keeps a refusal and the reference page saying the same thing.
    /// </summary>
    [Fact]
    public void TheFormInTheRefusalIsTheOneTheParserWrites()
    {
        using var document = JsonDocument.Parse("{\"value\":\"1994\"}");
        var parse = RuleValueParser.ReadInteger(document.RootElement.GetProperty("value"), "/value");

        Assert.NotNull(parse.Error);

        var read = ReadRule(Rule("{\"field\":\"productionYear\",\"operator\":\"equals\",\"value\":\"1994\"}"));

        Assert.EndsWith(parse.Error!.Message, Assert.Single(read.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConditionWritingNoValueForAnOperatorThatTakesOneIsRefused()
    {
        var read = ReadRule(Rule("{\"field\":\"genres\",\"operator\":\"contains\"}"));

        Assert.False(read.IsAccepted);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0", error.Pointer);
        Assert.Contains("\"contains\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("one value", error.Message, StringComparison.Ordinal);
        Assert.Contains("\"value\"", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same refusal for an operator written with a list, and it says which shape is missing
    /// rather than the shape thirteen of the seventeen operators take. Whoever left the member out
    /// of an <c>in</c> condition is told to write an array and not a value.
    /// </summary>
    [Fact]
    public void AListOperatorWritingNoValueIsRefusedNamingTheShapeItTakes()
    {
        var read = ReadRule(Rule("{\"field\":\"officialRating\",\"operator\":\"in\"}"));

        Assert.False(read.IsAccepted);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0", error.Pointer);
        Assert.Contains("\"in\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("a list of values", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("one value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOperatorThatTakesNoValueIsReadWithNoneAndAcceptsTheCondition()
    {
        var read = ReadRule(Rule("{\"field\":\"overview\",\"operator\":\"isEmpty\"}"));

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));

        var entry = Assert.Single(read.Conditions);

        Assert.Equal(RuleOperator.IsEmpty, entry.Operator.Operator);
        Assert.Empty(entry.Values);
    }

    /// <summary>
    /// A value beside an operator that takes none is refused rather than dropped. Dropping it
    /// would collect a set the document does not describe and report nothing about it.
    /// </summary>
    [Fact]
    public void AValueBesideAnOperatorThatTakesNoneIsRefusedRatherThanIgnored()
    {
        var read = ReadRule(Rule("{\"field\":\"overview\",\"operator\":\"isNotEmpty\",\"value\":\"Thriller\"}"));

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Conditions);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/value", error.Pointer);
        Assert.Contains("\"isNotEmpty\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("\"overview\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AListOperatorReadsEveryMemberInTheOrderTheDocumentWroteThem()
    {
        var read = ReadRule(Rule(
            "{\"field\":\"officialRating\",\"operator\":\"in\",\"value\":[\"PG\",\"PG-13\",\"R\"]}"));

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));

        var entry = Assert.Single(read.Conditions);

        Assert.Equal(
            ["PG", "PG-13", "R"],
            entry.Values.Select(value => value.Value).ToArray());
        Assert.All(entry.Values, value => Assert.Equal(RuleValueType.String, value.Type));
    }

    /// <summary>
    /// Every bad member of a list is named on one read. A stage stopping at the first would make
    /// repairing a list of twenty values with three bad ones three edits and three re-reads.
    /// </summary>
    [Fact]
    public void EveryMemberOfAListThatDoesNotParseIsNamedWithItsOwnIndex()
    {
        var read = ReadRule(Rule(
            "{\"field\":\"productionYear\",\"operator\":\"in\",\"value\":[1994,\"1995\",1996,\"1997\"]}"));

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Conditions);
        Assert.Equal(
            ["/match/allOf/0/value/1", "/match/allOf/0/value/3"],
            read.Errors.Select(error => error.Pointer).ToArray());
        Assert.All(
            read.Errors,
            error => Assert.Contains("\"productionYear\"", error.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyListIsRefusedRatherThanReadAsMatchingNothing()
    {
        var read = ReadRule(Rule("{\"field\":\"officialRating\",\"operator\":\"in\",\"value\":[]}"));

        Assert.False(read.IsAccepted);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/value", error.Pointer);
        Assert.Contains("\"officialRating\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AListOperatorWrittenWithOneValueIsRefusedForBeingTheWrongShape()
    {
        var read = ReadRule(Rule("{\"field\":\"officialRating\",\"operator\":\"notIn\",\"value\":\"PG\"}"));

        Assert.False(read.IsAccepted);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/value", error.Pointer);
        Assert.Contains("\"notIn\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("JSON array", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other direction, and it is refused by its own sentence rather than by the parser's. The
    /// parser would say the array is not a JSON string, which is true and sends whoever wrote a
    /// list to check the quoting instead of the operator.
    /// </summary>
    [Fact]
    public void AListWrittenBesideAnOperatorThatTakesOneValueIsRefusedNamingTheOnesThatDo()
    {
        var read = ReadRule(Rule("{\"field\":\"genres\",\"operator\":\"contains\",\"value\":[\"Thriller\"]}"));

        Assert.False(read.IsAccepted);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/value", error.Pointer);
        Assert.Contains("\"contains\"", error.Message, StringComparison.Ordinal);
        Assert.Contains("in and notIn", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON string", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every reason on one read rather than the first, which is the shape the three stages before
    /// this one hold and the reason a rule is repaired in one pass.
    /// </summary>
    [Fact]
    public void EveryConditionThatIsRefusedIsReportedOnOneRead()
    {
        var read = ReadRule(Rule(
            "{\"field\":\"genres\",\"operator\":\"contains\",\"value\":\"Thriller\"},"
            + "{\"field\":\"productionYear\",\"operator\":\"equals\",\"value\":\"1994\"},"
            + "{\"field\":\"communityRating\",\"operator\":\"greaterThan\",\"value\":true}"));

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Conditions);
        Assert.Equal(
            ["/match/allOf/1/value", "/match/allOf/2/value"],
            read.Errors.Select(error => error.Pointer).ToArray());
    }

    /// <summary>
    /// A condition nested inside a group is read the same way as one at the top, because the stage
    /// walks what the operator stage produced and that is already flat.
    /// </summary>
    [Fact]
    public void AConditionInsideANestedGroupIsReadWithItsOwnPointer()
    {
        var read = ReadRule(
            "{\"schemaVersion\":1,\"id\":\"a\",\"name\":\"A\",\"match\":{\"allOf\":[{\"anyOf\":["
            + "{\"field\":\"tags\",\"operator\":\"contains\",\"value\":\"favourite\"}]}]}}");

        Assert.True(read.IsAccepted, string.Join("; ", read.Errors.Select(error => error.ToString())));
        Assert.Equal("/match/allOf/0/anyOf/0", Assert.Single(read.Conditions).Pointer);
    }

    [Fact]
    public void AReadRefusesANullOperatorList()
    {
        using var document = JsonDocument.Parse("{}");
        var root = document.RootElement;

        Assert.Throws<ArgumentNullException>(() => RuleValueReader.Read(root, null!));
    }

    /// <summary>
    /// A pointer that resolves in one document and not in another means the caller handed this
    /// stage two different reads, which is a fault in the caller rather than in the rule, so it
    /// throws instead of producing a refusal an operator cannot act on.
    /// </summary>
    [Fact]
    public void AnOperatorReadFromAnotherDocumentThrowsRatherThanRefusingTheRule()
    {
        using var read = JsonDocument.Parse(
            Rule("{\"field\":\"genres\",\"operator\":\"contains\",\"value\":\"Thriller\"}"));
        using var other = JsonDocument.Parse("{\"schemaVersion\":1,\"id\":\"a\",\"name\":\"A\"}");

        var root = read.RootElement;
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");
        var fields = RuleFieldReader.Read(root, composition.Group!);
        var operators = RuleOperatorReader.Read(root, fields.Fields);

        var thrown = Assert.Throws<ArgumentException>(
            () => RuleValueReader.Read(other.RootElement, operators.Operators));

        Assert.Contains("/match/allOf/0", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The member this stage reads, named once so a document and the stage cannot drift apart on
    /// a string literal repeated in two files.
    /// </summary>
    [Fact]
    public void TheMemberThisStageReadsIsTheOneTheDocumentsWrite()
    {
        Assert.Equal("value", RuleValueReader.ValueMember);
    }
}
