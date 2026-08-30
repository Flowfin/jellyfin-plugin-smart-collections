using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What reading a rule's fields produced: either one row per condition, or every reason it was
/// refused.
/// </summary>
/// <remarks>
/// Never both, which is the shape <see cref="RuleDocumentValidation"/> holds a document in and
/// the shape <see cref="RuleCompositionRead"/> already uses one stage earlier.
///
/// Every reason rather than the first, for the reason the composition stage gives about itself: a
/// document names a field once per condition, so a stage reporting one mistyped field per run
/// would make repairing a rule a sequence of edits and re-reads with no way to see how many are
/// left.
/// </remarks>
public sealed class RuleFieldRead
{
    private RuleFieldRead(IReadOnlyList<RuleConditionField> fields, IReadOnlyList<RuleValidationError> errors)
    {
        Fields = fields;
        Errors = errors;
    }

    /// <summary>
    /// Gets one entry per condition, in the order the stage read them. Empty where the read was
    /// refused.
    /// </summary>
    public IReadOnlyList<RuleConditionField> Fields { get; }

    /// <summary>
    /// Gets every reason it was refused, in the order they were found.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether every condition named a declared field.
    /// </summary>
    public bool IsAccepted => Errors.Count == 0;

    /// <summary>
    /// Creates the result for a read that passed.
    /// </summary>
    /// <param name="fields">One entry per condition.</param>
    /// <returns>A result carrying the entries and no errors.</returns>
    public static RuleFieldRead Accepted(IReadOnlyList<RuleConditionField> fields)
        => new(fields, []);

    /// <summary>
    /// Creates the result for a read that was refused.
    /// </summary>
    /// <param name="errors">Every reason it was refused. At least one.</param>
    /// <returns>A result carrying the errors and no entries.</returns>
    public static RuleFieldRead Refused(IReadOnlyList<RuleValidationError> errors)
        => new([], errors);
}
