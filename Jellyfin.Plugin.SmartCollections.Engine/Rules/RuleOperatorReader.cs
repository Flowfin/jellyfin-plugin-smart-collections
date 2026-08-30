using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Reads the operator each condition of a rule applies, and refuses one the field it was written
/// against does not accept.
/// </summary>
/// <remarks>
/// This stage reads the <c>operator</c> member of a condition and nothing else. What the value
/// beside it says is the next stage's business over the same text, exactly as
/// <see cref="RuleFieldReader"/> reads the field and never the operator and the composition stage
/// reads the shape and never a condition. Keeping them apart is what lets a document with an
/// unknown operator be told from one whose field is wrong, from one whose groups are wrong and
/// from one whose value will not parse, which are four different repairs.
///
/// It is handed what the field stage produced rather than the composition tree, because every
/// refusal it can build needs the field's row: the list an unknown name is refused with, the type
/// an inapplicable operator is refused against, and the narrower question of whether this
/// particular field offers the comparison at all. A stage that resolved the field itself would
/// give one condition two chances to resolve differently.
///
/// TWO REFUSALS SIT BETWEEN A DECLARED OPERATOR AND AN ACCEPTED ONE, AND THEY ARE DIFFERENT
/// STATEMENTS. The operator table is asked first, and it answers whether the operator applies to a
/// field of this type at all - <c>productionYear contains 5</c> fails there, because a substring
/// test is not defined over a whole number. The field's own row is asked second, and it answers
/// whether this particular field offers the comparison - <c>genres startsWith "Thr"</c> fails
/// there, because the operator is defined over strings and a list of genres is not a string a
/// prefix test means anything against. Collapsing the two would tell somebody repairing the second
/// document that the operator does not work on text, which is not true and sends them the wrong
/// way.
/// </remarks>
public static class RuleOperatorReader
{
    /// <summary>
    /// The member of a condition that names the operator.
    /// </summary>
    public const string OperatorMember = "operator";

    /// <summary>
    /// Reads the operator each condition applies and resolves it against that condition's field.
    /// </summary>
    /// <param name="document">The document the conditions were read from.</param>
    /// <param name="fields">One entry per condition, as the field stage produced them.</param>
    /// <returns>One entry per condition, or every reason the read was refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fields"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A condition pointer in <paramref name="fields"/> refers to nothing in
    /// <paramref name="document"/>, which means the field read and the document are not the same
    /// read.
    /// </exception>
    public static RuleOperatorRead Read(JsonElement document, IReadOnlyList<RuleConditionField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var operators = new List<RuleConditionOperator>();
        var errors = new List<RuleValidationError>();

        foreach (var field in fields)
        {
            ReadCondition(document, field, operators, errors);
        }

        return errors.Count > 0 ? RuleOperatorRead.Refused(errors) : RuleOperatorRead.Accepted(operators);
    }

    private static void ReadCondition(
        JsonElement document,
        RuleConditionField field,
        List<RuleConditionOperator> operators,
        List<RuleValidationError> errors)
    {
        var condition = RuleFieldReader.Resolve(document, field.Pointer)
            ?? throw new ArgumentException(
                "The condition at " + field.Pointer
                + " is not in this document, so the field read and the document are not the same read.",
                nameof(document));

        if (!condition.TryGetProperty(OperatorMember, out var written))
        {
            errors.Add(new RuleValidationError(
                field.Pointer,
                "This condition applies no operator. A condition carries an \"" + OperatorMember
                + "\" member, and the operators for a \"" + field.Row.Name + "\" field are "
                + RuleFieldTable.OperatorNames(field.Row) + "."));
            return;
        }

        var at = field.Pointer + "/" + OperatorMember;

        if (written.ValueKind != JsonValueKind.String)
        {
            // Refused rather than read through ToString, for the reason the field stage gives about
            // its own member: an operator is a name from a declared list, so a number or an object
            // there is somebody writing something else in the place a name goes.
            errors.Add(new RuleValidationError(
                at,
                "An operator is written as a string naming one of "
                + RuleFieldTable.OperatorNames(field.Row) + "."));
            return;
        }

        var name = written.GetString()!;
        var row = RuleOperatorTable.Find(name);

        if (row is null)
        {
            errors.Add(RuleFieldTable.RefuseUnknownOperator(field.Row, name, at));
            return;
        }

        if (!RuleOperatorTable.AcceptsField(row.Operator, field.Row.ValueType))
        {
            errors.Add(RuleOperatorTable.RefuseFieldType(row.Operator, field.Row.ValueType, at));
            return;
        }

        if (!RuleFieldTable.Accepts(field.Row.Field, row.Operator))
        {
            errors.Add(RuleFieldTable.RefuseOperator(field.Row, row.Operator, at));
            return;
        }

        operators.Add(new RuleConditionOperator(field.Pointer, field.Row, row));
    }
}
