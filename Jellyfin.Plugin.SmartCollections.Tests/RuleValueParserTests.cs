using System;
using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// One parser per declared value type, and these are the accepted forms and the refusals.
/// </summary>
/// <remarks>
/// Two properties run through the whole file. A value's spelling never decides its type, so every
/// type has a test refusing the value that the neighbouring type would accept. And a refusal
/// names what was written and what the type accepts, so an operator can repair the document from
/// the message alone.
/// </remarks>
public class RuleValueParserTests
{
    private const string Pointer = "/conditions/0/value";

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string RefusalOf(RuleValueParse parse)
    {
        Assert.False(parse.IsAccepted);
        Assert.Null(parse.Value);
        Assert.NotNull(parse.Error);
        Assert.Equal(Pointer, parse.Error!.Pointer);
        return parse.Error.Message;
    }

    private static object AcceptedValue(RuleValueParse parse, RuleValueType type)
    {
        Assert.True(parse.IsAccepted);
        Assert.Null(parse.Error);
        Assert.NotNull(parse.Value);
        Assert.Equal(type, parse.Value!.Type);
        return parse.Value.Value;
    }

    [Fact]
    public void AStringIsTheTextTheDocumentWrote()
    {
        var parse = RuleValueParser.ReadString(Json("\"  Studio Ghibli  \""), Pointer);

        Assert.Equal("  Studio Ghibli  ", AcceptedValue(parse, RuleValueType.String));
    }

    [Fact]
    public void ANumberWhereAStringIsDeclaredIsRefusedWithWhatWasWrittenAndWhatIsAccepted()
    {
        var parse = RuleValueParser.ReadString(Json("12"), Pointer);

        Assert.Equal("The value 12 is not a JSON string.", RefusalOf(parse));
    }

    [Fact]
    public void NullWhereAStringIsDeclaredIsRefused()
    {
        Assert.Equal("The value null is not a JSON string.", RefusalOf(RuleValueParser.ReadString(Json("null"), Pointer)));
    }

    [Fact]
    public void AnIntegerIsReadOutOfTheNumberRatherThanOffAString()
    {
        var parse = RuleValueParser.ReadInteger(Json("1997"), Pointer);

        Assert.Equal(1997L, AcceptedValue(parse, RuleValueType.Integer));
    }

    /// <summary>
    /// The case the type declaration exists for. A document may not opt out of its field's
    /// declared type by writing the value in the other spelling.
    /// </summary>
    [Fact]
    public void ANumberWrittenAsAStringIsRefusedWhereAnIntegerIsDeclared()
    {
        Assert.Equal(
            "The value \"1997\" is not a JSON number with no fractional part, between -9223372036854775808 and 9223372036854775807.",
            RefusalOf(RuleValueParser.ReadInteger(Json("\"1997\""), Pointer)));
    }

    [Fact]
    public void ANumberWithAFractionalPartIsRefusedRatherThanTruncated()
    {
        Assert.StartsWith(
            "The value 1.5 is not a JSON number with no fractional part",
            RefusalOf(RuleValueParser.ReadInteger(Json("1.5"), Pointer)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberPastTheIntegerRangeIsRefused()
    {
        Assert.StartsWith(
            "The value 9223372036854775808 is not a JSON number with no fractional part",
            RefusalOf(RuleValueParser.ReadInteger(Json("9223372036854775808"), Pointer)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADecimalKeepsTheDigitsThatWereWritten()
    {
        var parse = RuleValueParser.ReadDecimal(Json("8.1"), Pointer);

        Assert.Equal(8.1m, AcceptedValue(parse, RuleValueType.Decimal));
    }

    [Fact]
    public void AWholeNumberIsADecimalToo()
    {
        Assert.Equal(8m, AcceptedValue(RuleValueParser.ReadDecimal(Json("8"), Pointer), RuleValueType.Decimal));
    }

    [Fact]
    public void ANumberWrittenAsAStringIsRefusedWhereADecimalIsDeclared()
    {
        Assert.Equal(
            "The value \"8.1\" is not a JSON number between -79228162514264337593543950335 and 79228162514264337593543950335.",
            RefusalOf(RuleValueParser.ReadDecimal(Json("\"8.1\""), Pointer)));
    }

    [Fact]
    public void ANumberPastTheDecimalRangeIsRefused()
    {
        Assert.StartsWith(
            "The value 79228162514264337593543950336 is not a JSON number between",
            RefusalOf(RuleValueParser.ReadDecimal(Json("79228162514264337593543950336"), Pointer)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Pinned rather than chosen. More digits than a decimal holds are rounded by the framework's
    /// own parser rather than refused, and <c>docs/rule-values.md</c> says so; this test is what
    /// stops that sentence going stale without anything noticing.
    /// </summary>
    [Fact]
    public void MoreDigitsThanADecimalHoldsAreRoundedRatherThanRefused()
    {
        var parse = RuleValueParser.ReadDecimal(Json("1.00000000000000000000000000005"), Pointer);

        Assert.Equal(1.0000000000000000000000000001m, AcceptedValue(parse, RuleValueType.Decimal));
    }

    [Fact]
    public void TrueAndFalseAreTheOnlyBooleans()
    {
        Assert.Equal(true, AcceptedValue(RuleValueParser.ReadBoolean(Json("true"), Pointer), RuleValueType.Boolean));
        Assert.Equal(false, AcceptedValue(RuleValueParser.ReadBoolean(Json("false"), Pointer), RuleValueType.Boolean));
    }

    [Fact]
    public void TheStringTrueIsNotABoolean()
    {
        Assert.Equal(
            "The value \"true\" is not the JSON literal true or the JSON literal false.",
            RefusalOf(RuleValueParser.ReadBoolean(Json("\"true\""), Pointer)));
    }

    [Fact]
    public void ADateOnItsOwnIsTheStartOfThatDayAtOffsetZero()
    {
        var parse = RuleValueParser.ReadDate(Json("\"2026-08-30\""), Pointer);

        Assert.Equal(
            new DateTimeOffset(2026, 8, 30, 0, 0, 0, TimeSpan.Zero),
            AcceptedValue(parse, RuleValueType.Date));
    }

    /// <summary>
    /// Two documents writing one instant in two offsets parse to one value, which is what makes
    /// an expected output comparable between servers.
    /// </summary>
    [Fact]
    public void TwoOffsetsNamingOneInstantParseToOneValue()
    {
        var written = RuleValueParser.ReadDate(Json("\"2026-08-30T21:00:00+02:00\""), Pointer);
        var universal = RuleValueParser.ReadDate(Json("\"2026-08-30T19:00:00Z\""), Pointer);

        Assert.Equal(
            new DateTimeOffset(2026, 8, 30, 19, 0, 0, TimeSpan.Zero),
            AcceptedValue(written, RuleValueType.Date));
        Assert.Equal(
            AcceptedValue(written, RuleValueType.Date),
            AcceptedValue(universal, RuleValueType.Date));
    }

    [Fact]
    public void AFractionalSecondIsAccepted()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 8, 30, 19, 0, 0, TimeSpan.Zero).AddTicks(1234567),
            AcceptedValue(RuleValueParser.ReadDate(Json("\"2026-08-30T19:00:00.1234567Z\""), Pointer), RuleValueType.Date));
    }

    [Fact]
    public void AFractionalSecondOnAWrittenOffsetIsAccepted()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 8, 30, 19, 0, 0, TimeSpan.Zero).AddTicks(5000000),
            AcceptedValue(RuleValueParser.ReadDate(Json("\"2026-08-30T21:00:00.5+02:00\""), Pointer), RuleValueType.Date));
    }

    /// <summary>
    /// The refusal the date type exists for. A time with no offset names an instant only once a
    /// zone is supplied, and the only zone available at that point is the server's.
    /// </summary>
    [Fact]
    public void ATimeWithNoOffsetIsRefused()
    {
        Assert.Equal(
            "The value \"2026-08-30T21:00:00\" is not a JSON string holding an ISO 8601 date with an explicit offset, or an ISO 8601 date on its own.",
            RefusalOf(RuleValueParser.ReadDate(Json("\"2026-08-30T21:00:00\""), Pointer)));
    }

    [Fact]
    public void ADateThatIsNotADateIsRefused()
    {
        Assert.StartsWith(
            "The value \"2026-13-01\" is not a JSON string",
            RefusalOf(RuleValueParser.ReadDate(Json("\"2026-13-01\""), Pointer)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberWhereADateIsDeclaredIsRefused()
    {
        Assert.StartsWith(
            "The value 20260830 is not a JSON string",
            RefusalOf(RuleValueParser.ReadDate(Json("20260830"), Pointer)),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"P30D\"", 30 * 24 * 60 * 60)]
    [InlineData("\"P2W\"", 14 * 24 * 60 * 60)]
    [InlineData("\"PT12H30M\"", (12 * 60 * 60) + (30 * 60))]
    [InlineData("\"P1DT2H3M4S\"", (24 * 60 * 60) + (2 * 60 * 60) + (3 * 60) + 4)]
    [InlineData("\"PT0S\"", 0)]
    public void ADurationInFixedUnitsIsAccepted(string written, long seconds)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(seconds),
            AcceptedValue(RuleValueParser.ReadDuration(Json(written), Pointer), RuleValueType.Duration));
    }

    /// <summary>
    /// The one refusal a duration gets its own message for, because the table cannot say why
    /// years and months are absent and it is the refusal an operator meets first.
    /// </summary>
    [Theory]
    [InlineData("\"P1Y\"")]
    [InlineData("\"P1M\"")]
    [InlineData("\"P1Y6M\"")]
    public void YearsAndMonthsAreRefusedByName(string written)
    {
        Assert.Contains(
            "names years or months, and how long either of those is depends on when it is counted from",
            RefusalOf(RuleValueParser.ReadDuration(Json(written), Pointer)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MinutesAfterTheSeparatorAreMinutes()
    {
        Assert.Equal(
            TimeSpan.FromMinutes(1),
            AcceptedValue(RuleValueParser.ReadDuration(Json("\"PT1M\""), Pointer), RuleValueType.Duration));
    }

    [Theory]
    [InlineData("\"P1W2D\"")]        // weeks do not combine
    [InlineData("\"P1DT\"")]         // a separator with nothing after it
    [InlineData("\"PT\"")]           // the same, with no date part either
    [InlineData("\"P\"")]            // no component at all
    [InlineData("\"\"")]             // nothing at all
    [InlineData("\"30D\"")]          // no designator at the front
    [InlineData("\"p30d\"")]         // the lower case spelling
    [InlineData("\"PT0.5S\"")]       // a fraction
    [InlineData("\"P1D1D\"")]        // one designator twice
    [InlineData("\"PT1M1H\"")]       // out of the declared order
    [InlineData("\"P1DT1HT1M\"")]    // two separators
    [InlineData("\"P12\"")]          // digits with no designator
    [InlineData("\"PD\"")]           // a designator with no digits
    [InlineData("\"P1X\"")]          // a designator that is not one
    [InlineData("\"P-1D\"")]         // a sign
    [InlineData("\"P99999999999999999999D\"")] // more digits than the count holds
    [InlineData("\"P100000000000D\"")]         // more ticks than a span holds
    public void ADurationOutsideTheDeclaredFormIsRefused(string written)
    {
        Assert.EndsWith(
            "is not a JSON string holding an ISO 8601 duration written in whole weeks, or in whole days, hours, minutes and seconds.",
            RefusalOf(RuleValueParser.ReadDuration(Json(written), Pointer)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberWhereADurationIsDeclaredIsRefused()
    {
        Assert.StartsWith(
            "The value 30 is not a JSON string holding an ISO 8601 duration",
            RefusalOf(RuleValueParser.ReadDuration(Json("30"), Pointer)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnumerationAcceptsANameTheFieldDeclares()
    {
        var parse = RuleValueParser.ReadEnumeration(Json("\"Series\""), Pointer, ["Movie", "Series"]);

        Assert.Equal("Series", AcceptedValue(parse, RuleValueType.Enumeration));
    }

    [Fact]
    public void AnEnumerationRefusesWhatIsNotDeclaredAndListsWhatIs()
    {
        Assert.Equal(
            "The value \"Episode\" is not one of the names this field declares. They are Movie, Series.",
            RefusalOf(RuleValueParser.ReadEnumeration(Json("\"Episode\""), Pointer, ["Movie", "Series"])));
    }

    /// <summary>
    /// Ordinal, so a name that differs only in case is a different name. A culture-sensitive
    /// comparison here would make a rule match one set of items in one locale and another in the
    /// next, which is the property the whole engine is held to.
    /// </summary>
    [Fact]
    public void AnEnumerationNameThatDiffersOnlyInCaseIsADifferentName()
    {
        Assert.StartsWith(
            "The value \"series\" is not one of the names this field declares",
            RefusalOf(RuleValueParser.ReadEnumeration(Json("\"series\""), Pointer, ["Movie", "Series"])),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberWhereAnEnumerationIsDeclaredIsRefusedWithTheSameList()
    {
        Assert.Equal(
            "The value 1 is not one of the names this field declares. They are Movie, Series.",
            RefusalOf(RuleValueParser.ReadEnumeration(Json("1"), Pointer, ["Movie", "Series"])));
    }

    [Fact]
    public void AnEnumerationDeclaringNoNamesIsAFaultInTheTableRatherThanInTheDocument()
    {
        Assert.Throws<ArgumentNullException>(
            () => RuleValueParser.ReadEnumeration(Json("\"Movie\""), Pointer, null!));

        var empty = Assert.Throws<ArgumentException>(
            () => RuleValueParser.ReadEnumeration(Json("\"Movie\""), Pointer, []));

        Assert.Equal("declared", empty.ParamName);
    }

    /// <summary>
    /// A refusal is read in a log line and in a form field, so a value that did not parse cannot
    /// be quoted back at whatever length the document allows.
    /// </summary>
    [Fact]
    public void AValueTooLongToQuoteIsCutAndSaidToBeCut()
    {
        var written = "\"" + new string('a', 200) + "\"";

        var message = RefusalOf(RuleValueParser.ReadInteger(Json(written), Pointer));

        Assert.StartsWith(
            "The value \"" + new string('a', RuleValueParser.MaximumQuotedLength - 1) + "... is not ",
            message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The cut lands on a code point rather than inside one. Half a surrogate pair renders as a
    /// replacement character, which would put a character in the message that is in neither the
    /// document nor the plugin.
    /// </summary>
    [Fact]
    public void AValueIsCutOnACodePointRatherThanInsideOne()
    {
        var pair = char.ConvertFromUtf32(0x1F600);
        var written = "\"" + new string('a', RuleValueParser.MaximumQuotedLength - 2) + pair + new string('b', 20) + "\"";

        var message = RefusalOf(RuleValueParser.ReadInteger(Json(written), Pointer));

        var quoted = message.Substring("The value ".Length);
        Assert.StartsWith(
            "\"" + new string('a', RuleValueParser.MaximumQuotedLength - 2) + "...",
            quoted,
            StringComparison.Ordinal);
        Assert.DoesNotContain(pair[0].ToString(CultureInfo.InvariantCulture), quoted, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueExactlyAtTheBoundIsQuotedWhole()
    {
        var written = "\"" + new string('a', RuleValueParser.MaximumQuotedLength - 2) + "\"";

        Assert.Equal(
            "The value " + written + " is not a JSON number with no fractional part, between -9223372036854775808 and 9223372036854775807.",
            RefusalOf(RuleValueParser.ReadInteger(Json(written), Pointer)));
    }
}
