using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Reads the value each condition of a rule writes, and parses it against the type the condition's
/// field and operator settle on.
/// </summary>
/// <remarks>
/// This stage reads the <c>value</c> member of a condition and nothing else, exactly as
/// <see cref="RuleOperatorReader"/> reads the operator and never the value, the field stage reads
/// the field and never the operator, and the composition stage reads the shape and never a
/// condition. Keeping them apart is what lets a document whose value will not parse be told from
/// one whose operator is wrong, from one whose field is wrong and from one whose groups are wrong,
/// which are four different repairs.
///
/// It is handed what the operator stage produced rather than the composition tree, because every
/// question it asks needs both rows. Which type the value is parsed against is the field's type
/// narrowed by the operator's value column. Whether a value belongs there at all is the operator's
/// value column. Whether it is written as one value or as a list is the operator's own
/// <see cref="RuleOperatorRow.TakesAList"/>.
///
/// THE MESSAGE NAMES THE FIELD AND THE PARSER NAMES THE VALUE AND THE FORM, WHICH IS WHY A REFUSAL
/// HERE IS TWO SENTENCES. The parser's own sentence is reused rather than rewritten, so the words
/// an operator reads for a value that will not parse are the same words wherever the parser is
/// called from and the same words <c>docs/rule-values.md</c> carries. What the parser cannot say
/// is which field the value was written against, because it is handed a type and never a row, and
/// naming the field is what the done condition on the value-types issue asks for.
///
/// This stage parses and does not compile. Whether the values it produced can be turned into a
/// query is the compiler's question, and it is a different milestone.
/// </remarks>
public static class RuleValueReader
{
    /// <summary>
    /// The member of a condition that carries the value.
    /// </summary>
    public const string ValueMember = "value";

    /// <summary>
    /// Reads the value each condition writes and parses it.
    /// </summary>
    /// <param name="document">The document the conditions were read from.</param>
    /// <param name="operators">One entry per condition, as the operator stage produced them.</param>
    /// <returns>One entry per condition, or every reason the read was refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operators"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A condition pointer in <paramref name="operators"/> refers to nothing in
    /// <paramref name="document"/>, which means the operator read and the document are not the
    /// same read.
    /// </exception>
    public static RuleValueRead Read(JsonElement document, IReadOnlyList<RuleConditionOperator> operators)
    {
        ArgumentNullException.ThrowIfNull(operators);

        var conditions = new List<RuleConditionValue>();
        var errors = new List<RuleValidationError>();

        foreach (var entry in operators)
        {
            ReadCondition(document, entry, conditions, errors);
        }

        return errors.Count > 0 ? RuleValueRead.Refused(errors) : RuleValueRead.Accepted(conditions);
    }

    private static void ReadCondition(
        JsonElement document,
        RuleConditionOperator entry,
        List<RuleConditionValue> conditions,
        List<RuleValidationError> errors)
    {
        var condition = RuleFieldReader.Resolve(document, entry.Pointer)
            ?? throw new ArgumentException(
                "The condition at " + entry.Pointer
                + " is not in this document, so the operator read and the document are not the same read.",
                nameof(document));

        var written = condition.TryGetProperty(ValueMember, out var member) ? member : (JsonElement?)null;

        if (!entry.Operator.TakesAValue)
        {
            ReadValuelessCondition(entry, written, conditions, errors);
            return;
        }

        if (written is null)
        {
            errors.Add(new RuleValidationError(
                entry.Pointer,
                "This condition writes no value. The operator \"" + entry.Operator.Name
                + "\" is written with " + Shape(entry.Operator) + ", in a \"" + ValueMember + "\" member."));
            return;
        }

        var at = entry.Pointer + "/" + ValueMember;
        var type = RuleOperatorTable.ValueTypeFor(entry.Operator.Operator, entry.Field.ValueType);

        var values = entry.Operator.TakesAList
            ? ReadList(entry, written.Value, at, type, errors)
            : ReadOne(entry, written.Value, at, type, errors);

        if (values is not null)
        {
            conditions.Add(new RuleConditionValue(entry.Pointer, entry.Field, entry.Operator, values));
        }
    }

    // An operator that asks about the field alone. A value written beside one is refused rather
    // than ignored: whoever wrote it meant it to narrow the condition, and a plugin that dropped
    // it would collect a set the document does not describe while reporting nothing.
    private static void ReadValuelessCondition(
        RuleConditionOperator entry,
        JsonElement? written,
        List<RuleConditionValue> conditions,
        List<RuleValidationError> errors)
    {
        if (written is not null)
        {
            errors.Add(new RuleValidationError(
                entry.Pointer + "/" + ValueMember,
                "The operator \"" + entry.Operator.Name
                + "\" takes no value, and there is one written here. It asks about the \""
                + entry.Field.Name + "\" field alone."));
            return;
        }

        conditions.Add(new RuleConditionValue(entry.Pointer, entry.Field, entry.Operator, []));
    }

    private static IReadOnlyList<RuleValue>? ReadOne(
        RuleConditionOperator entry,
        JsonElement written,
        string at,
        RuleValueType type,
        List<RuleValidationError> errors)
    {
        // An array where one value belongs is refused by its own sentence rather than by the
        // parser's. The parser would say the value is not a JSON string, which is true and sends
        // whoever wrote a list to check the quoting instead of the operator.
        if (written.ValueKind == JsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(
                at,
                "The operator \"" + entry.Operator.Name
                + "\" is written with one value rather than a list. The operators that take a list are "
                + ListTakingOperatorNames() + "."));
            return null;
        }

        // No declared names, because no field row holds an enumeration and none can: the names an
        // enumeration accepts are a column the field table does not carry, and
        // RuleFieldTableTests.NoRowDeclaresAValueTypeThisTableCannotCarryTheNamesFor refuses a row
        // that declares the type. The day the table grows that column, the row is what this list
        // comes from.
        var parse = RuleValueParser.Parse(written, at, type, []);

        if (parse.Error is not null)
        {
            errors.Add(Named(entry, parse.Error));
            return null;
        }

        return [parse.Value!];
    }

    private static List<RuleValue>? ReadList(
        RuleConditionOperator entry,
        JsonElement written,
        string at,
        RuleValueType type,
        List<RuleValidationError> errors)
    {
        if (written.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new RuleValidationError(
                at,
                "The operator \"" + entry.Operator.Name
                + "\" is written with a list of values rather than one, as a JSON array."));
            return null;
        }

        // An empty list is refused rather than read as matching nothing. Both readings are
        // defensible - "the field is one of no values" is false for every item, and an operator
        // who wrote it almost certainly meant to fill it in - which is why neither may be chosen
        // quietly, and it is the reading the composition stage takes for an empty group.
        if (written.GetArrayLength() == 0)
        {
            errors.Add(new RuleValidationError(
                at,
                "This list is empty, and a condition comparing the \"" + entry.Field.Name
                + "\" field against no value at all narrows nothing. Write the values it is one of, or remove the condition."));
            return null;
        }

        var values = new List<RuleValue>();
        var refused = false;
        var index = 0;

        // Every member is read even after one is refused, for the reason the stage reports every
        // condition rather than the first: a list of twenty values with three bad ones is one
        // repair when all three are named and three repairs when they arrive one at a time.
        foreach (var member in written.EnumerateArray())
        {
            // Empty for the reason the single-value read gives about its own.
            var parse = RuleValueParser.Parse(
                member,
                at + "/" + index.ToString(CultureInfo.InvariantCulture),
                type,
                []);

            if (parse.Error is null)
            {
                values.Add(parse.Value!);
            }
            else
            {
                errors.Add(Named(entry, parse.Error));
                refused = true;
            }

            index++;
        }

        return refused ? null : values;
    }

    // The parser's sentence with the field named in front of it. The pointer is the parser's,
    // because it is the one that says which of a list's members was refused.
    private static RuleValidationError Named(RuleConditionOperator entry, RuleValidationError error)
        => new(
            error.Pointer,
            "The \"" + entry.Field.Name + "\" field does not hold this value. " + error.Message);

    private static string Shape(RuleOperatorRow @operator)
        => @operator.TakesAList ? "a list of values" : "one value";

    // Derived from the table rather than written out, so an operator that starts taking a list is
    // named here without anybody remembering to come back.
    private static string ListTakingOperatorNames()
    {
        var names = new List<string>();

        foreach (var row in RuleOperatorTable.Rows)
        {
            if (row.TakesAList)
            {
                names.Add(row.Name);
            }
        }

        return string.Join(" and ", names);
    }
}
