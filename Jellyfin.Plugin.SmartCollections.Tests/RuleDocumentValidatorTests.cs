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
    private const string Minimal = "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Christmas\"}";

    [Fact]
    public void AValidDocumentIsAcceptedAndKeptExactlyAsItWasRead()
    {
        // Deliberately not the shape a serialiser would emit: extra spacing, a trailing newline
        // and a member no version declares. Keeping the text as read is what lets a member this
        // version does not understand survive a round trip instead of being dropped.
        const string Text = "{\n    \"schemaVersion\" :  1,\n    \"id\": \"christmas\",\n    \"name\": \"Christmas\",\n    \"somethingLaterVersionsMayAdd\": []\n}\n";

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
    [InlineData("{\"schemaVersion\": 1}")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"\"}")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": 7}")]
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
            "{\"schemaVersion\": " + RuleDocumentValidator.LowestSchemaVersion + ", \"id\": \"christmas\", \"name\": \"Christmas\"}");

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

    /// <summary>
    /// The member that says which rule this is. A document without one is refused rather than
    /// given an id worked out from its name or its file, because an identity derived from either
    /// of those changes when that one is edited, which is the one thing an identity exists not to
    /// do.
    /// </summary>
    [Fact]
    public void ADocumentWithNoIdIsRefusedAtTheMember()
    {
        var result = RuleDocumentValidator.Read("{\"schemaVersion\": 1}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/id", error.Pointer);
        Assert.Contains(RuleDocumentValidator.IdMember, error.Message, StringComparison.Ordinal);

        // The distinctive half of the sentence. Without it this assertion also passes on the
        // refusal for an id that is not a string, which an absent member reaches as
        // JsonValueKind.Undefined, and the guard for a missing member would be provable by
        // deleting it.
        Assert.Contains("declares no id", error.Message, StringComparison.Ordinal);
        Assert.Null(result.Document);
    }

    [Theory]
    [InlineData("{\"schemaVersion\": 1, \"id\": 7}", "a number")]
    [InlineData("{\"schemaVersion\": 1, \"id\": true}", "a true or false value")]
    [InlineData("{\"schemaVersion\": 1, \"id\": null}", "null")]
    [InlineData("{\"schemaVersion\": 1, \"id\": []}", "an array")]
    [InlineData("{\"schemaVersion\": 1, \"id\": {}}", "an object")]
    public void AnIdThatIsNotAStringIsRefusedAndTheMessageNamesWhatItIs(string text, string named)
    {
        var result = RuleDocumentValidator.Read(text);

        var error = Assert.Single(result.Errors);
        Assert.Equal("/id", error.Pointer);
        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Contains("has to be a string", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyIdIsRefused()
    {
        var result = RuleDocumentValidator.Read("{\"schemaVersion\": 1, \"id\": \"\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/id", error.Pointer);
        Assert.Contains("empty", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The set is narrow on purpose, and each case here is one of the reasons it is. An uppercase
    /// letter would leave case folding deciding an identity, and the rule for folding one is not
    /// the same in every language. A letter outside ASCII would leave a normalisation form
    /// deciding it, since the two encodings of one accented letter render identically and compare
    /// as different. A space and a combining mark are refused by the same clause rather than by
    /// three of their own, which is why they are here and not in tests of their own.
    /// </summary>
    [Theory]
    [InlineData("Christmas", 'C')]
    [InlineData("christmas films", ' ')]
    [InlineData("christmas_films", '_')]
    [InlineData("christmas.films", '.')]
    [InlineData("weihnachtsfilme-f\u00fcr-alle", '\u00fc')]
    [InlineData("christmas\u0301", '\u0301')]
    public void AnIdHoldingACharacterOutsideTheSetIsRefusedAndTheMessageSaysWhereItIs(string id, char offending)
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"" + id + "\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/id", error.Pointer);
        Assert.Contains(
            "position " + id.IndexOf(offending).ToString(CultureInfo.InvariantCulture),
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains("lowercase letters a to z", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A character a message cannot show is named by its code point instead. Printing a tab raw
    /// leaves a refusal with a gap where the reason should be, and the operator is told a position
    /// and nothing else.
    /// </summary>
    [Fact]
    public void AnIdHoldingATabIsNamedByItsCodePointRatherThanPrintedRaw()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christ\\tmas\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/id", error.Pointer);
        Assert.Contains("U+0009", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\t", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Inside the set, including the id the front page publishes in its own example. A test that
    /// only watched the refusals would pass with a validator that refused every id there is.
    /// </summary>
    [Theory]
    [InlineData("nineties-thrillers")]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("-")]
    [InlineData("christmas-films-2026")]
    public void AnIdInsideTheSetIsAccepted(string id)
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"" + id + "\", \"name\": \"Christmas\"}");

        Assert.True(result.IsValid, Because(result));
        Assert.Equal(id, result.Document!.Id, StringComparer.Ordinal);
    }

    [Fact]
    public void AnIdLongerThanTheMaximumIsRefusedWithBothNumbersInTheMessage()
    {
        var tooLong = new string('a', RuleDocumentValidator.MaximumIdLength + 1);

        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"" + tooLong + "\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/id", error.Pointer);
        Assert.Contains(
            (RuleDocumentValidator.MaximumIdLength + 1).ToString(CultureInfo.InvariantCulture),
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            RuleDocumentValidator.MaximumIdLength.ToString(CultureInfo.InvariantCulture),
            error.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The one-character near miss for the bound above, which is what separates a rule that
    /// refuses too much from one that refuses what it says it does.
    /// </summary>
    [Fact]
    public void AnIdOfExactlyTheMaximumIsAccepted()
    {
        var atTheBound = new string('a', RuleDocumentValidator.MaximumIdLength);

        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"" + atTheBound + "\", \"name\": \"Christmas\"}");

        Assert.True(result.IsValid, Because(result));
    }

    /// <summary>
    /// Both members are wrong and the id is what is reported. A document that cannot be identified
    /// is one every later message can name only by the file it came from, which is the coupling
    /// the id exists to break, so it is what the envelope asks for first after the version.
    /// </summary>
    [Fact]
    public void AnIdIsJudgedBeforeAName()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": 7, \"name\": 7}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/id", error.Pointer);
    }

    /// <summary>
    /// Judged after the version, for the reason the version is judged first: what an id may hold
    /// is a property of a format version, so an id read before the version is known would be
    /// judged against the wrong format.
    /// </summary>
    [Fact]
    public void AVersionThisPluginCannotReadIsReportedBeforeAnIdIsJudged()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 99, \"id\": \"NOT IN THE SET\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/schemaVersion", error.Pointer);
    }

    /// <summary>
    /// The id is the one member this record carries, because it is the one thing a caller asks
    /// about a document without reading it. Everything else stays in the text.
    /// </summary>
    [Fact]
    public void AnAcceptedDocumentCarriesTheIdItDeclared()
    {
        var result = RuleDocumentValidator.Read(Minimal);

        Assert.True(result.IsValid, Because(result));
        Assert.Equal("christmas", result.Document!.Id, StringComparer.Ordinal);
    }

    /// <summary>
    /// The member that decides what an operator sees. A document without one is refused rather
    /// than named after the file it was read from: the file name is the store's business, and
    /// borrowing it would mean renaming a collection by renaming a file.
    /// </summary>
    [Fact]
    public void ADocumentWithNoNameIsRefusedAtTheMember()
    {
        var result = RuleDocumentValidator.Read("{\"schemaVersion\": 1, \"id\": \"christmas\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/name", error.Pointer);
        Assert.Contains(RuleDocumentValidator.NameMember, error.Message, StringComparison.Ordinal);

        // The distinctive half of the sentence. Without it this assertion also passes on the
        // refusal for a name that is not a string, which an absent member reaches as
        // JsonValueKind.Undefined, and the guard for a missing member would be provable by
        // deleting it.
        Assert.Contains("declares no name", error.Message, StringComparison.Ordinal);
        Assert.Null(result.Document);
    }

    /// <summary>
    /// The kind is named in the message, and a number is named as a number. The version member's
    /// own refusal calls a number "a number that is not a 32-bit integer", which is the right
    /// sentence there and reports a name as a bad integer if it is borrowed.
    /// </summary>
    [Theory]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": 7}", "a number")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": true}", "a true or false value")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": null}", "null")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": []}", "an array")]
    [InlineData("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": {}}", "an object")]
    public void ANameThatIsNotAStringIsRefusedAndTheMessageNamesWhatItIs(string text, string named)
    {
        var result = RuleDocumentValidator.Read(text);

        var error = Assert.Single(result.Errors);
        Assert.Equal("/name", error.Pointer);
        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Contains("has to be a string", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyNameIsRefused()
    {
        var result = RuleDocumentValidator.Read("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/name", error.Pointer);
        Assert.Contains("empty", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Refused rather than trimmed. Trimming would make the plugin rewrite what the operator
    /// wrote, which is the thing the whole document format is built not to do, and it would let
    /// two documents whose names differ only at an invisible edge become one name silently.
    /// </summary>
    [Theory]
    [InlineData(" Christmas")]
    [InlineData("Christmas ")]
    [InlineData("\\tChristmas")]
    [InlineData("Christmas\\n")]
    [InlineData(" ")]
    public void ANameWithWhitespaceAtEitherEndIsRefusedRatherThanTrimmed(string name)
    {
        var result = RuleDocumentValidator.Read("{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"" + name + "\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/name", error.Pointer);
        Assert.Contains("whitespace", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The near miss for the rule above. A space between words is what a collection called
    /// <c>Nineties Thrillers</c> is made of, and a rule refusing edge whitespace by refusing
    /// whitespace would refuse most names anybody writes.
    /// </summary>
    [Fact]
    public void WhitespaceInsideANameIsWhatANameIsMadeOf()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Nineties Thrillers\"}");

        Assert.True(result.IsValid, Because(result));
    }

    /// <summary>
    /// A control character reaches a document escaped, because a raw one never gets past the
    /// parser. It renders as nothing where the name is shown, so the collection somebody sees
    /// would be a different string from the one in the document, and the position is in the
    /// message because that is the only way to find a character that displays as nothing.
    /// </summary>
    [Fact]
    public void ANameHoldingAControlCharacterIsRefusedAndTheMessageSaysWhereItIs()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Chri\\u0007stmas\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/name", error.Pointer);
        Assert.Contains("control character", error.Message, StringComparison.Ordinal);
        Assert.Contains("position 4", error.Message, StringComparison.Ordinal);
        Assert.Contains("U+0007", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tab at an edge is caught by the whitespace rule and a tab in the middle by the control
    /// rule, so neither reaches a library. The two are asserted together because a reader of
    /// either rule alone would think the other case falls through.
    /// </summary>
    [Fact]
    public void ATabInTheMiddleIsRefusedAsAControlCharacter()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Chri\\tstmas\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/name", error.Pointer);
        Assert.Contains("control character", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameLongerThanTheMaximumIsRefusedWithBothNumbersInTheMessage()
    {
        var tooLong = new string('a', RuleDocumentValidator.MaximumNameLength + 1);

        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"" + tooLong + "\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/name", error.Pointer);
        Assert.Contains(
            (RuleDocumentValidator.MaximumNameLength + 1).ToString(CultureInfo.InvariantCulture),
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            RuleDocumentValidator.MaximumNameLength.ToString(CultureInfo.InvariantCulture),
            error.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The one-character near miss for the bound above, which is what separates a rule that
    /// refuses too much from one that refuses what it says it does.
    /// </summary>
    [Fact]
    public void ANameOfExactlyTheMaximumIsAccepted()
    {
        var atTheBound = new string('a', RuleDocumentValidator.MaximumNameLength);

        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"" + atTheBound + "\"}");

        Assert.True(result.IsValid, Because(result));
    }

    /// <summary>
    /// Two rules may carry one name, deliberately. The identity of a rule is its id, and the
    /// collection it owns is found by the stamp rather than by the title, so refusing a duplicate
    /// would make this member a second identity, which is the coupling the id exists to break.
    /// A document is judged on its own here, and this asserts that nothing about it depends on
    /// what another document happens to be called.
    /// </summary>
    [Fact]
    public void TwoDocumentsMayCarryTheSameNameAndBothAreAccepted()
    {
        var first = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas-films\", \"name\": \"Christmas\"}");
        var second = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas-series\", \"name\": \"Christmas\"}");

        Assert.True(first.IsValid, Because(first));
        Assert.True(second.IsValid, Because(second));
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
