using System;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The written form each value type accepts, in the words a refusal uses.
/// </summary>
/// <remarks>
/// One sentence per type, held in one place. A refusal that described the accepted form in its
/// own words would describe it slightly differently at every site, and the reference page would
/// then be a third wording nobody could hold either of the first two to. The message a person
/// reads and the row the documentation carries are the same string, and
/// <c>RuleValueDocumentTests</c> refuses a page that has stopped saying what this table says.
///
/// The sentences name a form and never an example. An example in a refusal is read as the only
/// accepted spelling, and the day a second one is accepted the example is what has to be found
/// and changed. Examples live on the page, where a reader is looking for one.
/// </remarks>
public static class RuleValueForm
{
    /// <summary>
    /// Returns the accepted written form of a value type.
    /// </summary>
    /// <param name="type">The type the field declared.</param>
    /// <returns>The accepted form, as one sentence with no trailing stop.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a declared type.</exception>
    public static string Of(RuleValueType type) => type switch
    {
        RuleValueType.String => "a JSON string",
        RuleValueType.Integer => "a JSON number with no fractional part, between -9223372036854775808 and 9223372036854775807",
        RuleValueType.Decimal => "a JSON number between -79228162514264337593543950335 and 79228162514264337593543950335",
        RuleValueType.Boolean => "the JSON literal true or the JSON literal false",
        RuleValueType.Date => "a JSON string holding an ISO 8601 date with an explicit offset, or an ISO 8601 date on its own",
        RuleValueType.Duration => "a JSON string holding an ISO 8601 duration written in whole weeks, or in whole days, hours, minutes and seconds",
        RuleValueType.Enumeration => "a JSON string holding one of the names the field declares",

        // Unreachable while every declared member is named above, and the suite refuses a member
        // that is not. It is a throw rather than a fallback sentence, because a fallback would
        // let a type added without its form ship a refusal that describes nothing.
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No written form is declared for this value type.")
    };
}
