using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The stage that reads the field each condition names. The done condition this file carries is
/// the last one: an unknown field in a rule document produces a validation error that names the
/// field and lists the legal ones.
/// </summary>
public class RuleFieldReaderTests
{
    private static RuleFieldRead ReadRule(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");

        Assert.True(composition.IsAccepted, string.Join("; ", composition.Errors.Select(error => error.ToString())));

        return RuleFieldReader.Read(root, composition.Group!, RuleItemKindTable.Rows);
    }

    private static string Rule(string conditions)
        => "{\"schemaVersion\":1,\"id\":\"a\",\"name\":\"A\",\"match\":{\"allOf\":[" + conditions + "]}}";

    [Fact]
    public void EveryConditionNamingADeclaredFieldIsResolvedToItsRow()
    {
        var read = ReadRule(Rule(
            "{\"field\":\"genres\",\"operator\":\"contains\",\"value\":\"Thriller\"},"
            + "{\"field\":\"productionYear\",\"operator\":\"greaterThanOrEqual\",\"value\":1990}"));

        Assert.True(read.IsAccepted);
        Assert.Empty(read.Errors);
        Assert.Equal(
            [RuleField.Genres, RuleField.ProductionYear],
            read.Fields.Select(field => field.Row.Field).ToArray());
        Assert.Equal(
            ["/match/allOf/0", "/match/allOf/1"],
            read.Fields.Select(field => field.Pointer).ToArray());
    }

    /// <summary>
    /// The done condition, watched rather than reasoned about: the message names what was written
    /// and lists every field that exists.
    /// </summary>
    [Fact]
    public void AnUnknownFieldIsRefusedWithItsOwnNameAndEveryLegalOne()
    {
        var read = ReadRule(Rule("{\"field\":\"genre\",\"operator\":\"contains\",\"value\":\"Thriller\"}"));

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Fields);

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/field", error.Pointer);
        Assert.Contains("\"genre\"", error.Message, StringComparison.Ordinal);

        foreach (var name in RuleFieldTable.Names)
        {
            Assert.Contains(name, error.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A name that differs from a declared one only by case is a different name here, so the
    /// server's locale cannot decide whether a document named a field.
    /// </summary>
    [Fact]
    public void AFieldNameSpelledWithTheWrongCaseIsUnknownRatherThanFolded()
    {
        var read = ReadRule(Rule("{\"field\":\"Genres\",\"operator\":\"contains\",\"value\":\"Thriller\"}"));

        Assert.False(read.IsAccepted);
        Assert.Contains("\"Genres\"", Assert.Single(read.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConditionCarryingNoFieldMemberIsRefusedAndTheRefusalListsTheFields()
    {
        var read = ReadRule(Rule("{\"operator\":\"contains\",\"value\":\"Thriller\"}"));

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0", error.Pointer);
        Assert.Contains("names no field", error.Message, StringComparison.Ordinal);
        Assert.Contains("genres", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1990")]
    [InlineData("null")]
    [InlineData("[\"genres\"]")]
    [InlineData("{\"name\":\"genres\"}")]
    public void AFieldThatIsNotAStringIsRefusedRatherThanReadThroughItsText(string written)
    {
        var read = ReadRule(Rule("{\"field\":" + written + ",\"operator\":\"contains\"}"));

        var error = Assert.Single(read.Errors);

        Assert.Equal("/match/allOf/0/field", error.Pointer);
        Assert.Contains("written as a string", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every reason rather than the first, which is what the composition stage before this one
    /// does and what somebody repairing a file by hand needs.
    /// </summary>
    [Fact]
    public void EveryMistypedFieldIsReportedRatherThanTheFirst()
    {
        var read = ReadRule(Rule(
            "{\"field\":\"genre\"},{\"field\":\"tag\"},{\"field\":\"year\"}"));

        Assert.Equal(3, read.Errors.Count);
        Assert.Equal(
            ["/match/allOf/0/field", "/match/allOf/1/field", "/match/allOf/2/field"],
            read.Errors.Select(error => error.Pointer).ToArray());
    }

    /// <summary>
    /// A nested group's conditions are read as well, so a mistyped field does not hide by sitting
    /// one level down.
    /// </summary>
    [Fact]
    public void AConditionInsideANestedGroupIsRead()
    {
        var read = ReadRule(Rule(
            "{\"field\":\"genres\"},{\"anyOf\":[{\"field\":\"tags\"},{\"field\":\"nope\"}]}"));

        Assert.False(read.IsAccepted);
        Assert.Equal("/match/allOf/1/anyOf/1/field", Assert.Single(read.Errors).Pointer);
    }

    /// <summary>
    /// A refused read carries no rows at all rather than the ones it got through before the
    /// mistake, so nothing downstream can act on half a rule.
    /// </summary>
    [Fact]
    public void ARefusedReadCarriesNoRows()
    {
        var read = ReadRule(Rule("{\"field\":\"genres\"},{\"field\":\"nope\"}"));

        Assert.False(read.IsAccepted);
        Assert.Empty(read.Fields);
    }

    [Fact]
    public void ANullTreeIsRefused()
    {
        using var document = JsonDocument.Parse("{}");

        Assert.Throws<ArgumentNullException>(() => RuleFieldReader.Read(document.RootElement, null!, RuleItemKindTable.Rows));
    }

    /// <summary>
    /// A tree read from one document and handed another is a caller mistake rather than a
    /// document mistake, so it throws instead of producing a refusal an operator would be shown.
    /// </summary>
    [Fact]
    public void ATreeFromAnotherDocumentIsRefusedAsACallerMistake()
    {
        using var read = JsonDocument.Parse(Rule("{\"field\":\"genres\"}"));
        using var other = JsonDocument.Parse("{\"schemaVersion\":1}");

        var composition = RuleCompositionReader.Read(read.RootElement.GetProperty("match"), "/match");

        Assert.Throws<ArgumentException>(() => RuleFieldReader.Read(other.RootElement, composition.Group!, RuleItemKindTable.Rows));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("/schemaVersion", true)]
    [InlineData("/match/allOf/0/field", true)]
    [InlineData("/match/allOf/1", false)]
    [InlineData("/nope", false)]
    [InlineData("/schemaVersion/0", false)]
    [InlineData("/match/allOf/first", false)]
    [InlineData("match/allOf/0", false)]
    public void ThePointerResolverFindsWhatIsThereAndNothingElse(string pointer, bool found)
    {
        using var document = JsonDocument.Parse(Rule("{\"field\":\"genres\"}"));

        Assert.Equal(found, RuleFieldReader.Resolve(document.RootElement, pointer) is not null);
    }

    /// <summary>
    /// RFC 6901 decodes the two escapes in this order, so a member literally called <c>~1</c>
    /// resolves to itself rather than to one called <c>/</c>.
    /// </summary>
    [Fact]
    public void ThePointerResolverDecodesTheTwoEscapesInTheOrderTheStandardRequires()
    {
        using var document = JsonDocument.Parse("{\"a/b\":1,\"~1\":2}");

        Assert.Equal(1, RuleFieldReader.Resolve(document.RootElement, "/a~1b")!.Value.GetInt32());
        Assert.Equal(2, RuleFieldReader.Resolve(document.RootElement, "/~01")!.Value.GetInt32());
    }

    [Fact]
    public void ThePointerResolverRefusesANullPointer()
    {
        using var document = JsonDocument.Parse("{}");

        Assert.Throws<ArgumentNullException>(() => RuleFieldReader.Resolve(document.RootElement, null!));
    }
}
