using System.Collections.Generic;
using System.Globalization;
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
