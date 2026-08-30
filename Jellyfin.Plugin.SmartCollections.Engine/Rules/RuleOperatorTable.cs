using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The closed operator set, one row per operator.
/// </summary>
/// <remarks>
/// The table is the authority for which operators exist, which types of field each one applies to,
/// which types of value each one takes and what each one means. Nothing derives any of that from a
/// framework type, which is what both existing plugins in this space do and is why neither of them
/// can say what their own legal set is.
///
/// Both type columns are properties of the operator rather than of any field. The field's own row
/// declares the type it holds, and the two are compared where a condition is validated; this table
/// answers only the half it owns, and it answers the same way for every field.
///
/// WHICH OF THE TWO ENDS A QUESTION IS ABOUT IS ALWAYS IN ITS NAME HERE.
/// <see cref="AcceptsField"/> and <see cref="RefuseFieldType"/> are asked about the field a
/// condition names; <see cref="AcceptsValue"/> and <see cref="RefuseValueType"/> are asked about
/// the value written beside it. One method answering for both is what let <c>withinLast</c> sit in
/// this table unreachable from every rule.
///
/// The comparisons here are ordinal. An operator name is a wire token rather than a word in a
/// language, so a server's locale cannot decide whether a document names one.
/// </remarks>
public static class RuleOperatorTable
{
    /// <summary>
    /// Every declared type, for the operators that apply to a field whatever it holds.
    /// </summary>
    /// <remarks>
    /// <c>isEmpty</c> and <c>isNotEmpty</c> ask whether the field holds anything, which is a
    /// question every type answers. This list is the field end of those two rows; their value end
    /// is <see cref="NoValue"/>, and the two ends saying different things is the point.
    /// </remarks>
    private static readonly RuleValueType[] EveryType =
    [
        RuleValueType.String,
        RuleValueType.Integer,
        RuleValueType.Decimal,
        RuleValueType.Boolean,
        RuleValueType.Date,
        RuleValueType.Duration,
        RuleValueType.Enumeration
    ];

    /// <summary>
    /// The types an equality holds over, which is every declared type.
    /// </summary>
    /// <remarks>
    /// Equality is defined on all seven because every one of them has one written form and one
    /// parsed value, so two documents that wrote the same thing produce the same value. The list
    /// operators take the same set for the same reason: <c>in</c> is equality against several
    /// values rather than a different comparison.
    /// </remarks>
    private static readonly RuleValueType[] Comparable =
    [
        RuleValueType.String,
        RuleValueType.Integer,
        RuleValueType.Decimal,
        RuleValueType.Boolean,
        RuleValueType.Date,
        RuleValueType.Duration,
        RuleValueType.Enumeration
    ];

    /// <summary>
    /// The types an ordering holds over.
    /// </summary>
    /// <remarks>
    /// Strings are absent on purpose. Ordering text is either culture-sensitive, which this engine
    /// refuses because it would make a rule collect different items on two servers, or ordinal,
    /// which orders by code point and is almost never what somebody writing a rule means by
    /// "greater than". Booleans and enumerations are absent because neither declares an order:
    /// the enumeration's names are a set, and the day one is inserted in the middle every rule
    /// using an ordering over it would change meaning.
    /// </remarks>
    private static readonly RuleValueType[] Ordered =
    [
        RuleValueType.Integer,
        RuleValueType.Decimal,
        RuleValueType.Date,
        RuleValueType.Duration
    ];

    /// <summary>
    /// The types a substring holds over.
    /// </summary>
    private static readonly RuleValueType[] Textual = [RuleValueType.String];

    /// <summary>
    /// The one type an instant is written as.
    /// </summary>
    private static readonly RuleValueType[] Instant = [RuleValueType.Date];

    /// <summary>
    /// The one type a length of time is written as.
    /// </summary>
    private static readonly RuleValueType[] Span = [RuleValueType.Duration];

    /// <summary>
    /// The empty set, for the value end of the two operators that take no value.
    /// </summary>
    private static readonly RuleValueType[] NoValue = [];

    private static readonly RuleOperatorRow[] Table =
    [
        new(RuleOperator.Equals, "equals", Comparable, Comparable, "The field is exactly the value."),
        new(RuleOperator.NotEquals, "notEquals", Comparable, Comparable, "The field is anything other than the value."),
        new(RuleOperator.Contains, "contains", Textual, Textual, "The field holds the value somewhere inside it."),
        new(RuleOperator.NotContains, "notContains", Textual, Textual, "The field holds the value nowhere inside it."),
        new(RuleOperator.StartsWith, "startsWith", Textual, Textual, "The field begins with the value."),
        new(RuleOperator.EndsWith, "endsWith", Textual, Textual, "The field ends with the value."),
        new(RuleOperator.In, "in", Comparable, Comparable, "The field is one of the values in the list."),
        new(RuleOperator.NotIn, "notIn", Comparable, Comparable, "The field is none of the values in the list."),
        new(RuleOperator.GreaterThan, "greaterThan", Ordered, Ordered, "The field is above the value."),
        new(RuleOperator.GreaterThanOrEqual, "greaterThanOrEqual", Ordered, Ordered, "The field is the value or above it."),
        new(RuleOperator.LessThan, "lessThan", Ordered, Ordered, "The field is below the value."),
        new(RuleOperator.LessThanOrEqual, "lessThanOrEqual", Ordered, Ordered, "The field is the value or below it."),
        new(RuleOperator.IsEmpty, "isEmpty", EveryType, NoValue, "The field holds nothing."),
        new(RuleOperator.IsNotEmpty, "isNotEmpty", EveryType, NoValue, "The field holds something."),
        new(RuleOperator.Before, "before", Instant, Instant, "The field is earlier than the value."),
        new(RuleOperator.After, "after", Instant, Instant, "The field is later than the value."),
        new(RuleOperator.WithinLast, "withinLast", Instant, Span, "The field is inside the span that ends at the instant the evaluation was given.")
    ];

    private static readonly Dictionary<string, RuleOperatorRow> ByName = BuildIndex();

    /// <summary>
    /// Gets every row, in the order the table declares them.
    /// </summary>
    public static IReadOnlyList<RuleOperatorRow> Rows => Table;

    /// <summary>
    /// Gets every operator name a document may write, sorted as a refusal lists them.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = SortedNames();

    /// <summary>
    /// Returns the row for an operator.
    /// </summary>
    /// <param name="operator">The operator.</param>
    /// <returns>Its row.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="operator"/> has no row.</exception>
    public static RuleOperatorRow Of(RuleOperator @operator)
    {
        foreach (var row in Table)
        {
            if (row.Operator == @operator)
            {
                return row;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "No row is declared for this operator.");
    }

    /// <summary>
    /// Finds the row a document's operator name refers to.
    /// </summary>
    /// <param name="name">The name as the document wrote it.</param>
    /// <returns>The row, or <see langword="null"/> where no operator has that name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static RuleOperatorRow? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return ByName.TryGetValue(name, out var row) ? row : null;
    }

    /// <summary>
    /// Answers whether an operator applies to a field of a type.
    /// </summary>
    /// <param name="operator">The operator.</param>
    /// <param name="fieldType">The type the field declares.</param>
    /// <returns><see langword="true"/> where the operator applies to such a field.</returns>
    /// <remarks>
    /// The field end, and never the value end. <c>isEmpty</c> applies to a field of every declared
    /// type and takes no value, so this is <see langword="true"/> for every type asked of it while
    /// <see cref="AcceptsValue"/> is <see langword="false"/> for every type asked of it. Those two
    /// answers are not in tension; they are the two ends of one condition.
    /// </remarks>
    public static bool AcceptsField(RuleOperator @operator, RuleValueType fieldType)
    {
        foreach (var accepted in Of(@operator).FieldTypes)
        {
            if (accepted == fieldType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Answers whether an operator takes a value of a type beside it.
    /// </summary>
    /// <param name="operator">The operator.</param>
    /// <param name="valueType">The type the value beside the condition is written as.</param>
    /// <returns><see langword="true"/> where the operator takes such a value.</returns>
    /// <remarks>
    /// An operator that takes no value takes no type, so this is <see langword="false"/> for every
    /// type asked of <c>isEmpty</c> and <c>isNotEmpty</c>. That is the answer rather than a gap: a
    /// condition writing a value beside one of those is asking for something the operator has no
    /// meaning for.
    /// </remarks>
    public static bool AcceptsValue(RuleOperator @operator, RuleValueType valueType)
    {
        foreach (var accepted in Of(@operator).ValueTypes)
        {
            if (accepted == valueType)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The refusal for an operator name no operator has.
    /// </summary>
    /// <param name="name">The name as the document wrote it.</param>
    /// <param name="pointer">Where the name is, as a JSON Pointer.</param>
    /// <returns>The refusal, naming the name and every legal one.</returns>
    /// <remarks>
    /// Every legal name rather than the ones that suit the field's declared type. Narrowing the
    /// list to a field's type is what the done condition on the operator issue asks for and it
    /// needs the field table, which is not in this tree yet; listing all of them is wider than
    /// that and never wrong, and the day the table lands the narrowing is this one call site.
    /// </remarks>
    public static RuleValidationError RefuseUnknownOperator(string name, string pointer)
    {
        ArgumentNullException.ThrowIfNull(name);

        return new RuleValidationError(
            pointer,
            string.Create(
                CultureInfo.InvariantCulture,
                $"There is no operator called \"{name}\". The operators are {string.Join(", ", Names)}."));
    }

    /// <summary>
    /// The refusal for an operator applied to a field of a type it does not apply to.
    /// </summary>
    /// <param name="operator">The operator.</param>
    /// <param name="fieldType">The type the field declares.</param>
    /// <param name="pointer">Where the condition is, as a JSON Pointer.</param>
    /// <returns>The refusal, naming the operator, the field's type and the types it does apply to.</returns>
    /// <exception cref="ArgumentException"><paramref name="operator"/> applies to <paramref name="fieldType"/>.</exception>
    public static RuleValidationError RefuseFieldType(RuleOperator @operator, RuleValueType fieldType, string pointer)
    {
        var row = Of(@operator);

        if (AcceptsField(@operator, fieldType))
        {
            throw new ArgumentException(
                "This operator applies to a field of this type, so there is nothing to refuse. Ask AcceptsField before building a refusal.",
                nameof(@operator));
        }

        // The repair is choosing another operator, so the sentence names what this one does apply
        // to. It says "a field of type" in both halves rather than "type" alone, because the other
        // refusal in this table is about the other end of the same condition and a reader meeting
        // one of them has to be able to tell which end it is about without opening this file.
        return new RuleValidationError(
            pointer,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The operator \"{row.Name}\" does not apply to a field of type {fieldType}. It applies to a field of type {string.Join(", ", row.FieldTypes)}."));
    }

    /// <summary>
    /// The refusal for a value of a type an operator does not take.
    /// </summary>
    /// <param name="operator">The operator.</param>
    /// <param name="valueType">The type the value beside the condition is written as.</param>
    /// <param name="pointer">Where the condition is, as a JSON Pointer.</param>
    /// <returns>The refusal, naming the operator and the value's type.</returns>
    /// <exception cref="ArgumentException"><paramref name="operator"/> takes a value of <paramref name="valueType"/>.</exception>
    public static RuleValidationError RefuseValueType(RuleOperator @operator, RuleValueType valueType, string pointer)
    {
        var row = Of(@operator);

        if (AcceptsValue(@operator, valueType))
        {
            throw new ArgumentException(
                "This operator takes a value of this type, so there is nothing to refuse. Ask AcceptsValue before building a refusal.",
                nameof(@operator));
        }

        // Two messages, because the two failures are different repairs. An operator that takes no
        // value is repaired by deleting the value; one that takes the wrong type is repaired by
        // writing the value in the form the operator takes, and the list of what it does take is
        // what that repair needs.
        var message = row.TakesAValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The operator \"{row.Name}\" does not take a value of type {valueType}. It takes {string.Join(", ", row.ValueTypes)}.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"The operator \"{row.Name}\" takes no value, and this condition writes one of type {valueType}.");

        return new RuleValidationError(pointer, message);
    }

    private static Dictionary<string, RuleOperatorRow> BuildIndex()
    {
        var index = new Dictionary<string, RuleOperatorRow>(StringComparer.Ordinal);

        foreach (var row in Table)
        {
            index.Add(row.Name, row);
        }

        return index;
    }

    private static string[] SortedNames()
    {
        var names = new string[Table.Length];

        for (var i = 0; i < Table.Length; i++)
        {
            names[i] = Table[i].Name;
        }

        Array.Sort(names, StringComparer.Ordinal);

        return names;
    }
}
