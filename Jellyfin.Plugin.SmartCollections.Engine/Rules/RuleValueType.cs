namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// The types a value in a rule document may hold.
/// </summary>
/// <remarks>
/// The set is closed and it is written here rather than derived from whatever a reflected member
/// happens to be. The prior art in this space converts a value with <c>Convert.ChangeType</c>
/// against the type reflection reported for a property, so the legal set is a framework detail,
/// nothing tells the person writing a rule what a field will accept, and a value that converts
/// into something other than what they meant converts silently.
///
/// One type per field in the vocabulary table, and one parser per type in
/// <see cref="RuleValueParser"/>. The parser a value goes through is decided by the field's
/// declared type and never by what the value looks like, so <c>"1"</c> is a string wherever a
/// string is declared and is refused wherever an integer is, on every server and in every locale.
///
/// A member added here is a member that owes a parser and a section in <c>docs/rule-values.md</c>,
/// and both are held by tests rather than by whoever remembers.
/// </remarks>
public enum RuleValueType
{
    /// <summary>
    /// Text, exactly as the document wrote it.
    /// </summary>
    String,

    /// <summary>
    /// A whole number.
    /// </summary>
    Integer,

    /// <summary>
    /// A number that may carry a fractional part.
    /// </summary>
    Decimal,

    /// <summary>
    /// True or false.
    /// </summary>
    Boolean,

    /// <summary>
    /// A point in time, written with an explicit offset or as a date on its own.
    /// </summary>
    Date,

    /// <summary>
    /// A length of time, written in the designators whose length does not depend on when they
    /// are counted from.
    /// </summary>
    Duration,

    /// <summary>
    /// One of a list of names the field declares.
    /// </summary>
    Enumeration
}
