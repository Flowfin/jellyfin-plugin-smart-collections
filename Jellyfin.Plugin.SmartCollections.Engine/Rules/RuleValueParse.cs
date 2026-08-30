namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What parsing one value produced: either the value, or the reason it was refused.
/// </summary>
/// <remarks>
/// Never both, which is the same shape <see cref="RuleDocumentValidation"/> holds one document
/// in and for the same reason: a caller holding a value holds one that passed, so there is no
/// path on which an unparsed value reaches a query.
///
/// One error rather than a list. A document collects every reason it was refused, because an
/// operator fixing a file wants all of them at once; one value has one reason, and a parser that
/// returned a list of them would be inviting a caller to decide which one to show.
/// </remarks>
public sealed class RuleValueParse
{
    private RuleValueParse(RuleValue? value, RuleValidationError? error)
    {
        Value = value;
        Error = error;
    }

    /// <summary>
    /// Gets the value, or <see langword="null"/> where it was refused.
    /// </summary>
    public RuleValue? Value { get; }

    /// <summary>
    /// Gets the reason it was refused, or <see langword="null"/> where it was not.
    /// </summary>
    public RuleValidationError? Error { get; }

    /// <summary>
    /// Gets a value indicating whether the value parsed.
    /// </summary>
    public bool IsAccepted => Value is not null;

    /// <summary>
    /// Creates the result for a value that parsed.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>A result carrying the value and no error.</returns>
    public static RuleValueParse Accepted(RuleValue value)
        => new(value, null);

    /// <summary>
    /// Creates the result for a value that was refused.
    /// </summary>
    /// <param name="error">The reason it was refused.</param>
    /// <returns>A result carrying the error and no value.</returns>
    public static RuleValueParse Refused(RuleValidationError error)
        => new(null, error);
}
