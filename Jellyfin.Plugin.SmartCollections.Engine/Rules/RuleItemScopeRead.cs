using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What reading a rule's item scope produced: either the kinds it collects, or every reason it was
/// refused.
/// </summary>
/// <remarks>
/// Never both, which is the shape <see cref="RuleDocumentValidation"/> holds a document in and the
/// shape <see cref="RuleFieldRead"/> and <see cref="RuleCompositionRead"/> already use.
///
/// The kinds arrive in the table's order rather than the document's, so two documents naming one
/// set in two orders produce the same scope and compile to the same query. A scope is a set, and
/// carrying the document's order would make it look like something a rule can decide.
/// </remarks>
public sealed class RuleItemScopeRead
{
    private RuleItemScopeRead(IReadOnlyList<RuleItemKindRow> kinds, IReadOnlyList<RuleValidationError> errors)
    {
        Kinds = kinds;
        Errors = errors;
    }

    /// <summary>
    /// Gets the kinds the rule collects, in the order the table declares them. Empty where the
    /// read was refused.
    /// </summary>
    public IReadOnlyList<RuleItemKindRow> Kinds { get; }

    /// <summary>
    /// Gets every reason it was refused, in the order they were found.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether the document declared a scope this plugin accepts.
    /// </summary>
    public bool IsAccepted => Errors.Count == 0;

    /// <summary>
    /// Creates the result for a read that passed.
    /// </summary>
    /// <param name="kinds">The kinds the rule collects. At least one.</param>
    /// <returns>A result carrying the kinds and no errors.</returns>
    public static RuleItemScopeRead Accepted(IReadOnlyList<RuleItemKindRow> kinds)
        => new(kinds, []);

    /// <summary>
    /// Creates the result for a read that was refused.
    /// </summary>
    /// <param name="errors">Every reason it was refused. At least one.</param>
    /// <returns>A result carrying the errors and no kinds.</returns>
    public static RuleItemScopeRead Refused(IReadOnlyList<RuleValidationError> errors)
        => new([], errors);
}
