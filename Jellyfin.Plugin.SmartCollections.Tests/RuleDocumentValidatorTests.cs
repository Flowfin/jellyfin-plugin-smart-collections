using System;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A rule document is untrusted text: it is written by hand into a directory on a server, or
/// posted through a form, and either way it reaches the plugin before anything has looked at it.
/// These tests hold the envelope, which is the part that decides whether the rest of the document
/// may be interpreted at all.
/// </summary>
public class RuleDocumentValidatorTests
{
    private const string Minimal = "{\"schemaVersion\": 1}";

    [Fact]
    public void AValidDocumentIsAcceptedAndKeptExactlyAsItWasRead()
    {
        // Deliberately not the shape a serialiser would emit: extra spacing, a trailing newline
        // and a member no version declares. Keeping the text as read is what lets a member this
        // version does not understand survive a round trip instead of being dropped.
        const string Text = "{\n    \"schemaVersion\" :  1,\n    \"somethingLaterVersionsMayAdd\": []\n}\n";

        var result = RuleDocumentValidator.Read(Text);

        Assert.True(result.IsValid, Because(result));
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Document!.SchemaVersion);
        Assert.Equal(Text, result.Document.Text, StringComparer.Ordinal);
    }

    [Fact]
    public void TextThatIsNotJsonIsRefusedAgainstTheWholeDocument()
    {
        var result = RuleDocumentValidator.Read("not json at all");

        var error = Assert.Single(result.Errors);
        Assert.Equal(RuleValidationError.WholeDocument, error.Pointer);
        Assert.Contains("not JSON", error.Message, StringComparison.Ordinal);
        Assert.Null(result.Document);
    }

    [Theory]
    [InlineData("[]", "an array")]
    [InlineData("\"a string\"", "a string")]
    [InlineData("7", "a number")]
    [InlineData("null", "null")]
    public void ATopLevelThatIsNotAnObjectIsRefusedAndTheMessageNamesWhatItIs(string text, string named)
    {
        var result = RuleDocumentValidator.Read(text);

        var error = Assert.Single(result.Errors);
        Assert.Equal(RuleValidationError.WholeDocument, error.Pointer);
        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentWithNoSchemaVersionIsRefusedRatherThanGuessedAt()
    {
        var result = RuleDocumentValidator.Read("{\"name\": \"Christmas\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/schemaVersion", error.Pointer);
        Assert.Contains("schemaVersion", error.Message, StringComparison.Ordinal);
        Assert.Null(result.Document);
    }

    [Theory]
    [InlineData("{\"schemaVersion\": \"1\"}")]
    [InlineData("{\"schemaVersion\": 1.5}")]
    [InlineData("{\"schemaVersion\": true}")]
    [InlineData("{\"schemaVersion\": null}")]
    [InlineData("{\"schemaVersion\": []}")]
    [InlineData("{\"schemaVersion\": {}}")]
    [InlineData("{\"schemaVersion\": false}")]
    public void ASchemaVersionThatIsNotAnIntegerIsRefusedAtTheMember(string text)
    {
        var result = RuleDocumentValidator.Read(text);

        var error = Assert.Single(result.Errors);
        Assert.Equal("/schemaVersion", error.Pointer);
        Assert.Contains("integer", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ASchemaVersionBelowTheLowestThereHasEverBeenIsRefused(int version)
    {
        var result = RuleDocumentValidator.Read("{\"schemaVersion\": " + version + "}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/schemaVersion", error.Pointer);
        Assert.Contains(
            RuleDocumentValidator.LowestSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            error.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A document from a future version is refused with BOTH numbers named. One number tells the
    /// operator nothing they can act on: the message has to say what the document declares and
    /// what this plugin reads, or the only way to learn the second is to read the source.
    /// </summary>
    [Fact]
    public void ADocumentFromAFutureVersionIsRefusedWithBothNumbersInTheMessage()
    {
        var future = RuleDocumentValidator.CurrentSchemaVersion + 1;

        var result = RuleDocumentValidator.Read("{\"schemaVersion\": " + future + "}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/schemaVersion", error.Pointer);
        Assert.Contains(
            future.ToString(System.Globalization.CultureInfo.InvariantCulture),
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            RuleDocumentValidator.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            error.Message,
            StringComparison.Ordinal);
        Assert.Null(result.Document);
    }

    /// <summary>
    /// A comment and a trailing comma are both things a hand-edited file picks up, and both are
    /// refused rather than tolerated: accepting them would make the file this plugin reads a
    /// different format from the one every other JSON tool reads, including the browser the
    /// configuration page runs in.
    /// </summary>
    [Theory]
    [InlineData("{\n  // why this rule exists\n  \"schemaVersion\": 1\n}")]
    [InlineData("{\"schemaVersion\": 1,}")]
    public void JsonThisFormatDoesNotAcceptIsRefused(string text)
    {
        var result = RuleDocumentValidator.Read(text);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Every refusal carries a pointer into the document, and the pointer is a JSON Pointer:
    /// empty for the document as a whole, otherwise a path beginning with a slash. That is what
    /// lets one error serve both a log line and a form deciding which control to mark, and it is
    /// asserted over every invalid document this file knows about rather than one of them.
    /// </summary>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("\"a string\"")]
    [InlineData("{\"name\": \"Christmas\"}")]
    [InlineData("{\"schemaVersion\": \"1\"}")]
    [InlineData("{\"schemaVersion\": 0}")]
    [InlineData("{\"schemaVersion\": 99}")]
    [InlineData("{\"schemaVersion\": 1,}")]
    public void EveryRefusalCarriesAPointerAndAMessage(string text)
    {
        var result = RuleDocumentValidator.Read(text);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        foreach (var error in result.Errors)
        {
            Assert.True(
                error.Pointer.Length == 0 || error.Pointer.StartsWith('/'),
                $"'{error.Pointer}' is not a JSON Pointer.");
            Assert.False(string.IsNullOrWhiteSpace(error.Message), "The error carries no message.");
        }
    }

    [Fact]
    public void TheLowestVersionIsAccepted()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": " + RuleDocumentValidator.LowestSchemaVersion + "}");

        Assert.True(result.IsValid, Because(result));
    }

    [Fact]
    public void AnAcceptedDocumentCarriesNoErrors()
    {
        var result = RuleDocumentValidator.Read(Minimal);

        Assert.True(result.IsValid, Because(result));
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// The mark an editor writes at the start of a UTF-8 file is an encoding detail of the file
    /// and never a character of the document. Left in, it reaches the parser as U+FEFF and the
    /// operator is told their document is not JSON because of something their editor wrote.
    /// </summary>
    [Fact]
    public void AByteOrderMarkIsConsumedRatherThanReadAsPartOfTheDocument()
    {
        byte[] withMark = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Minimal)];

        var result = RuleDocumentValidator.Read(withMark);

        Assert.True(result.IsValid, Because(result));
        Assert.Equal(Minimal, result.Document!.Text, StringComparer.Ordinal);
    }

    /// <summary>
    /// The other half of the same rule. Skipping three bytes whether or not a mark is there would
    /// eat the first three characters of every document written without one, which is most of
    /// them.
    /// </summary>
    [Fact]
    public void ADocumentWithoutAMarkKeepsItsFirstThreeBytes()
    {
        var result = RuleDocumentValidator.Read(Encoding.UTF8.GetBytes(Minimal));

        Assert.True(result.IsValid, Because(result));
        Assert.Equal(Minimal, result.Document!.Text, StringComparer.Ordinal);
    }

    /// <summary>
    /// A file that is only a mark carries no document. It is refused for being empty rather than
    /// for its encoding, which is the message that tells the operator what to do about it.
    /// </summary>
    [Fact]
    public void AFileHoldingNothingButTheMarkIsRefused()
    {
        var result = RuleDocumentValidator.Read([0xEF, 0xBB, 0xBF]);

        Assert.False(result.IsValid);
        Assert.Contains("not JSON", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A surrogate encoded as though it were a character. UTF-8 has no encoding for one, and a
    /// decoder that replaced it would hand the parser a document holding U+FFFD where the
    /// operator wrote something else.
    /// </summary>
    [Fact]
    public void ALoneSurrogateIsRefusedAndTheRefusalNamesWhereItStarts()
    {
        // {"a":" ED A0 80 "}
        byte[] content = [0x7B, 0x22, 0x61, 0x22, 0x3A, 0x22, 0xED, 0xA0, 0x80, 0x22, 0x7D];

        AssertRefusedAsNotUtf8(content, offset: 6, first: "0xED");
    }

    /// <summary>
    /// A byte that begins no sequence at all, which is what a file in another encoding looks like
    /// from here.
    /// </summary>
    [Fact]
    public void AByteThatBeginsNoSequenceIsRefusedAndTheRefusalNamesWhereItIs()
    {
        byte[] content = [0x7B, 0xFF, 0x7D];

        AssertRefusedAsNotUtf8(content, offset: 1, first: "0xFF");
    }

    /// <summary>
    /// The shape a write cut short leaves behind: a multi-byte sequence whose remaining bytes
    /// never arrived. It is the reason the decode is refused rather than salvaged, because the
    /// bytes that would say what the document meant are gone.
    /// </summary>
    [Fact]
    public void ASequenceCutShortIsRefusedAndTheRefusalNamesWhereItStarts()
    {
        // {"a":" and the first two bytes of a three byte character.
        byte[] content = [0x7B, 0x22, 0x61, 0x22, 0x3A, 0x22, 0xE2, 0x82];

        AssertRefusedAsNotUtf8(content, offset: 6, first: "0xE2");
    }

    /// <summary>
    /// The offset is into the file, not into what was left after the mark was taken off. An
    /// operator opening the file in an editor counts from the first byte, and a refusal counting
    /// from somewhere else sends them to the wrong place.
    /// </summary>
    [Fact]
    public void TheOffsetIsCountedFromTheStartOfTheFileEvenWithAMark()
    {
        byte[] content = [0xEF, 0xBB, 0xBF, 0x7B, 0xFF, 0x7D];

        AssertRefusedAsNotUtf8(content, offset: 4, first: "0xFF");
    }

    /// <summary>
    /// A caller that lost the bytes it meant to read should learn about it here rather than have
    /// an absent file read as a document that refused itself.
    /// </summary>
    [Fact]
    public void ReadingBytesThatAreNotThereIsRefusedRatherThanTreatedAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => RuleDocumentValidator.Read((byte[])null!));
    }

    private static void AssertRefusedAsNotUtf8(byte[] content, int offset, string first)
    {
        var result = RuleDocumentValidator.Read(content);

        Assert.False(result.IsValid);

        var error = Assert.Single(result.Errors);

        Assert.Equal(RuleValidationError.WholeDocument, error.Pointer, StringComparer.Ordinal);
        Assert.Contains("not UTF-8", error.Message, StringComparison.Ordinal);
        Assert.Contains(
            "offset " + offset.ToString(CultureInfo.InvariantCulture),
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains(first, error.Message, StringComparison.Ordinal);
    }

    // xunit prints the assertion, not the reason, so the reason is put where it will be read.
    private static string Because(RuleDocumentValidation result)
        => "Refused with: " + string.Join(" | ", result.Errors);
}
