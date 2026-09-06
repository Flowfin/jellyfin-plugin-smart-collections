using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Reads the order a rule declares and the cap it may put on the collection, and refuses a cap
/// written without an order.
/// </summary>
/// <remarks>
/// A COLLECTION HAS AN ORDER WHETHER OR NOT A DOCUMENT DECLARES ONE, which is why this stage
/// exists at all. Without a declared sort the order is the one the evaluation ends on, which is
/// the identifier and nothing else: total, reproducible, and meaningless to a person. A sort makes
/// the order the document's rather than the plugin's, and the identifier stays underneath it as
/// the tie-break, so an order a document declares is total however many items share the value it
/// names.
///
/// A CAP WITHOUT AN ORDER IS REFUSED RATHER THAN GIVEN THE IDENTIFIER ORDER. "The first fifty" of
/// a set nobody ordered is fifty items chosen by how their identifiers happen to sort, which is
/// reproducible and is not a thing anybody meant. The two repairs a person would make - declare
/// the sort, or drop the cap - are each one line, and the refusal names both. This is the one rule
/// in this stage that is about a PAIR of members rather than about either one of them, and it is
/// here rather than in the schema because a schema processor cannot say why.
///
/// Every reason is collected rather than the first, for the reason the neighbouring stages give: a
/// sort with two mistakes in it is one repair when both are named and two when they arrive one at
/// a time.
/// </remarks>
public static class RuleSortReader
{
    /// <summary>
    /// The member a rule document declares its order in.
    /// </summary>
    /// <remarks>
    /// Here rather than on <see cref="RuleSortTable"/> beside the names a TERM writes, because
    /// this one and <see cref="LimitMember"/> are members of the DOCUMENT and the two beside them
    /// are not. The suite reflects over the stages that declare document members to compare them
    /// against the schema, and a term's member name reaching that comparison would ask the schema
    /// to declare a top-level member nobody writes.
    /// </remarks>
    public const string SortMember = "sort";

    /// <summary>
    /// The member a rule document declares its cap in.
    /// </summary>
    public const string LimitMember = "limit";

    private const string SortPointer = "/" + SortMember;

    private const string LimitPointer = "/" + LimitMember;

    /// <summary>
    /// Reads the order and the cap a rule document declares.
    /// </summary>
    /// <param name="document">The document, at its top level.</param>
    /// <param name="scope">The kinds the rule collects, as the scope stage read them.</param>
    /// <returns>The terms and the cap, or every reason the read was refused.</returns>
    public static RuleSortRead Read(JsonElement document, IReadOnlyList<RuleItemKindRow> scope)
        => Read(document, scope, RuleFieldTable.Find);

    /// <summary>
    /// The same read against a vocabulary the caller supplies.
    /// </summary>
    /// <param name="document">The document, at its top level.</param>
    /// <param name="scope">The kinds the rule collects, as the scope stage read them.</param>
    /// <param name="vocabulary">Resolves a name a document wrote to a field row, or to nothing.</param>
    /// <returns>The terms and the cap, or every reason the read was refused.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scope"/> or <paramref name="vocabulary"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// INTERNAL, AND IT EXISTS FOR THE REASON THE FIELD STAGE'S OWN SEAM DOES. Every field this
    /// version declares applies to every item kind a rule may collect, so the arm below that
    /// refuses a term naming a field that means nothing for anything the rule collects cannot be
    /// reached through the real vocabulary by any document. A guard with no proof that it bites is
    /// refused here by name, and a fixture vocabulary is what reaches it.
    ///
    /// It is not a hook and it is not configuration: nothing outside this assembly and the suite
    /// can call it, and the read above binds the vocabulary to the table.
    /// </remarks>
    internal static RuleSortRead Read(
        JsonElement document,
        IReadOnlyList<RuleItemKindRow> scope,
        Func<string, RuleFieldRow?> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var errors = new List<RuleValidationError>();
        var terms = ReadTerms(document, scope, vocabulary, errors);
        var limit = ReadLimit(document, errors);

        if (errors.Count > 0)
        {
            return RuleSortRead.Refused(errors);
        }

        // Last, and only where both members were read without a reason of their own, so a document
        // whose sort is misspelled is told what is wrong with the sort rather than told that its
        // cap has no order.
        if (limit is not null && terms.Count == 0)
        {
            return RuleSortRead.Refused(
            [
                new RuleValidationError(
                    LimitPointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The document declares a {LimitMember} and no {SortMember}. The first {limit} of a set nobody ordered are the items whose identifiers happen to sort first, which is reproducible and is not a thing anybody means. Declare the order the cap applies to, or drop the cap."))
            ]);
        }

        return RuleSortRead.Accepted(terms, limit);
    }

    private static List<RuleSortTerm> ReadTerms(
        JsonElement document,
        IReadOnlyList<RuleItemKindRow> scope,
        Func<string, RuleFieldRow?> vocabulary,
        List<RuleValidationError> errors)
    {
        var terms = new List<RuleSortTerm>();

        if (document.ValueKind != JsonValueKind.Object
            || !document.TryGetProperty(SortMember, out var declared))
        {
            return terms;
        }

        if (declared.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(
                SortPointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{SortMember} has to be an array of terms, each naming a {RuleSortTable.FieldMember} and a {RuleSortTable.DirectionMember}, and this document writes something else there. A single term written on its own is refused rather than read as a list of one, so a rule that later orders by two fields does not change shape as well as order.")));
            return terms;
        }

        if (declared.GetArrayLength() == 0)
        {
            errors.Add(new RuleValidationError(
                SortPointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{SortMember} is empty. A rule declaring no order leaves the member out, and an empty array is most often a term somebody deleted rather than an order somebody meant.")));
            return terms;
        }

        var taken = new Dictionary<RuleField, int>();
        var index = 0;

        foreach (var member in declared.EnumerateArray())
        {
            ReadTerm(
                member,
                SortPointer + "/" + index.ToString(CultureInfo.InvariantCulture),
                index,
                scope,
                vocabulary,
                taken,
                terms,
                errors);
            index++;
        }

        return terms;
    }

    private static void ReadTerm(
        JsonElement member,
        string at,
        int index,
        IReadOnlyList<RuleItemKindRow> scope,
        Func<string, RuleFieldRow?> vocabulary,
        Dictionary<RuleField, int> taken,
        List<RuleSortTerm> terms,
        List<RuleValidationError> errors)
    {
        if (member.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new RuleValidationError(
                at,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A sort term is an object naming a {RuleSortTable.FieldMember} and a {RuleSortTable.DirectionMember}.")));
            return;
        }

        var field = ReadField(member, at, scope, vocabulary, errors);
        var direction = ReadDirection(member, at, errors);

        if (field is null || direction is null)
        {
            return;
        }

        if (taken.TryGetValue(field.Field, out var first))
        {
            // Refused rather than folded away, for the reason a repeated item kind is: a second
            // term over one field decides nothing, because the first has already ordered every
            // item that carries a value for it, and a document saying one thing twice is most
            // often a half-finished edit.
            errors.Add(new RuleValidationError(
                at,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The order already sorts by \"{field.Name}\", at position {first}. A second term over one field decides nothing, because the first has already ordered every item that carries a value for it.")));
            return;
        }

        taken.Add(field.Field, index);
        terms.Add(new RuleSortTerm(field, direction.Value, at));
    }

    private static RuleFieldRow? ReadField(
        JsonElement term,
        string at,
        IReadOnlyList<RuleItemKindRow> scope,
        Func<string, RuleFieldRow?> vocabulary,
        List<RuleValidationError> errors)
    {
        var pointer = at + "/" + RuleSortTable.FieldMember;

        if (!term.TryGetProperty(RuleSortTable.FieldMember, out var declared))
        {
            errors.Add(new RuleValidationError(
                pointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A sort term names the {RuleSortTable.FieldMember} it orders by. The fields a rule may order by are {RuleSortTable.WrittenFieldNames}.")));
            return null;
        }

        if (declared.ValueKind != JsonValueKind.String)
        {
            errors.Add(new RuleValidationError(
                pointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A sort term's {RuleSortTable.FieldMember} is written as a string naming one of {RuleSortTable.WrittenFieldNames}.")));
            return null;
        }

        var name = declared.GetString()!;
        var row = vocabulary(name);

        if (row is null)
        {
            errors.Add(RuleFieldTable.RefuseUnknownField(name, pointer));
            return null;
        }

        if (!RuleSortTable.CanSortBy(row.Field))
        {
            errors.Add(RuleSortTable.RefuseUnsortableField(row, pointer));
            return null;
        }

        if (!RuleFieldTable.AppliesToAnyOf(row, scope))
        {
            errors.Add(RuleFieldTable.RefuseOutsideScope(row, scope, pointer));
            return null;
        }

        return row;
    }

    private static RuleSortDirection? ReadDirection(JsonElement term, string at, List<RuleValidationError> errors)
    {
        var pointer = at + "/" + RuleSortTable.DirectionMember;

        if (!term.TryGetProperty(RuleSortTable.DirectionMember, out var declared))
        {
            // Refused rather than defaulted to ascending. A default is a direction the document
            // does not carry, so two readers of one file disagree about what it says, and the
            // repair is one word.
            errors.Add(new RuleValidationError(
                pointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A sort term names the {RuleSortTable.DirectionMember} it orders in, which is one of {RuleSortTable.WrittenDirections}. It is refused rather than defaulted, because a direction nobody wrote is one two readers of this file would disagree about.")));
            return null;
        }

        if (declared.ValueKind != JsonValueKind.String)
        {
            errors.Add(new RuleValidationError(
                pointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A sort term's {RuleSortTable.DirectionMember} is written as a string naming one of {RuleSortTable.WrittenDirections}.")));
            return null;
        }

        var name = declared.GetString()!;
        var direction = RuleSortTable.Find(name);

        if (direction is null)
        {
            errors.Add(new RuleValidationError(
                pointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"There is no sort direction called \"{name}\". The directions are {RuleSortTable.WrittenDirections}.")));
            return null;
        }

        return direction;
    }

    private static int? ReadLimit(JsonElement document, List<RuleValidationError> errors)
    {
        if (document.ValueKind != JsonValueKind.Object
            || !document.TryGetProperty(LimitMember, out var declared))
        {
            return null;
        }

        if (declared.ValueKind != JsonValueKind.Number || !declared.TryGetInt32(out var limit))
        {
            errors.Add(new RuleValidationError(
                LimitPointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{LimitMember} is written as a whole number of items, and this document writes something else there.")));
            return null;
        }

        if (limit < 1)
        {
            errors.Add(new RuleValidationError(
                LimitPointer,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{LimitMember} is {limit}, and a rule collects at least one item. A rule meant to collect nothing is not one anybody writes on purpose, so it is refused rather than read as a collection that stays empty.")));
            return null;
        }

        return limit;
    }
}
