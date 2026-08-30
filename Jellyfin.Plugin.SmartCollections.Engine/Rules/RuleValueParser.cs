using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Turns a value in a rule document into a <see cref="RuleValue"/>, once, at validation.
/// </summary>
/// <remarks>
/// One reader per declared type, named after the type it reads, and a test refuses a
/// <see cref="RuleValueType"/> member that has none. Which reader a value goes through is decided
/// by the type the field declares and never by what the value looks like, so a document cannot
/// change what a field means by writing its value differently: <c>"12"</c> reaching an integer
/// field is refused rather than converted, and <c>12</c> reaching a string field is refused
/// rather than rendered.
///
/// That is the whole difference from the prior art in this space, which converts with
/// <c>Convert.ChangeType</c> against a reflected property type and rewrites dates into another
/// representation by mutating the parsed rule in place before evaluating it. A conversion driven
/// by a reflected type is invisible to the person writing the rule, and a conversion that mutates
/// the parsed rule is not idempotent.
///
/// Every reader answers the same way on every server. Numbers are read out of the JSON parser
/// rather than off a string, dates and durations are parsed against explicit formats with
/// <see cref="CultureInfo.InvariantCulture"/>, and every string comparison here is ordinal, so
/// none of the answers moves with the server's locale.
///
/// No reader reads a clock. A relative date is a duration in the document plus the instant the
/// evaluation was given, and both are inputs; there is nothing here that could supply the second
/// one, which is what <c>docs/rule-language.md</c> refuses the wall clock as an implicit input
/// for.
/// </remarks>
public static class RuleValueParser
{
    /// <summary>
    /// The most of a value a refusal quotes back, counted in UTF-16 code units.
    /// </summary>
    /// <remarks>
    /// A refusal is read in a log line and in a form field, and a value that did not parse can be
    /// as long as the document allows. The bound is what keeps one bad value from filling either
    /// surface; it is far above any value a person types and far below a length that turns a
    /// refusal into the payload it was refusing.
    /// </remarks>
    public const int MaximumQuotedLength = 60;

    /// <summary>
    /// How a date is read once it matched a format.
    /// </summary>
    /// <remarks>
    /// <c>AssumeUniversal</c> decides the one case a format leaves open, a date written on its
    /// own, and it is the reason no format accepts a time without an offset: it would silently
    /// answer that case too. <c>AdjustToUniversal</c> then puts every accepted value on one
    /// offset, so two documents writing one instant in two offsets parse to one value, which is
    /// what an instant means. Neither flag reads the server's zone, and neither reads a clock.
    /// </remarks>
    private const DateTimeStyles DateStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    /// <summary>
    /// The designators a duration may carry, in the order they may appear.
    /// </summary>
    /// <remarks>
    /// Weeks and days before the time separator, hours, minutes and seconds after it. Each is a
    /// fixed number of ticks, which is why years and months are not here: their length depends on
    /// when they are counted from, so a rule carrying one would mean a different span in
    /// February than in March and could not be held to an expected output.
    /// </remarks>
    private static readonly (char Designator, bool AfterTheSeparator, long TicksPerUnit)[] DurationUnits =
    [
        ('W', false, TimeSpan.TicksPerDay * 7),
        ('D', false, TimeSpan.TicksPerDay),
        ('H', true, TimeSpan.TicksPerHour),
        ('M', true, TimeSpan.TicksPerMinute),
        ('S', true, TimeSpan.TicksPerSecond)
    ];

    /// <summary>
    /// The forms a date is accepted in.
    /// </summary>
    /// <remarks>
    /// Every one of them either carries an explicit offset or carries no time at all. There is
    /// deliberately no format for a date and time written without an offset: such a value names
    /// an instant only once somebody supplies a zone, the only zone available at that point is
    /// the server's, and a document that means a different instant on two servers is the thing
    /// this plugin exists not to produce.
    /// </remarks>
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"
    ];

    /// <summary>
    /// Parses a value against the type a condition's field and operator settled on.
    /// </summary>
    /// <param name="value">The value as the document wrote it.</param>
    /// <param name="pointer">Where the value is, as a JSON Pointer.</param>
    /// <param name="type">The type the value is parsed against.</param>
    /// <param name="declaredNames">
    /// The names an enumeration field accepts, in the order a refusal lists them. Read only where
    /// <paramref name="type"/> is <see cref="RuleValueType.Enumeration"/>.
    /// </param>
    /// <returns>The value, or the reason it was refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declaredNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a declared type.</exception>
    /// <remarks>
    /// The one place the seven readers are chosen between, so a caller holding a type gets the
    /// reader for it and never a reader for what the value happens to look like. Every caller in
    /// this tree comes through here; the readers stay public because each one is the subject of
    /// its own tests, and a dispatch that hid them would make those tests read through a table
    /// they are not about.
    ///
    /// It is not called <c>Read</c>. <c>RuleValueDocumentTests.EveryDeclaredTypeHasAReaderNamedAfterIt</c>
    /// reads every public static member of this class whose name begins with <c>Read</c> and
    /// requires that set to be exactly one per declared type, which is what catches a type added
    /// without a parser. A dispatch called <c>Read</c> would sit inside that population and weaken
    /// the guard for a naming preference.
    /// </remarks>
    public static RuleValueParse Parse(
        JsonElement value,
        string pointer,
        RuleValueType type,
        IReadOnlyList<string> declaredNames)
    {
        ArgumentNullException.ThrowIfNull(declaredNames);

        return type switch
        {
            RuleValueType.String => ReadString(value, pointer),
            RuleValueType.Integer => ReadInteger(value, pointer),
            RuleValueType.Decimal => ReadDecimal(value, pointer),
            RuleValueType.Boolean => ReadBoolean(value, pointer),
            RuleValueType.Date => ReadDate(value, pointer),
            RuleValueType.Duration => ReadDuration(value, pointer),
            RuleValueType.Enumeration => ReadEnumeration(value, pointer, declaredNames),

            // Unreachable while every declared member is named above, and the suite refuses a
            // member that is not. It is a throw rather than a fallback reader, for the reason
            // RuleValueForm gives about its own: a fallback would let a type added without its
            // reader parse as something it is not.
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No reader is declared for this value type.")
        };
    }

    /// <summary>
    /// Reads a string value.
    /// </summary>
    /// <param name="value">The value as the document wrote it.</param>
    /// <param name="pointer">Where the value is, as a JSON Pointer.</param>
    /// <returns>The value, or the reason it was refused.</returns>
    public static RuleValueParse ReadString(JsonElement value, string pointer)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return Refuse(value, pointer, RuleValueType.String);
        }

        // Not null: the kind is String, and GetString returns null only for JsonValueKind.Null.
        // Nothing is trimmed and nothing is folded: a string is the text the document wrote, and
        // a plugin that quietly trimmed one would answer a question the operator did not ask.
        return RuleValueParse.Accepted(RuleValue.Of(RuleValueType.String, value.GetString()!));
    }

    /// <summary>
    /// Reads an integer value.
    /// </summary>
    /// <param name="value">The value as the document wrote it.</param>
    /// <param name="pointer">Where the value is, as a JSON Pointer.</param>
    /// <returns>The value, or the reason it was refused.</returns>
    public static RuleValueParse ReadInteger(JsonElement value, string pointer)
    {
        // A number written as a string is refused rather than read. Accepting both spellings
        // would make the document's own type system advisory, and the day a field's declared
        // type changes, every value that was written in the other spelling changes meaning.
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var parsed))
        {
            return Refuse(value, pointer, RuleValueType.Integer);
        }

        return RuleValueParse.Accepted(RuleValue.Of(RuleValueType.Integer, parsed));
    }

    /// <summary>
    /// Reads a decimal value.
    /// </summary>
    /// <param name="value">The value as the document wrote it.</param>
    /// <param name="pointer">Where the value is, as a JSON Pointer.</param>
    /// <returns>The value, or the reason it was refused.</returns>
    /// <remarks>
    /// Decimal rather than a binary floating point type, because a rule comparing against a
    /// rating an operator typed should compare against the number they typed. Reading 8.1 into
    /// the nearest double and comparing it for equality is the defect this choice removes, and
    /// the cost is a narrower range, which the accepted form states.
    /// </remarks>
    public static RuleValueParse ReadDecimal(JsonElement value, string pointer)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var parsed))
        {
            return Refuse(value, pointer, RuleValueType.Decimal);
        }

        return RuleValueParse.Accepted(RuleValue.Of(RuleValueType.Decimal, parsed));
    }

    /// <summary>
    /// Reads a boolean value.
    /// </summary>
    /// <param name="value">The value as the document wrote it.</param>
    /// <param name="pointer">Where the value is, as a JSON Pointer.</param>
    /// <returns>The value, or the reason it was refused.</returns>
    public static RuleValueParse ReadBoolean(JsonElement value, string pointer)
    {
        if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
        {
            return Refuse(value, pointer, RuleValueType.Boolean);
        }

        return RuleValueParse.Accepted(
            RuleValue.Of(RuleValueType.Boolean, value.ValueKind == JsonValueKind.True));
    }

    /// <summary>
    /// Reads a date value.
    /// </summary>
    /// <param name="value">The value as the document wrote it.</param>
    /// <param name="pointer">Where the value is, as a JSON Pointer.</param>
    /// <returns>The value, or the reason it was refused.</returns>
    /// <remarks>
    /// A date written on its own is read as the start of that day at offset zero. That is a
    /// choice rather than a reading of anything, and it is the only one available: the
    /// alternative is the server's own zone, which would make the same document mean a different
    /// instant on two servers. What it costs is that an operator on a positive offset writing a
    /// day means an instant a few hours before their own midnight, and the repair for anybody
    /// that matters to is to write the offset.
    /// </remarks>
    public static RuleValueParse ReadDate(JsonElement value, string pointer)
    {
        if (value.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParseExact(
                value.GetString(),
                DateFormats,
                CultureInfo.InvariantCulture,
                DateStyles,
                out var parsed))
        {
            return Refuse(value, pointer, RuleValueType.Date);
        }

        return RuleValueParse.Accepted(RuleValue.Of(RuleValueType.Date, parsed));
    }

    /// <summary>
    /// Reads a duration value.
    /// </summary>
    /// <param name="value">The value as the document wrote it.</param>
    /// <param name="pointer">Where the value is, as a JSON Pointer.</param>
    /// <returns>The value, or the reason it was refused.</returns>
    public static RuleValueParse ReadDuration(JsonElement value, string pointer)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return Refuse(value, pointer, RuleValueType.Duration);
        }

        // Not null: the kind is String.
        var text = value.GetString()!;

        return ReadDurationText(text, value, pointer);
    }

    /// <summary>
    /// Reads an enumeration value against the names a field declares.
    /// </summary>
    /// <param name="value">The value as the document wrote it.</param>
    /// <param name="pointer">Where the value is, as a JSON Pointer.</param>
    /// <param name="declared">The names the field accepts, in the order the refusal lists them.</param>
    /// <returns>The value, or the reason it was refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declared"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="declared"/> is empty.</exception>
    /// <remarks>
    /// The names come from the field's row rather than from a list held here, because which names
    /// an enumeration accepts is a property of the field and not of the type. An empty list is a
    /// fault in the table rather than in the document, so it throws instead of refusing the
    /// operator's value: a refusal listing no legal name tells the operator nothing they can act
    /// on and hides a defect that is not theirs.
    /// </remarks>
    public static RuleValueParse ReadEnumeration(JsonElement value, string pointer, IReadOnlyList<string> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        if (declared.Count == 0)
        {
            throw new ArgumentException(
                "An enumeration field declares at least one name. A field declaring none accepts nothing and could only ever refuse.",
                nameof(declared));
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return RefuseEnumeration(value, pointer, declared);
        }

        // Not null: the kind is String.
        var text = value.GetString()!;

        for (var index = 0; index < declared.Count; index++)
        {
            if (string.Equals(declared[index], text, StringComparison.Ordinal))
            {
                return RuleValueParse.Accepted(RuleValue.Of(RuleValueType.Enumeration, declared[index]));
            }
        }

        return RefuseEnumeration(value, pointer, declared);
    }

    // The duration grammar, walked once. The cursor into DurationUnits is what holds the order
    // and the at-most-once rule together: a designator is looked for at or after the position the
    // last one took, so one written twice and two written out of order are the same refusal and
    // neither needs a set of what has been seen.
    private static RuleValueParse ReadDurationText(string text, JsonElement value, string pointer)
    {
        if (text.Length == 0 || text[0] != 'P')
        {
            return Refuse(value, pointer, RuleValueType.Duration);
        }

        var index = 1;
        var next = 0;
        var afterTheSeparator = false;
        var components = 0;
        var timeComponents = 0;
        long ticks = 0;

        while (index < text.Length)
        {
            if (text[index] == 'T')
            {
                if (afterTheSeparator)
                {
                    return Refuse(value, pointer, RuleValueType.Duration);
                }

                afterTheSeparator = true;
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }

            if (index == start || index == text.Length)
            {
                return Refuse(value, pointer, RuleValueType.Duration);
            }

            var designator = text[index];
            index++;

            // Named before the table is searched, because the table cannot say why they are
            // absent and this is the refusal an operator is most likely to meet.
            if (designator == 'Y' || (designator == 'M' && !afterTheSeparator))
            {
                return RuleValueParse.Refused(new RuleValidationError(
                    pointer,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The value {AsWritten(value)} names years or months, and how long either of those is depends on when it is counted from. A duration is written as {RuleValueForm.Of(RuleValueType.Duration)}.")));
            }

            var slot = IndexOfUnit(designator, next, afterTheSeparator);
            if (slot < 0)
            {
                return Refuse(value, pointer, RuleValueType.Duration);
            }

            next = slot + 1;

            // Weeks do not combine with anything. ISO 8601 makes the week form exclusive, and
            // the reason survives the standard: P1W2D reads as nine days to one person and as a
            // mistake to another, and neither of them is wrong about the text.
            if (designator == 'W' && index != text.Length)
            {
                return Refuse(value, pointer, RuleValueType.Duration);
            }

            if (!long.TryParse(
                    text.AsSpan(start, index - 1 - start),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var amount))
            {
                return Refuse(value, pointer, RuleValueType.Duration);
            }

            try
            {
                checked
                {
                    ticks += amount * DurationUnits[slot].TicksPerUnit;
                }
            }
            catch (OverflowException)
            {
                return Refuse(value, pointer, RuleValueType.Duration);
            }

            components++;
            if (afterTheSeparator)
            {
                timeComponents++;
            }
        }

        // A separator with nothing after it is the second half of a duration somebody stopped
        // writing, and reading it as the first half alone accepts a truncated file.
        if (components == 0 || (afterTheSeparator && timeComponents == 0))
        {
            return Refuse(value, pointer, RuleValueType.Duration);
        }

        return RuleValueParse.Accepted(
            RuleValue.Of(RuleValueType.Duration, TimeSpan.FromTicks(ticks)));
    }

    // The position of a designator at or after `from`, or -1. The separator side is compared as
    // well as the letter, so an M before the separator cannot be read as the minutes after it.
    private static int IndexOfUnit(char designator, int from, bool afterTheSeparator)
    {
        for (var slot = from; slot < DurationUnits.Length; slot++)
        {
            if (DurationUnits[slot].Designator == designator
                && DurationUnits[slot].AfterTheSeparator == afterTheSeparator)
            {
                return slot;
            }
        }

        return -1;
    }

    // One refusal for every way a value can fail its declared type, and it names the two things
    // the operator needs: what they wrote, and what the type accepts. There is deliberately no
    // second sentence describing which of the ways it failed. A value that is a string where a
    // number belongs and a number that is out of range are the same repair - read the form and
    // write the value again - and a message that sorted them would be a switch over failure
    // kinds with an arm no input reaches.
    private static RuleValueParse Refuse(JsonElement value, string pointer, RuleValueType type)
        => RuleValueParse.Refused(new RuleValidationError(
            pointer,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The value {AsWritten(value)} is not {RuleValueForm.Of(type)}.")));

    private static RuleValueParse RefuseEnumeration(JsonElement value, string pointer, IReadOnlyList<string> declared)
        => RuleValueParse.Refused(new RuleValidationError(
            pointer,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The value {AsWritten(value)} is not one of the names this field declares. They are {string.Join(", ", declared)}.")));

    // The value as the document wrote it, which is what an operator is looking at while they read
    // the refusal. Bounded, because a value that did not parse may be as long as the document
    // allows, and cut on a code point rather than in the middle of one: half a surrogate pair
    // renders as a replacement character and would put a character in the message that is in
    // neither the document nor the plugin.
    private static string AsWritten(JsonElement value)
    {
        var raw = value.GetRawText();
        if (raw.Length <= MaximumQuotedLength)
        {
            return raw;
        }

        var length = char.IsHighSurrogate(raw[MaximumQuotedLength - 1])
            ? MaximumQuotedLength - 1
            : MaximumQuotedLength;

        return string.Concat(raw.AsSpan(0, length), "...");
    }
}
