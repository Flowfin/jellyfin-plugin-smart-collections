using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What reading a rule's composition produced: either the tree, or every reason it was refused.
/// </summary>
/// <remarks>
/// Never both, which is the shape <see cref="RuleDocumentValidation"/> holds a document in.
///
/// Every reason rather than the first. A composition is where an operator's typing mistakes
/// collect - an empty group here, a member that is not an object there - and a stage that
/// reported one of them per run would make repairing a document a sequence of edits and re-reads
/// with no way to see how many are left.
/// </remarks>
public sealed class RuleCompositionRead
{
    private RuleCompositionRead(RuleConditionGroup? group, IReadOnlyList<RuleValidationError> errors)
    {
        Group = group;
        Errors = errors;
    }

    /// <summary>
    /// Gets the tree, or <see langword="null"/> where the composition was refused.
    /// </summary>
    public RuleConditionGroup? Group { get; }

    /// <summary>
    /// Gets every reason it was refused, in the order they were found.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether the composition passed.
    /// </summary>
    public bool IsAccepted => Group is not null;

    /// <summary>
    /// Creates the result for a composition that passed.
    /// </summary>
    /// <param name="group">The outermost group.</param>
    /// <returns>A result carrying the tree and no errors.</returns>
    public static RuleCompositionRead Accepted(RuleConditionGroup group)
        => new(group, []);

    /// <summary>
    /// Creates the result for a composition that was refused.
    /// </summary>
    /// <param name="errors">Every reason it was refused. At least one.</param>
    /// <returns>A result carrying the errors and no tree.</returns>
    public static RuleCompositionRead Refused(IReadOnlyList<RuleValidationError> errors)
        => new(null, errors);
}
