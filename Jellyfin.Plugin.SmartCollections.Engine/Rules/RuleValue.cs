using System;
using System.Globalization;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// A value from a rule document, after the one parse it gets.
/// </summary>
/// <remarks>
/// The declared type and the parsed payload travel together, because a payload on its own is a
/// boxed number that any later reader would have to guess the meaning of. The prior art in this
/// space parses a second time at evaluation and rewrites dates into another representation by
/// mutating the parsed rule in place, so the same document means one thing before the rewrite and
/// another after it, and running the rewrite twice is not the same as running it once. Nothing
/// downstream of this type parses anything: it is constructed by <see cref="RuleValueParser"/>
/// and it is read.
///
/// Every member is get-only and there is no setter of any kind, <c>init</c> included. This is a
/// class rather than a record for exactly that: a positional record's members carry an
/// <c>init</c> accessor, which is a setter that a <c>with</c> expression calls, and a value that
/// can be rebuilt with one member replaced is a value a later stage can change its mind about
/// after validation has passed it.
///
/// <see cref="Value"/> is the payload as a CLR object, and <see cref="Type"/> fixes what is in
/// it: <see cref="string"/> for <see cref="RuleValueType.String"/> and
/// <see cref="RuleValueType.Enumeration"/>, <see cref="long"/> for
/// <see cref="RuleValueType.Integer"/>, <see cref="decimal"/> for
/// <see cref="RuleValueType.Decimal"/>, <see cref="bool"/> for
/// <see cref="RuleValueType.Boolean"/>, <see cref="DateTimeOffset"/> for
/// <see cref="RuleValueType.Date"/> and <see cref="TimeSpan"/> for
/// <see cref="RuleValueType.Duration"/>. That pairing is asserted by the parser's own tests
/// rather than left as a sentence here, because a sentence is what the prior art also had.
/// </remarks>
public sealed class RuleValue
{
    private RuleValue(RuleValueType type, object value)
    {
        Type = type;
        Value = value;
    }

    /// <summary>
    /// Gets the type the field declared, which decides what <see cref="Value"/> holds.
    /// </summary>
    public RuleValueType Type { get; }

    /// <summary>
    /// Gets the parsed payload.
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// Creates a value of a declared type.
    /// </summary>
    /// <param name="type">The type the field declared.</param>
    /// <param name="value">The payload, of the CLR type that pairs with <paramref name="type"/>.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public static RuleValue Of(RuleValueType type, object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new RuleValue(type, value);
    }

    /// <summary>
    /// Returns the type and the payload, for a log line and for a failing assertion.
    /// </summary>
    /// <returns>The type and the payload.</returns>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Type}: {Value}");
}
