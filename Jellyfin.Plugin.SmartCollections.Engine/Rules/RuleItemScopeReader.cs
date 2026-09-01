using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Reads the item kinds a rule declares it collects, and refuses a document that declares none.
/// </summary>
/// <remarks>
/// This stage reads the <c>collects</c> member and nothing else, exactly as the composition stage
/// reads the shape of a rule and the field stage reads the name each condition writes. Keeping
/// them apart is what lets a document that collects nothing be told from one whose groups are
/// wrong, which are different repairs.
///
/// THE MEMBER IS REQUIRED AND IS NEITHER DEFAULTED NOR INFERRED, which is the whole of what this
/// stage is for. Defaulting to every kind makes every rule a full library walk on a library nobody
/// measured; inferring the scope from the fields a rule happens to use makes adding one condition
/// silently change the size of the query. Both read well on a small library and neither can be
/// explained to somebody whose server got slower.
///
/// Every reason is collected rather than the first, for the reason the neighbouring stages give: a
/// list of kinds with two mistakes in it is one repair when both are named and two repairs when
/// they arrive one at a time.
/// </remarks>
public static class RuleItemScopeReader
{
    /// <summary>
    /// The member a rule document declares its item scope in.
    /// </summary>
    public const string CollectsMember = "collects";

    private const string CollectsPointer = "/" + CollectsMember;

    /// <summary>
    /// Reads the scope a rule document declares.
    /// </summary>
    /// <param name="document">The document, at its top level.</param>
    /// <returns>The kinds the rule collects, or every reason the read was refused.</returns>
    public static RuleItemScopeRead Read(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object || !document.TryGetProperty(CollectsMember, out var declared))
        {
            return Refuse(
                CollectsPointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The document declares no {CollectsMember}. Every rule document says which item kinds it collects, as an array of one or more of {RuleItemKindTable.WrittenNames}. It is refused rather than defaulted, because a rule with no scope is a rule that reads every item in the library."));
        }

        if (declared.ValueKind != JsonValueKind.Array)
        {
            // The refusal does not say which kind was written instead, and the neighbouring
            // stages do. Naming it costs an arm per JSON kind plus one for a kind nothing can
            // reach here, which is a branch no fixture can execute in a tree whose coverage is
            // read on every run. The member is named, the shape it has to take is named, and the
            // pointer is where the operator is already looking.
            return Refuse(
                CollectsPointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{CollectsMember} has to be an array naming one or more of {RuleItemKindTable.WrittenNames}, and this document writes something else there. A single name written on its own is refused rather than read as a list of one, because a rule that later collects two kinds would then change shape as well as scope."));
        }

        if (declared.GetArrayLength() == 0)
        {
            return Refuse(
                CollectsPointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{CollectsMember} is empty, and a rule that collects no kind of item collects nothing. The kinds a rule may collect are {RuleItemKindTable.WrittenNames}."));
        }

        var errors = new List<RuleValidationError>();

        // Keyed on the row rather than on the name, so a second spelling of one kind would still
        // be caught if a row ever declared two names. Nothing declares two today, and this is one
        // word rather than a rewrite on the day something does.
        var taken = new Dictionary<RuleItemKind, int>();
        var index = 0;

        foreach (var member in declared.EnumerateArray())
        {
            ReadKind(member, CollectsPointer + "/" + index.ToString(CultureInfo.InvariantCulture), index, taken, errors);
            index++;
        }

        if (errors.Count > 0)
        {
            return RuleItemScopeRead.Refused(errors);
        }

        // The table's order rather than the document's, which is what makes a scope a set. Built
        // by walking the table so the result cannot carry a kind twice however the document was
        // written.
        return RuleItemScopeRead.Accepted(
            RuleItemKindTable.Rows.Where(row => taken.ContainsKey(row.Kind)).ToArray());
    }

    private static void ReadKind(
        JsonElement member,
        string at,
        int index,
        Dictionary<RuleItemKind, int> taken,
        List<RuleValidationError> errors)
    {
        if (member.ValueKind != JsonValueKind.String)
        {
            // Refused rather than read through ToString, for the reason the field stage gives: a
            // kind is a name from a declared list, so a number or an object in the place a name
            // goes is somebody writing something else rather than spelling a name unusually.
            errors.Add(new RuleValidationError(
                at,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"An item kind is written as a string naming one of {RuleItemKindTable.WrittenNames}.")));
            return;
        }

        var name = member.GetString()!;
        var row = RuleItemKindTable.Find(name);

        if (row is null)
        {
            errors.Add(RuleItemKindTable.RefuseUnknownKind(name, at));
            return;
        }

        if (taken.TryGetValue(row.Kind, out var first))
        {
            // Refused rather than folded away. A scope is a set, so a repeat changes nothing about
            // what the rule collects, and a document saying one thing twice is a document somebody
            // edited without finishing - most often the half-done edit that meant to replace it.
            errors.Add(new RuleValidationError(
                at,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"\"{name}\" is already collected, at position {first}. A rule names each kind once, and a repeat is left to be repaired rather than ignored.")));
            return;
        }

        taken.Add(row.Kind, index);
    }

    private static RuleItemScopeRead Refuse(string pointer, string message)
        => RuleItemScopeRead.Refused(new List<RuleValidationError> { new(pointer, message) });
}
