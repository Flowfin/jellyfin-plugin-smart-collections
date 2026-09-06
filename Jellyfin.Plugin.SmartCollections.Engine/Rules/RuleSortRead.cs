using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What reading a rule's order and limit produced: either the terms and the cap, or every reason
/// the read was refused.
/// </summary>
/// <remarks>
/// Never both, which is the shape <see cref="RuleItemScopeRead"/> and <see cref="RuleFieldRead"/>
/// already hold a read in.
///
/// The terms arrive in the DOCUMENT's order rather than in a table's, which is the opposite of the
/// scope beside it and is the difference between a set and a sequence: two documents naming one
/// scope in two orders collect the same items, and two documents naming one pair of sort terms in
/// two orders do not order them the same way.
/// </remarks>
public sealed class RuleSortRead
{
    private RuleSortRead(IReadOnlyList<RuleSortTerm> terms, int? limit, IReadOnlyList<RuleValidationError> errors)
    {
        Terms = terms;
        Limit = limit;
        Errors = errors;
    }

    /// <summary>
    /// Gets the terms the document declared, in the order it wrote them. Empty where it declared
    /// no sort and where the read was refused.
    /// </summary>
    public IReadOnlyList<RuleSortTerm> Terms { get; }

    /// <summary>
    /// Gets the greatest number of items the rule collects, or <see langword="null"/> where the
    /// document declared no cap.
    /// </summary>
    public int? Limit { get; }

    /// <summary>
    /// Gets every reason the read was refused, in the order they were found.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether the document declared an order this plugin accepts.
    /// </summary>
    public bool IsAccepted => Errors.Count == 0;

    /// <summary>
    /// Creates the result for a read that passed.
    /// </summary>
    /// <param name="terms">The terms, in the order the document wrote them. May be empty.</param>
    /// <param name="limit">The cap, or <see langword="null"/> where none was declared.</param>
    /// <returns>A result carrying the order and no errors.</returns>
    public static RuleSortRead Accepted(IReadOnlyList<RuleSortTerm> terms, int? limit)
        => new(terms, limit, []);

    /// <summary>
    /// Creates the result for a read that was refused.
    /// </summary>
    /// <param name="errors">Every reason it was refused. At least one.</param>
    /// <returns>A result carrying the errors and no order.</returns>
    public static RuleSortRead Refused(IReadOnlyList<RuleValidationError> errors)
        => new([], null, errors);
}
