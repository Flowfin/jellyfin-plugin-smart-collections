using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Reads the field each condition of a rule names, and refuses a name the vocabulary does not
/// declare.
/// </summary>
/// <remarks>
/// This stage reads the <c>field</c> member of a condition and nothing else. Which operator the
/// condition applies and what its value says are the next stage's business over the same text,
/// exactly as the composition stage reads the shape of a rule and never a condition. Keeping the
/// three apart is what lets a document with an unknown field be told from one whose groups are
/// wrong and from one whose value will not parse, which are three different repairs.
///
/// The stage is handed the document and the tree the composition stage produced, rather than
/// walking the document itself. A condition is a place in the document, and the tree is what says
/// where those places are; re-deciding here which members of a rule are conditions would give one
/// document two answers.
///
/// IT IS ALSO HANDED THE SCOPE, AND THAT ONE IS NOT OPTIONAL, for the reason
/// <see cref="RuleQueryCompiler"/> gives about its own. A field that means nothing for anything
/// the rule collects is a mistake this stage can name before a query runs, and it can name it
/// only if it knows what the rule collects. An overload without the scope would make the check
/// something a caller remembers, which is where a check is forgotten once.
/// </remarks>
public static class RuleFieldReader
{
    /// <summary>
    /// The member of a condition that names the field.
    /// </summary>
    public const string FieldMember = "field";

    /// <summary>
    /// Reads every condition in a composition and resolves the field each one names.
    /// </summary>
    /// <param name="document">The document the composition was read from.</param>
    /// <param name="group">The outermost group of the composition.</param>
    /// <param name="scope">The kinds the rule collects, as the scope stage read them.</param>
    /// <returns>One row per condition, or every reason the read was refused.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="group"/> or <paramref name="scope"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A condition pointer in <paramref name="group"/> refers to nothing in
    /// <paramref name="document"/>, which means the tree and the document are not the same read.
    /// </exception>
    public static RuleFieldRead Read(
        JsonElement document,
        RuleConditionGroup group,
        IReadOnlyList<RuleItemKindRow> scope)
        => Read(document, group, scope, RuleFieldTable.Find);

    /// <summary>
    /// The same read, against a vocabulary the caller supplies.
    /// </summary>
    /// <param name="document">The document the composition was read from.</param>
    /// <param name="group">The outermost group of the composition.</param>
    /// <param name="scope">The kinds the rule collects, as the scope stage read them.</param>
    /// <param name="vocabulary">Resolves a name a document wrote to a field row, or to nothing.</param>
    /// <returns>One row per condition, or every reason the read was refused.</returns>
    /// <remarks>
    /// INTERNAL, AND IT EXISTS FOR ONE REASON WORTH WRITING DOWN. Every field this version
    /// declares applies to every item kind a rule may collect, so the arm below that refuses a
    /// field meaning nothing for anything the rule collects cannot be reached through the real
    /// vocabulary at all - not by a document anybody can write, and not by one nobody would. A
    /// guard with no proof that it bites is refused here by name, and this seam is what a fixture
    /// vocabulary reaches it through.
    ///
    /// It is not a hook and it is not configuration. Nothing outside this assembly and the suite
    /// can call it, the public read above binds the vocabulary to the table, and no route a
    /// document takes chooses one. On the day a declared field is narrowed to fewer kinds, the arm
    /// becomes reachable through the public read and this overload stops being the only way in;
    /// it is not removed then, because the fixture is still the cheaper proof.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="group"/>, <paramref name="scope"/> or <paramref name="vocabulary"/> is
    /// <see langword="null"/>.
    /// </exception>
    internal static RuleFieldRead Read(
        JsonElement document,
        RuleConditionGroup group,
        IReadOnlyList<RuleItemKindRow> scope,
        Func<string, RuleFieldRow?> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var fields = new List<RuleConditionField>();
        var errors = new List<RuleValidationError>();

        ReadGroup(document, group, scope, vocabulary, fields, errors);

        return errors.Count > 0 ? RuleFieldRead.Refused(errors) : RuleFieldRead.Accepted(fields);
    }

    /// <summary>
    /// Resolves an RFC 6901 JSON Pointer against a document.
    /// </summary>
    /// <param name="document">The document to resolve against.</param>
    /// <param name="pointer">The pointer, which is empty for the document itself.</param>
    /// <returns>The element, or <see langword="null"/> where the pointer refers to nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pointer"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The two escapes are decoded in the order RFC 6901 requires, <c>~1</c> before <c>~0</c>, so
    /// a member literally called <c>~1</c> resolves to itself rather than to a member called
    /// <c>/</c>. No member of a rule document carries either character today, which is the reason
    /// to write the order down rather than the reason to skip it.
    /// </remarks>
    public static JsonElement? Resolve(JsonElement document, string pointer)
    {
        ArgumentNullException.ThrowIfNull(pointer);

        if (pointer.Length == 0)
        {
            return document;
        }

        if (pointer[0] != '/')
        {
            return null;
        }

        var current = document;

        foreach (var raw in pointer[1..].Split('/'))
        {
            var token = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

            switch (current.ValueKind)
            {
                case JsonValueKind.Object:
                    if (!current.TryGetProperty(token, out var member))
                    {
                        return null;
                    }

                    current = member;
                    break;

                case JsonValueKind.Array:
                    if (!int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index)
                        || index >= current.GetArrayLength())
                    {
                        return null;
                    }

                    current = current[index];
                    break;

                default:
                    return null;
            }
        }

        return current;
    }

    private static void ReadGroup(
        JsonElement document,
        RuleConditionGroup group,
        IReadOnlyList<RuleItemKindRow> scope,
        Func<string, RuleFieldRow?> vocabulary,
        List<RuleConditionField> fields,
        List<RuleValidationError> errors)
    {
        // The conditions this group holds, then the groups it holds. The tree keeps the two in
        // separate lists, so the order a document interleaved them in is not recoverable here and
        // this order is declared rather than inherited.
        foreach (var pointer in group.ConditionPointers)
        {
            ReadCondition(document, pointer, scope, vocabulary, fields, errors);
        }

        foreach (var child in group.Groups)
        {
            ReadGroup(document, child, scope, vocabulary, fields, errors);
        }
    }

    private static void ReadCondition(
        JsonElement document,
        string pointer,
        IReadOnlyList<RuleItemKindRow> scope,
        Func<string, RuleFieldRow?> vocabulary,
        List<RuleConditionField> fields,
        List<RuleValidationError> errors)
    {
        var condition = Resolve(document, pointer)
            ?? throw new ArgumentException(
                "The condition at " + pointer + " is not in this document, so the tree and the document are not the same read.",
                nameof(document));

        if (!condition.TryGetProperty(FieldMember, out var field))
        {
            errors.Add(new RuleValidationError(
                pointer,
                "This condition names no field. A condition carries a \"" + FieldMember
                + "\" member, and the fields are " + string.Join(", ", RuleFieldTable.Names) + "."
                + RefusedMemberNote(condition)));
            return;
        }

        var at = pointer + "/" + FieldMember;

        if (field.ValueKind != JsonValueKind.String)
        {
            // Refused rather than read through ToString. A field is a name from a declared list,
            // so a number or an object there is not a name spelled unusually, it is somebody
            // writing something else in the place a name goes.
            errors.Add(new RuleValidationError(
                at,
                "A field is written as a string naming one of " + string.Join(", ", RuleFieldTable.Names) + "."));
            return;
        }

        var name = field.GetString()!;
        var row = vocabulary(name);

        if (row is null)
        {
            errors.Add(RuleFieldTable.RefuseUnknownField(name, at));
            return;
        }

        // Read after the name resolves, because a field the vocabulary does not hold at all is a
        // different repair from one that exists and means nothing here, and telling somebody
        // about a scope for a field that does not exist would answer the wrong question.
        if (!RuleFieldTable.AppliesToAnyOf(row, scope))
        {
            errors.Add(RuleFieldTable.RefuseOutsideScope(row, scope, at));
            return;
        }

        fields.Add(new RuleConditionField(pointer, row));
    }

    /// <summary>
    /// The refusal note for the first member of a condition that names a refused construct.
    /// </summary>
    /// <param name="condition">The condition as the document wrote it.</param>
    /// <returns>The note, or the empty string where no member names one.</returns>
    /// <remarks>
    /// A condition carrying no <c>field</c> member is refused whatever else it holds, and the
    /// message above lists the fields that exist. Where the member somebody wrote instead is a
    /// construct this plugin refuses rather than one it has not got round to, the two are
    /// different answers, and this is where the difference is said. The members are read in the
    /// order the document wrote them, so a condition reaching for two refusals names the first
    /// one written rather than whichever the table happens to hold first.
    /// </remarks>
    private static string RefusedMemberNote(JsonElement condition)
    {
        foreach (var member in condition.EnumerateObject())
        {
            var note = RuleRefusalTable.Note(member.Name);

            if (note.Length > 0)
            {
                return note;
            }
        }

        return string.Empty;
    }
}
