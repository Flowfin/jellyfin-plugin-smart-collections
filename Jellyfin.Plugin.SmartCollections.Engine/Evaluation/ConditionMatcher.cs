using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.SmartCollections.Evaluation;

/// <summary>
/// Answers whether one item satisfies one condition, over what the query already returned.
/// </summary>
/// <remarks>
/// THE COMPARISON IS ORDINAL AND CASE-INSENSITIVE, EVERYWHERE, AND IT IS READ FROM #25 RATHER THAN
/// CHOSEN HERE. That issue declares the default for rule matching in those words, and a per
/// condition case-sensitivity flag as something a document may later set; this vocabulary declares
/// no such member, so every comparison below names the one comparison and there is nothing for a
/// document to vary. Naming it at every site rather than at one is what
/// <c>culture-sensitive-string-comparison</c> refuses the absence of: a comparison that reads the
/// server's culture makes the same rule collect one set in one locale and another set in the next.
///
/// A VALUE THE LIBRARY DOES NOT HOLD SATISFIES NO COMPARISON, POSITIVE OR NEGATIVE, and that is a
/// decision rather than a fallthrough. An item with no age classification does not satisfy
/// <c>officialRating equals PG</c>, and it does not satisfy <c>officialRating notEquals PG</c>
/// either: both compare against something that is not there, and the reading that answers true for
/// the second is the reading on which a rule collects items nobody described. What an operator
/// writing about absence has is <c>isEmpty</c> and <c>isNotEmpty</c>, which are the two operators
/// here that answer from <see cref="ItemFieldReading.IsPresent"/> alone and the only two that ever
/// answer true for an absent value. The opposite reading is defensible and is not the one taken;
/// re-taking it is a change to what a document means and belongs on an issue rather than in this
/// file.
///
/// WHAT THIS DOES NOT RE-DECIDE IS A CONDITION THE QUERY ALREADY ANSWERED. The step that calls
/// this hands it only the conditions the compiler could not push, because the server compares its
/// own cleaned form of a name, a genre and a tag, and re-comparing one here would apply a
/// different comparison to an item the server already selected. That boundary is the caller's and
/// is argued at <see cref="RuleEvaluator"/>.
/// </remarks>
public static class ConditionMatcher
{
    /// <summary>
    /// The comparison every text comparison here names.
    /// </summary>
    private const StringComparison Comparison = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Answers whether an item satisfies a condition.
    /// </summary>
    /// <param name="item">The item, as the server answered with it.</param>
    /// <param name="condition">The condition, with its values already parsed.</param>
    /// <param name="evaluatedAt">The instant the evaluation was given.</param>
    /// <returns><see langword="true"/> where the item satisfies it.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="item"/> or <paramref name="condition"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The condition names a pair no arm here answers. The vocabulary tables refuse such a pair
    /// before an evaluation can reach this, so it is a fault in a table rather than in a document.
    /// </exception>
    public static bool Matches(BaseItem item, RuleConditionValue condition, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(condition);

        var reading = ItemFieldReader.Read(item, condition.Field.Field);

        return Compare(reading, condition.Operator.Operator, condition.Values, evaluatedAt);
    }

    /// <summary>
    /// Answers whether a value read off an item satisfies an operator and the values beside it.
    /// </summary>
    /// <param name="reading">The value, as it was read off the item.</param>
    /// <param name="operator">The operator.</param>
    /// <param name="values">The values the document wrote.</param>
    /// <param name="evaluatedAt">The instant the evaluation was given.</param>
    /// <returns><see langword="true"/> where the reading satisfies the operator.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="reading"/> or <paramref name="values"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// No arm compares a reading of that shape, or none compares it with that operator.
    /// </exception>
    /// <remarks>
    /// Internal rather than private, and split off the entry point above rather than inlined into
    /// it, so the last arm of the shape dispatch has a caller that can reach it. The entry point
    /// builds its reading through <see cref="ItemFieldReader"/>, which produces one of five
    /// shapes, so through that route the arm for a sixth is unreachable and its guard would ship
    /// unproven. A shape this assembly does not produce is what the suite hands this method, and
    /// the reading's own constructor is open exactly wide enough for that fixture.
    /// </remarks>
    internal static bool Compare(
        ItemFieldReading reading,
        RuleOperator @operator,
        IReadOnlyList<RuleValue> values,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(values);

        if (@operator == RuleOperator.IsEmpty)
        {
            return !reading.IsPresent;
        }

        if (@operator == RuleOperator.IsNotEmpty)
        {
            return reading.IsPresent;
        }

        if (!reading.IsPresent)
        {
            return false;
        }

        return reading.Shape switch
        {
            ItemFieldShape.Text => CompareText(reading.Text!, @operator, values),
            ItemFieldShape.TextList => CompareTextList(reading.TextList, @operator, values),
            ItemFieldShape.Number => CompareNumber(reading.Number, @operator, values),
            ItemFieldShape.Instant => CompareInstant(reading.Instant, @operator, values, evaluatedAt),
            ItemFieldShape.Span => CompareSpan(reading.Span, @operator, values),
            _ => throw new ArgumentOutOfRangeException(
                nameof(reading),
                reading.Shape,
                "No arm compares a value of this shape.")
        };
    }

    /// <summary>
    /// Compares one string the library holds.
    /// </summary>
    /// <param name="text">The string.</param>
    /// <param name="operator">The operator.</param>
    /// <param name="values">The values the document wrote.</param>
    /// <returns>The answer.</returns>
    private static bool CompareText(string text, RuleOperator @operator, IReadOnlyList<RuleValue> values)
        => @operator switch
        {
            RuleOperator.Equals => string.Equals(text, Text(values[0]), Comparison),
            RuleOperator.NotEquals => !string.Equals(text, Text(values[0]), Comparison),
            RuleOperator.Contains => text.Contains(Text(values[0]), Comparison),
            RuleOperator.NotContains => !text.Contains(Text(values[0]), Comparison),
            RuleOperator.StartsWith => text.StartsWith(Text(values[0]), Comparison),
            RuleOperator.EndsWith => text.EndsWith(Text(values[0]), Comparison),
            RuleOperator.In => AnyEquals(text, values),
            RuleOperator.NotIn => !AnyEquals(text, values),
            _ => throw Unanswered(@operator, ItemFieldShape.Text)
        };

    /// <summary>
    /// Compares the strings the library holds against a value, by membership rather than by
    /// substring, which is what the field table's own remark says <c>contains</c> means over a
    /// list.
    /// </summary>
    /// <param name="values">The strings the library holds.</param>
    /// <param name="operator">The operator.</param>
    /// <param name="written">The values the document wrote.</param>
    /// <returns>The answer.</returns>
    private static bool CompareTextList(
        IReadOnlyList<string> values,
        RuleOperator @operator,
        IReadOnlyList<RuleValue> written)
        => @operator switch
        {
            RuleOperator.Contains => Holds(values, Text(written[0])),
            RuleOperator.NotContains => !Holds(values, Text(written[0])),
            _ => throw Unanswered(@operator, ItemFieldShape.TextList)
        };

    /// <summary>
    /// Compares one number the library holds.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <param name="operator">The operator.</param>
    /// <param name="values">The values the document wrote.</param>
    /// <returns>The answer.</returns>
    private static bool CompareNumber(decimal number, RuleOperator @operator, IReadOnlyList<RuleValue> values)
        => @operator switch
        {
            RuleOperator.Equals => number == Number(values[0]),
            RuleOperator.NotEquals => number != Number(values[0]),
            RuleOperator.GreaterThan => number > Number(values[0]),
            RuleOperator.GreaterThanOrEqual => number >= Number(values[0]),
            RuleOperator.LessThan => number < Number(values[0]),
            RuleOperator.LessThanOrEqual => number <= Number(values[0]),
            RuleOperator.In => AnyNumberEquals(number, values),
            RuleOperator.NotIn => !AnyNumberEquals(number, values),
            _ => throw Unanswered(@operator, ItemFieldShape.Number)
        };

    /// <summary>
    /// Compares one instant the library holds.
    /// </summary>
    /// <param name="instant">The instant.</param>
    /// <param name="operator">The operator.</param>
    /// <param name="values">The values the document wrote.</param>
    /// <param name="evaluatedAt">The instant the evaluation was given.</param>
    /// <returns>The answer.</returns>
    /// <remarks>
    /// The three answers are the ones <see cref="RuleQueryTable"/> writes into the server's own
    /// query for the pairs it carries: <c>after</c> and <c>before</c> are strict, and the span
    /// <c>withinLast</c> names is closed at both ends and ends at the instant the evaluation was
    /// given. They agree on purpose. One sentence compiled into the query on one document and
    /// compared here on another - which is what a disjunction produces - would otherwise collect
    /// two different sets.
    /// </remarks>
    private static bool CompareInstant(
        DateTimeOffset instant,
        RuleOperator @operator,
        IReadOnlyList<RuleValue> values,
        DateTimeOffset evaluatedAt)
        => @operator switch
        {
            RuleOperator.Before => instant < Moment(values[0]),
            RuleOperator.After => instant > Moment(values[0]),
            RuleOperator.WithinLast => instant <= evaluatedAt
                                       && evaluatedAt.UtcTicks - instant.UtcTicks <= Length(values[0]).Ticks,
            _ => throw Unanswered(@operator, ItemFieldShape.Instant)
        };

    /// <summary>
    /// Compares one length of time the library holds.
    /// </summary>
    /// <param name="span">The length.</param>
    /// <param name="operator">The operator.</param>
    /// <param name="values">The values the document wrote.</param>
    /// <returns>The answer.</returns>
    private static bool CompareSpan(TimeSpan span, RuleOperator @operator, IReadOnlyList<RuleValue> values)
        => @operator switch
        {
            RuleOperator.GreaterThan => span > Length(values[0]),
            RuleOperator.GreaterThanOrEqual => span >= Length(values[0]),
            RuleOperator.LessThan => span < Length(values[0]),
            RuleOperator.LessThanOrEqual => span <= Length(values[0]),
            _ => throw Unanswered(@operator, ItemFieldShape.Span)
        };

    /// <summary>
    /// Whether any value the document wrote is the string the library holds.
    /// </summary>
    /// <param name="text">The string the library holds.</param>
    /// <param name="values">The values the document wrote.</param>
    /// <returns>The answer.</returns>
    private static bool AnyEquals(string text, IReadOnlyList<RuleValue> values)
    {
        foreach (var value in values)
        {
            if (string.Equals(text, Text(value), Comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether any value the document wrote is the number the library holds.
    /// </summary>
    /// <param name="number">The number the library holds.</param>
    /// <param name="values">The values the document wrote.</param>
    /// <returns>The answer.</returns>
    private static bool AnyNumberEquals(decimal number, IReadOnlyList<RuleValue> values)
    {
        foreach (var value in values)
        {
            if (number == Number(value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the strings the library holds include one.
    /// </summary>
    /// <param name="values">The strings the library holds.</param>
    /// <param name="wanted">The string the document wrote.</param>
    /// <returns>The answer.</returns>
    private static bool Holds(IReadOnlyList<string> values, string wanted)
    {
        foreach (var value in values)
        {
            if (string.Equals(value, wanted, Comparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The string a parsed value carries.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The string.</returns>
    private static string Text(RuleValue value) => (string)value.Value;

    /// <summary>
    /// The number a parsed value carries, whichever of the two numeric types it declared.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The number.</returns>
    private static decimal Number(RuleValue value)
        => value.Type == RuleValueType.Integer ? (long)value.Value : (decimal)value.Value;

    /// <summary>
    /// The instant a parsed value carries.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The instant.</returns>
    private static DateTimeOffset Moment(RuleValue value) => (DateTimeOffset)value.Value;

    /// <summary>
    /// The length of time a parsed value carries.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The length.</returns>
    private static TimeSpan Length(RuleValue value) => (TimeSpan)value.Value;

    /// <summary>
    /// The fault a pair with no arm here is, which is a table's rather than a document's.
    /// </summary>
    /// <param name="operator">The operator.</param>
    /// <param name="shape">The shape the field takes on an item.</param>
    /// <returns>The exception to throw.</returns>
    private static ArgumentOutOfRangeException Unanswered(RuleOperator @operator, ItemFieldShape shape)
        => new(
            nameof(@operator),
            @operator,
            "No arm compares a field of shape " + shape
            + " with this operator. The field table declares which operators a field accepts, so a "
            + "pair reaching this is a row that gained an operator without an arm here.");
}
