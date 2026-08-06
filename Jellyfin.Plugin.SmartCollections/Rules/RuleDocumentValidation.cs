using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// What reading a rule document produced: either the document, or every reason it was refused.
/// </summary>
/// <remarks>
/// Never both. A caller that has a document has one that passed, and there is no path on which an
/// invalid document reaches anything downstream, which is the property the evaluation work is
/// entitled to assume.
/// </remarks>
public sealed class RuleDocumentValidation
{
    private RuleDocumentValidation(RuleDocument? document, IReadOnlyList<RuleValidationError> errors)
    {
        Document = document;
        Errors = errors;
    }

    /// <summary>
    /// Gets the document, or <see langword="null"/> where the document was refused.
    /// </summary>
    public RuleDocument? Document { get; }

    /// <summary>
    /// Gets every reason the document was refused, in the order they were found.
    /// </summary>
    public IReadOnlyList<RuleValidationError> Errors { get; }

    /// <summary>
    /// Gets a value indicating whether the document passed.
    /// </summary>
    public bool IsValid => Document is not null;

    /// <summary>
    /// Creates the result for a document that passed.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>A result carrying the document and no errors.</returns>
    public static RuleDocumentValidation Accepted(RuleDocument document)
        => new(document, []);

    /// <summary>
    /// Creates the result for a document that was refused.
    /// </summary>
    /// <param name="errors">Every reason it was refused. At least one.</param>
    /// <returns>A result carrying the errors and no document.</returns>
    public static RuleDocumentValidation Refused(IReadOnlyList<RuleValidationError> errors)
        => new(null, errors);
}
