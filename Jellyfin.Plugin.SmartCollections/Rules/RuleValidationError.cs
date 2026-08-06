namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One reason a rule document was refused, and where in the document the reason is.
/// </summary>
/// <remarks>
/// The pointer is an RFC 6901 JSON Pointer into the document that was read, so the same value
/// serves a person reading a log line and a form deciding which control to mark. An empty
/// pointer refers to the whole document, which is what a parse failure or a top level of the
/// wrong kind produces.
/// </remarks>
/// <param name="Pointer">Where in the document the fault is, as a JSON Pointer.</param>
/// <param name="Message">What was wrong, in the words an operator reads.</param>
public sealed record RuleValidationError(string Pointer, string Message)
{
    /// <summary>
    /// Gets the pointer that refers to the document as a whole.
    /// </summary>
    public static string WholeDocument => string.Empty;

    /// <summary>
    /// Returns the error as one line, pointer first.
    /// </summary>
    /// <returns>The pointer and the message.</returns>
    public override string ToString()
        => (Pointer.Length == 0 ? "<document>" : Pointer) + ": " + Message;
}
