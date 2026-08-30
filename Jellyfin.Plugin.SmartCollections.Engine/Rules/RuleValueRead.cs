using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What reading a rule's values produced: either one entry per condition, or every reason it was
/// refused.
/// </summary>
/// <remarks>
/// Never both, which is the shape <see cref="RuleOperatorRead"/> holds an operator read in and the
/// shape <see cref="RuleFieldRead"/> and <see cref="RuleCompositionRead"/> use before it.
///
/// Every reason rather than the first, for the reason the three stages before it give about
/// themselves. This stage has more to report than any of them - a document writes one value per
/// condition and <c>in</c> writes as many as the operator listed - so a stage stopping at the
/// first would make repairing a rule a sequence of edits and re-reads with no way to see how many
/// are left.
/// </remarks>
public sealed class RuleValueRead
{
    private RuleValueRead(IReadOnlyList<RuleConditionValue> conditions, IReadOnlyList<RuleValidationError> errors)
    {
        Conditions = conditions;
        Errors = errors;
    }

    /// <summary>
    /// Gets one entry per condition, in the order the stage read them. Empty where the read was
    /// refused.
    /// </summary>
    public IReadOnlyList<RuleConditionValue> Conditions { get; }

    /// <summary>
    /// Gets every reason it was refused, in the order they were found.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether every condition wrote a value its field and operator take.
    /// </summary>
    public bool IsAccepted => Errors.Count == 0;

    /// <summary>
    /// Creates the result for a read that passed.
    /// </summary>
    /// <param name="conditions">One entry per condition.</param>
    /// <returns>A result carrying the entries and no errors.</returns>
    public static RuleValueRead Accepted(IReadOnlyList<RuleConditionValue> conditions)
        => new(conditions, []);

    /// <summary>
    /// Creates the result for a read that was refused.
    /// </summary>
    /// <param name="errors">Every reason it was refused. At least one.</param>
    /// <returns>A result carrying the errors and no entries.</returns>
    public static RuleValueRead Refused(IReadOnlyList<RuleValidationError> errors)
        => new([], errors);
}
