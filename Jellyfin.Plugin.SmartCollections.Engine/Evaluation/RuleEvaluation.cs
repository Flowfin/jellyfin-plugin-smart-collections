using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SmartCollections.Rules;

namespace Jellyfin.Plugin.SmartCollections.Evaluation;

/// <summary>
/// What running a rule produced: the ordered identifiers of the items it collects, or the reasons
/// the rule could not be run at all.
/// </summary>
/// <remarks>
/// Never both, which is the shape <see cref="RuleDocumentValidation"/> and
/// <see cref="RuleQueryCompilation"/> already have. A caller holding an accepted evaluation holds
/// a list it can write onto a collection, and there is no path on which a refused rule hands one
/// out.
///
/// <see cref="EvaluatedAt"/> IS CARRIED ON BOTH ANSWERS AND IS THE INSTANT THE CALLER GAVE. A
/// refusal is a thing that happened at a moment as much as an acceptance is, and a report about a
/// collection is only reproducible if the instant its rule was compiled at travels with the
/// result. Nothing here reads a clock to fill it in: it is the argument the evaluation was handed,
/// carried through, which is what <c>ambient-clock-in-the-engine</c> refuses rather than trusts.
///
/// The identifiers are the answer rather than the items. What a refresh acts on is a set of
/// identifiers, holding a list of items alive past the evaluation keeps the server's own objects
/// in this plugin's hands for no reason, and an identifier is the one thing about an item that
/// does not change under it.
/// </remarks>
public sealed class RuleEvaluation
{
    private RuleEvaluation(
        IReadOnlyList<Guid> itemIds,
        DateTimeOffset evaluatedAt,
        IReadOnlyList<RuleValidationError> errors)
    {
        ItemIds = itemIds;
        EvaluatedAt = evaluatedAt;
        Errors = errors;
    }

    /// <summary>
    /// Gets the identifiers of the items the rule collects, ordered, empty where it was refused.
    /// </summary>
    public IReadOnlyList<Guid> ItemIds { get; }

    /// <summary>
    /// Gets the instant the evaluation was given.
    /// </summary>
    public DateTimeOffset EvaluatedAt { get; }

    /// <summary>
    /// Gets the reasons the rule was refused, empty where it ran.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether the rule ran.
    /// </summary>
    public bool IsAccepted => Errors.Count == 0;

    /// <summary>
    /// An evaluation that produced a list.
    /// </summary>
    /// <param name="itemIds">The identifiers, ordered.</param>
    /// <param name="evaluatedAt">The instant the evaluation was given.</param>
    /// <returns>The evaluation.</returns>
    public static RuleEvaluation Accepted(IReadOnlyList<Guid> itemIds, DateTimeOffset evaluatedAt)
        => new(itemIds, evaluatedAt, []);

    /// <summary>
    /// An evaluation that was refused before anything was asked of the server.
    /// </summary>
    /// <param name="errors">The reasons, in the order they were found.</param>
    /// <param name="evaluatedAt">The instant the evaluation was given.</param>
    /// <returns>The evaluation.</returns>
    public static RuleEvaluation Refused(
        IReadOnlyList<RuleValidationError> errors,
        DateTimeOffset evaluatedAt)
        => new([], evaluatedAt, errors);
}
