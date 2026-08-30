using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What reading a rule's operators produced: either one entry per condition, or every reason it
/// was refused.
/// </summary>
/// <remarks>
/// Never both, which is the shape <see cref="RuleFieldRead"/> holds a field read in and the shape
/// <see cref="RuleCompositionRead"/> uses two stages earlier.
///
/// Every reason rather than the first, for the reason the two stages before it give about
/// themselves: a document names an operator once per condition, so a stage reporting one bad
/// operator per run would make repairing a rule a sequence of edits and re-reads with no way to
/// see how many are left.
/// </remarks>
public sealed class RuleOperatorRead
{
    private RuleOperatorRead(IReadOnlyList<RuleConditionOperator> operators, IReadOnlyList<RuleValidationError> errors)
    {
        Operators = operators;
        Errors = errors;
    }

    /// <summary>
    /// Gets one entry per condition, in the order the stage read them. Empty where the read was
    /// refused.
    /// </summary>
    public IReadOnlyList<RuleConditionOperator> Operators { get; }

    /// <summary>
    /// Gets every reason it was refused, in the order they were found.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether every condition applied an operator its field accepts.
    /// </summary>
    public bool IsAccepted => Errors.Count == 0;

    /// <summary>
    /// Creates the result for a read that passed.
    /// </summary>
    /// <param name="operators">One entry per condition.</param>
    /// <returns>A result carrying the entries and no errors.</returns>
    public static RuleOperatorRead Accepted(IReadOnlyList<RuleConditionOperator> operators)
        => new(operators, []);

    /// <summary>
    /// Creates the result for a read that was refused.
    /// </summary>
    /// <param name="errors">Every reason it was refused. At least one.</param>
    /// <returns>A result carrying the errors and no entries.</returns>
    public static RuleOperatorRead Refused(IReadOnlyList<RuleValidationError> errors)
        => new([], errors);
}
