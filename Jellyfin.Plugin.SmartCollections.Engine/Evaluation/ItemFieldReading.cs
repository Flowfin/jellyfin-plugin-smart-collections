using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Evaluation;

/// <summary>
/// One field's value, read off one item.
/// </summary>
/// <remarks>
/// ABSENT AND EMPTY ARE ONE ANSWER HERE, AND THAT IS A DECISION RATHER THAN A CONVENIENCE. The
/// library leaves a field unset by holding a null, by holding an empty string and, for a list, by
/// holding an empty array, and an operator writing <c>officialRating isEmpty</c> means all three.
/// Carrying the three apart would put the choice at every comparison site instead of at this one,
/// which is where a later reader takes two of them and forgets the third.
///
/// Every member but <see cref="Shape"/> and <see cref="IsPresent"/> is meaningful only under its
/// own shape. Reading <see cref="Number"/> off a text reading answers zero rather than throwing,
/// and nothing does: <see cref="ConditionMatcher"/> dispatches on the shape first, which is the
/// property that makes the wrong read unreachable rather than merely wrong.
/// </remarks>
public sealed class ItemFieldReading
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemFieldReading"/> class.
    /// </summary>
    /// <param name="shape">The shape this reading carries.</param>
    /// <param name="isPresent">Whether the library holds a value.</param>
    /// <param name="text">The string, under <see cref="ItemFieldShape.Text"/>.</param>
    /// <param name="textList">The strings, under <see cref="ItemFieldShape.TextList"/>.</param>
    /// <param name="number">The number, under <see cref="ItemFieldShape.Number"/>.</param>
    /// <param name="instant">The instant, under <see cref="ItemFieldShape.Instant"/>.</param>
    /// <param name="span">The length, under <see cref="ItemFieldShape.Span"/>.</param>
    /// <remarks>
    /// Internal rather than private, and the suite is the only caller outside the five factories
    /// below. What it needs it for is the opposite of making a reading: every arm that dispatches
    /// on a shape carries a last one for a shape it has no comparison for, and the only way to
    /// reach that arm is a reading whose shape no factory produces. A guard with no proof that it
    /// bites is refused in this repository, so the constructor is open exactly wide enough to
    /// build the fixture that proves this one does, and no wider - nothing outside this assembly
    /// can build a reading at all.
    /// </remarks>
    internal ItemFieldReading(
        ItemFieldShape shape,
        bool isPresent,
        string? text,
        IReadOnlyList<string> textList,
        decimal number,
        DateTimeOffset instant,
        TimeSpan span)
    {
        Shape = shape;
        IsPresent = isPresent;
        Text = text;
        TextList = textList;
        Number = number;
        Instant = instant;
        Span = span;
    }

    /// <summary>
    /// Gets the shape this reading carries.
    /// </summary>
    public ItemFieldShape Shape { get; }

    /// <summary>
    /// Gets a value indicating whether the library holds a value for this field on this item.
    /// </summary>
    /// <remarks>
    /// False for a null, for a string holding nothing, and for a list holding nothing. That is
    /// what <c>isEmpty</c> answers true for and what every comparing operator answers false for:
    /// a condition comparing a value against something the library does not hold has no answer,
    /// and the item is not collected rather than collected by default.
    /// </remarks>
    public bool IsPresent { get; }

    /// <summary>
    /// Gets the string, under <see cref="ItemFieldShape.Text"/>.
    /// </summary>
    public string? Text { get; }

    /// <summary>
    /// Gets the strings, under <see cref="ItemFieldShape.TextList"/>, in the order the library
    /// holds them.
    /// </summary>
    public IReadOnlyList<string> TextList { get; }

    /// <summary>
    /// Gets the number, under <see cref="ItemFieldShape.Number"/>.
    /// </summary>
    public decimal Number { get; }

    /// <summary>
    /// Gets the instant, under <see cref="ItemFieldShape.Instant"/>.
    /// </summary>
    public DateTimeOffset Instant { get; }

    /// <summary>
    /// Gets the length of time, under <see cref="ItemFieldShape.Span"/>.
    /// </summary>
    public TimeSpan Span { get; }

    /// <summary>
    /// A reading of one string.
    /// </summary>
    /// <param name="text">The string the library holds, or <see langword="null"/>.</param>
    /// <returns>The reading.</returns>
    public static ItemFieldReading OfText(string? text)
        => new(
            ItemFieldShape.Text,
            !string.IsNullOrWhiteSpace(text),
            text,
            [],
            0m,
            default,
            default);

    /// <summary>
    /// A reading of several strings.
    /// </summary>
    /// <param name="values">The strings the library holds, possibly none.</param>
    /// <returns>The reading.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    public static ItemFieldReading OfTextList(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return new(ItemFieldShape.TextList, values.Count > 0, null, values, 0m, default, default);
    }

    /// <summary>
    /// A reading of one number.
    /// </summary>
    /// <param name="value">The number the library holds, or <see langword="null"/>.</param>
    /// <returns>The reading.</returns>
    public static ItemFieldReading OfNumber(decimal? value)
        => new(
            ItemFieldShape.Number,
            value.HasValue,
            null,
            [],
            value ?? 0m,
            default,
            default);

    /// <summary>
    /// A reading of one instant.
    /// </summary>
    /// <param name="value">The instant the library holds, or <see langword="null"/>.</param>
    /// <returns>The reading.</returns>
    public static ItemFieldReading OfInstant(DateTimeOffset? value)
        => new(
            ItemFieldShape.Instant,
            value.HasValue,
            null,
            [],
            0m,
            value ?? default,
            default);

    /// <summary>
    /// A reading of one length of time.
    /// </summary>
    /// <param name="value">The length the library holds, or <see langword="null"/>.</param>
    /// <returns>The reading.</returns>
    public static ItemFieldReading OfSpan(TimeSpan? value)
        => new(
            ItemFieldShape.Span,
            value.HasValue,
            null,
            [],
            0m,
            default,
            value ?? default);
}
