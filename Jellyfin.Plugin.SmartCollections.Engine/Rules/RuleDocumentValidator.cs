using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Reads a rule document and returns either the document or every reason it was refused.
/// </summary>
/// <remarks>
/// Validation happens before evaluation, never during. This type is the only way a document
/// becomes a <see cref="RuleDocument"/>, so there is no path on which an unread document reaches
/// anything that acts on it.
///
/// What it checks today is the envelope: that the text is JSON, that its top level is an object,
/// and that it declares a schema version this plugin reads. The rule inside the envelope is
/// checked by stages that arrive with the vocabulary they check against, each adding its errors
/// to the same list with its own pointer. That order matters: a document whose version this
/// plugin cannot read is refused before anything tries to interpret its contents, because reading
/// as far as it parses would apply a rule that means something else.
/// </remarks>
public static class RuleDocumentValidator
{
    /// <summary>
    /// The lowest schema version that has ever existed.
    /// </summary>
    public const int LowestSchemaVersion = 1;

    /// <summary>
    /// The highest schema version this plugin reads.
    /// </summary>
    /// <remarks>
    /// Raising this is what declares that the shape changed. The migration chain that carries an
    /// older document forward to it is planned separately, and until it exists this constant and
    /// <see cref="LowestSchemaVersion"/> are deliberately the same number.
    /// </remarks>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The member every rule document carries at its top level.
    /// </summary>
    public const string SchemaVersionMember = "schemaVersion";

    private const string SchemaVersionPointer = "/" + SchemaVersionMember;

    /// <summary>
    /// The three bytes an editor writes at the start of a file to say it is UTF-8.
    /// </summary>
    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// The decoder a file goes through, which refuses what it cannot read rather than replacing it.
    /// </summary>
    /// <remarks>
    /// The default decoder substitutes U+FFFD for a byte sequence that is not UTF-8, which turns a
    /// truncated or corrupt file into a document that parses and means something the operator did
    /// not write. Refusing is the only answer that leaves the operator able to act.
    ///
    /// The constructor argument decides whether this encoding EMITS a mark and nothing else. It
    /// does not consume one while decoding, so a file written with a mark decodes to a string
    /// beginning U+FEFF and the parser refuses it as not JSON, reporting the operator's document
    /// for something their editor wrote. The mark is removed below rather than here, and this
    /// paragraph exists because the argument reads as though it were the removal.
    /// </remarks>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Reads a rule document from the bytes a file holds.
    /// </summary>
    /// <remarks>
    /// This is the entry point for anything that reads a file, because the bytes are where the
    /// interesting refusals live: a byte order mark, a lone surrogate, an unexpected byte and a
    /// multi-byte sequence cut short by a truncated write. A caller that decoded first and handed
    /// the text to the overload below would have answered all four of those questions on its own,
    /// each caller differently.
    /// </remarks>
    /// <param name="content">The file's bytes, exactly as they were read.</param>
    /// <returns>The document, or every reason it was refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    public static RuleDocumentValidation Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Consumed rather than trimmed out of the decoded text: the mark says which encoding the
        // file is in and is never a character of the document, so removing it here is what lets
        // everything downstream read a document that cannot begin with one. Asked for rather than
        // assumed, because skipping three bytes unconditionally eats the first three bytes of
        // every document written without a mark.
        var start = content.AsSpan().StartsWith(ByteOrderMark) ? ByteOrderMark.Length : 0;

        string text;

        try
        {
            text = Utf8.GetString(content, start, content.Length - start);
        }
        catch (DecoderFallbackException exception)
        {
            var offset = start + exception.Index;

            return Refuse(
                RuleValidationError.WholeDocument,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file is not UTF-8 text. The byte at offset {offset}, 0x{content[offset]:X2}, begins a sequence that does not decode."));
        }

        return Read(text);
    }

    /// <summary>
    /// Reads a rule document.
    /// </summary>
    /// <param name="text">The document exactly as it was read from wherever it came from.</param>
    /// <returns>The document, or every reason it was refused.</returns>
    public static RuleDocumentValidation Read(string text)
    {
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                // A rule document is written by a person or by a form, and neither has a reason
                // to carry a comment or a trailing comma. Accepting either would make the file
                // this plugin reads a different format from the one every other JSON tool reads.
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
        }
        catch (JsonException exception)
        {
            return Refuse(
                RuleValidationError.WholeDocument,
                "The document is not JSON: " + exception.Message);
        }

        using (parsed)
        {
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Refuse(
                    RuleValidationError.WholeDocument,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"A rule document is a JSON object at its top level, and this document's top level is {Describe(root.ValueKind)}."));
            }

            if (!root.TryGetProperty(SchemaVersionMember, out var declared))
            {
                return Refuse(
                    SchemaVersionPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The document declares no {SchemaVersionMember}. Every rule document carries one as an integer at its top level, and this plugin reads {LowestSchemaVersion} to {CurrentSchemaVersion}. A document without one is refused rather than guessed at, because a guess reads it as the shape this version happens to expect."));
            }

            if (declared.ValueKind != JsonValueKind.Number || !declared.TryGetInt32(out var version))
            {
                return Refuse(
                    SchemaVersionPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{SchemaVersionMember} is {Describe(declared.ValueKind)} and has to be an integer."));
            }

            if (version < LowestSchemaVersion)
            {
                return Refuse(
                    SchemaVersionPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The document declares {SchemaVersionMember} {version}, and the lowest version there has ever been is {LowestSchemaVersion}."));
            }

            if (version > CurrentSchemaVersion)
            {
                return Refuse(
                    SchemaVersionPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The document declares {SchemaVersionMember} {version} and this plugin reads up to {SchemaVersionMember} {CurrentSchemaVersion}. It is refused rather than read as far as it parses, because a newer version may have changed what an existing member means."));
            }

            return RuleDocumentValidation.Accepted(new RuleDocument(version, text));
        }
    }

    private static RuleDocumentValidation Refuse(string pointer, string message)
        => RuleDocumentValidation.Refused(
            new List<RuleValidationError> { new(pointer, message) });

    // The kind as an operator would say it, so a message reads as a sentence rather than as an
    // enumeration member. Every JsonValueKind is named: a message saying "is Undefined" for a
    // kind nobody listed is the message somebody reports as a bug in the plugin.
    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number that is not a 32-bit integer",
        JsonValueKind.True or JsonValueKind.False => "a true or false value",
        JsonValueKind.Null => "null",
        _ => "absent"
    };
}
