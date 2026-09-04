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
/// What it checks first is the envelope: that the text is JSON, that its top level is an object,
/// that it declares a schema version this plugin reads, that it declares an id for the rule, and
/// that it declares a name for the collection the rule owns. What it checks after that is what the
/// envelope carries, through the stages that arrive with the vocabulary they check against, each
/// reporting with its own pointer into the document. That order matters: a document whose version this
/// plugin cannot read is refused before anything tries to interpret its contents, because reading
/// as far as it parses would apply a rule that means something else. The id and the name are
/// checked after the version for the same reason: what either may hold is a property of a format
/// version, so judging one before the version is known would judge it against the wrong format.
///
/// The id is checked before the name because it is what the rule is, and the name is what its
/// collection is called. Every message about a document from here on names the rule, and a
/// document that cannot be identified is one an administrator surface can only report by the file
/// it came from, which is the coupling the id exists to break.
///
/// A DOCUMENT WHOSE RULE IS WRONG IS REFUSED HERE RATHER THAN LOADED. Until the stages were
/// wired in, this type read the envelope and nothing else, so a document naming a field no table
/// declares, an operator no operator has or a value that will not parse was accepted, listed as a
/// loaded rule, and owned a collection that never changed. That is the failure this plugin's own
/// design is against: a bad document is a visible fault rather than a collection that quietly
/// stopped updating, and a document that reaches an administrator surface as loaded is invisible.
///
/// Whether an id collides with another document's is not a question about one document and is
/// not asked here. One document is judged on its own bytes, so this type answers the same way
/// whatever else is in the directory; the collision is refused by the scan that reads the
/// directory, which is the only place that knows what else was loaded.
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
    /// The member a rule document carries its identity in.
    /// </summary>
    public const string IdMember = "id";

    private const string IdPointer = "/" + IdMember;

    /// <summary>
    /// The most an id may hold, counted in UTF-16 code units.
    /// </summary>
    /// <remarks>
    /// This is a number this plugin chose rather than one it read off the server. The columns a
    /// provider key and its value are stored in declare no length on either supported line:
    ///
    /// <code>
    /// for ref in v10.11.11 v12.0-rc4; do
    ///   gh api "repos/jellyfin/jellyfin/contents/src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/BaseItemProvider.cs?ref=$ref"     ///     --jq .content | base64 -d | grep -cE 'MaxLength'
    /// done
    /// 0
    /// 0
    /// </code>
    ///
    /// So nothing downstream refuses a longer one and the bound exists for what an id has to fit
    /// into here: one refusal message, one stamp on a collection and one line of a log. Unlike a
    /// name it is never rendered to somebody browsing a library, so the bound is smaller: it is
    /// far above any identifier a person types and far below a length that turns a document into
    /// a payload.
    /// </remarks>
    public const int MaximumIdLength = 64;

    /// <summary>
    /// The set an id is made of, in the words the refusal uses.
    /// </summary>
    private const string IdSetInWords = "the lowercase letters a to z, the digits 0 to 9 and the hyphen";

    /// <summary>
    /// The member a rule document carries the collection's name in.
    /// </summary>
    public const string NameMember = "name";

    /// <summary>
    /// The most a name may hold, counted in UTF-16 code units.
    /// </summary>
    /// <remarks>
    /// This is a number this plugin chose rather than one it read off the server. The column a
    /// collection name is stored in declares no length on either supported line, so nothing
    /// downstream refuses a longer one and the bound exists for what a name has to fit into
    /// here: one refusal message, one row of an administrator page and one line of a log. It is
    /// far above any name an operator would type and far below a length that turns a document
    /// into a payload.
    ///
    /// The unit is what <see cref="string.Length"/> counts, so a name written in characters
    /// outside the basic plane reaches this bound in half as many of them. That is stated rather
    /// than corrected: the alternative counts text elements, which is a different number again,
    /// and neither is the one an operator has in mind.
    /// </remarks>
    public const int MaximumNameLength = 255;

    private const string NamePointer = "/" + NameMember;

    /// <summary>
    /// The member a rule document carries its rule in.
    /// </summary>
    /// <remarks>
    /// The name lives here rather than on the composition stage because that stage is handed an
    /// element and a pointer and never looks a member up: it reads the shape of a group wherever
    /// the group is, which is what lets it read a nested one. Which member of a document holds the
    /// outermost group is this type's business, because this type is the one thing that reads a
    /// whole document.
    /// </remarks>
    public const string MatchMember = "match";

    private const string MatchPointer = "/" + MatchMember;

    /// <summary>
    /// The top-level members this version of the format declares, in the order a document writes
    /// them.
    /// </summary>
    /// <remarks>
    /// Written from the constants that name them rather than as literals, so a member renamed on
    /// either type is renamed here in the same edit. The list is the one a refusal shows and the
    /// one the schema is held against, and there is no third copy.
    /// </remarks>
    private static readonly string[] DeclaredMembers =
    [
        SchemaVersionMember,
        IdMember,
        NameMember,
        RuleItemScopeReader.CollectsMember,
        MatchMember
    ];

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

            if (!root.TryGetProperty(IdMember, out var declaredId))
            {
                return Refuse(
                    IdPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The document declares no {IdMember}. Every rule document carries one as a string at its top level, and it is what this rule is called by everything that is not a person. A document without one is refused rather than given an id derived from its name or its file, because an identity derived from either of those changes when that one is edited, which is the whole thing an identity exists not to do."));
            }

            if (declaredId.ValueKind != JsonValueKind.String)
            {
                return Refuse(
                    IdPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{IdMember} is {DescribeKind(declaredId.ValueKind)} and has to be a string."));
            }

            // Not null: the kind is String, and GetString returns null only for JsonValueKind.Null.
            var id = declaredId.GetString()!;

            if (id.Length == 0)
            {
                return Refuse(
                    IdPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{IdMember} is empty, and a rule with no identity cannot be told from any other."));
            }

            var outside = IndexOfCharacterOutsideTheIdSet(id);
            if (outside >= 0)
            {
                return Refuse(
                    IdPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{IdMember} holds {Quote(id[outside])} at position {outside}, and an id is made of {IdSetInWords}. The set is narrow because this member is compared rather than read: two ids that a person cannot tell apart, or that differ only in how their text was encoded, would be two identities wearing one face."));
            }

            if (id.Length > MaximumIdLength)
            {
                return Refuse(
                    IdPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{IdMember} is {id.Length} characters long and the most an id may be is {MaximumIdLength}."));
            }

            if (!root.TryGetProperty(NameMember, out var declaredName))
            {
                return Refuse(
                    NamePointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The document declares no {NameMember}. Every rule document carries one as a string at its top level, and it is what the collection this rule owns is called. A document without one is refused rather than named after its file, because the file name is the store's business and an operator renaming a collection would then have to rename a file to do it."));
            }

            if (declaredName.ValueKind != JsonValueKind.String)
            {
                return Refuse(
                    NamePointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{NameMember} is {DescribeKind(declaredName.ValueKind)} and has to be a string."));
            }

            // Not null: the kind is String, and GetString returns null only for JsonValueKind.Null.
            var name = declaredName.GetString()!;

            if (name.Length == 0)
            {
                return Refuse(
                    NamePointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{NameMember} is empty, and a collection with no name is one nobody can find in a library."));
            }

            if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1]))
            {
                return Refuse(
                    NamePointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{NameMember} begins or ends with whitespace. It is refused rather than trimmed, because this plugin does not rewrite what you wrote, and two names differing only at an invisible edge are two names nobody can tell apart."));
            }

            var control = IndexOfControlCharacter(name);
            if (control >= 0)
            {
                return Refuse(
                    NamePointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{NameMember} holds a control character at position {control}, U+{(int)name[control]:X4}. The name is what a library shows, and a character that renders as nothing there makes the collection somebody sees a different string from the one written here."));
            }

            if (name.Length > MaximumNameLength)
            {
                return Refuse(
                    NamePointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{NameMember} is {name.Length} characters long and the most a name may be is {MaximumNameLength}."));
            }

            var inside = ReadInsideTheEnvelope(root);

            return inside.Count > 0
                ? RuleDocumentValidation.Refused(inside)
                : RuleDocumentValidation.Accepted(new RuleDocument(version, id, name, text));
        }
    }

    /// <summary>
    /// Reads what the envelope carries: the scope the rule collects over, and the rule itself.
    /// </summary>
    /// <remarks>
    /// The stages are called in the order a document meets them, and the first one that refuses is
    /// the last one that runs. That is not a choice about how many reasons to collect: each stage
    /// is handed what the stage before it produced, so a field read over a composition that was
    /// refused would be a read over a tree nobody built. Inside one stage every reason is still
    /// collected, which is where a document with two mistakes in one member gets both of them.
    ///
    /// A MEMBER THIS VERSION DOES NOT DECLARE IS REFUSED, decided on #231 on 2026-09-04. It is the
    /// half of that decision that catches a misspelling: a document writing <c>mach</c> where it
    /// meant <see cref="MatchMember"/> used to be accepted, and used to be indistinguishable from
    /// one that meant to carry no rule at all.
    ///
    /// REFUSING IT COSTS NO FORWARD COMPATIBILITY, which is what makes it available rather than a
    /// trade. A document written for a later version of this format declares a later
    /// <see cref="SchemaVersionMember"/>, and the envelope stage above refuses one higher than this
    /// plugin reads before any of this runs. So a member nobody here declares, on a document
    /// claiming this version, is a mistake rather than a member from the future.
    ///
    /// THE OTHER HALF OF THAT DECISION IS NOT HERE. A document declaring no
    /// <see cref="MatchMember"/> at all is still accepted, and it is refused under the same
    /// decision once every document in the suite, the corpus and the pages carries a rule. #231
    /// holds that, and this paragraph is what stops a reader taking the refusal above for the
    /// whole answer.
    /// </remarks>
    private static IReadOnlyList<RuleValidationError> ReadInsideTheEnvelope(JsonElement root)
    {
        var unknown = FirstUndeclaredMember(root);
        if (unknown is not null)
        {
            return [new RuleValidationError(
                "/" + unknown,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"This version declares no member called \"{unknown}\". The members are {string.Join(", ", DeclaredMembers)}. A member this version does not declare is refused rather than carried, because a document written for a later format declares a later {SchemaVersionMember} and is refused above, so a name here that nothing reads is a mistake and most often a misspelling."))];
        }

        var scope = RuleItemScopeReader.Read(root);
        if (!scope.IsAccepted)
        {
            return scope.Errors;
        }

        if (!root.TryGetProperty(MatchMember, out var declaredMatch))
        {
            return [];
        }

        var composition = RuleCompositionReader.Read(declaredMatch, MatchPointer);
        if (!composition.IsAccepted)
        {
            return composition.Errors;
        }

        var fields = RuleFieldReader.Read(root, composition.Group!, scope.Kinds);
        if (!fields.IsAccepted)
        {
            return fields.Errors;
        }

        var operators = RuleOperatorReader.Read(root, fields.Fields);
        if (!operators.IsAccepted)
        {
            return operators.Errors;
        }

        var values = RuleValueReader.Read(root, operators.Operators);

        return values.IsAccepted ? [] : values.Errors;
    }

    // The first member of the document that this version does not declare, or null. The document's
    // own order rather than the declared order, because the message names what somebody wrote and
    // the first thing they wrote wrong is the one they are looking at.
    private static string? FirstUndeclaredMember(JsonElement root)
    {
        foreach (var member in root.EnumerateObject())
        {
            if (Array.IndexOf(DeclaredMembers, member.Name) < 0)
            {
                return member.Name;
            }
        }

        return null;
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

    // The position of the first control character, or -1. Written as a loop rather than as a
    // search over a set, because the set is a property of the code point and not a list somebody
    // has to keep: C0 and C1 both count, and a name carrying one arrives escaped, since a raw
    // one never gets past the parser.
    private static int IndexOfControlCharacter(string name)
    {
        for (var index = 0; index < name.Length; index++)
        {
            if (char.IsControl(name[index]))
            {
                return index;
            }
        }

        return -1;
    }

    // The position of the first character outside the declared set, or -1. The set is written as
    // a range test rather than as a list or a character class, because what it has to be is
    // exactly this and nothing a library decides: char.IsLetterOrDigit accepts every script and
    // every digit Unicode knows, which is the opposite of what an identity wants.
    //
    // Three refusals the name member spells out separately are inside this one. Whitespace at an
    // edge, whitespace anywhere else and a control character are all outside the set, so an id
    // carrying one is refused here and the message names the position rather than the category.
    private static int IndexOfCharacterOutsideTheIdSet(string id)
    {
        for (var index = 0; index < id.Length; index++)
        {
            var character = id[index];

            var permitted = (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9')
                || character == '-';

            if (!permitted)
            {
                return index;
            }
        }

        return -1;
    }

    // The character as a message can carry it. A control character or a space printed raw makes a
    // refusal that says nothing where the character should be, so anything outside the printable
    // ASCII range is named by its code point instead of shown.
    private static string Quote(char character)
        => character is >= ' ' and <= '~'
            ? string.Create(CultureInfo.InvariantCulture, $"'{character}'")
            : string.Create(CultureInfo.InvariantCulture, $"U+{(int)character:X4}");

    // The same vocabulary as Describe, minus the clause it adds for a number. Describe is called
    // where a number has already failed to be a 32-bit integer and saying so is the whole point
    // there; here a number is simply not a string, and borrowing that sentence would report a
    // name as a bad integer.
    private static string DescribeKind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Number => "a number",
        _ => Describe(kind)
    };
}
