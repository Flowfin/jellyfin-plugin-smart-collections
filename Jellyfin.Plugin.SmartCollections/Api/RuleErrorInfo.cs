namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// One reason a rule document was refused, as the page shows it.
/// </summary>
/// <remarks>
/// The pointer is carried beside the message rather than folded into it, because the page puts a
/// message next to the member it is about and a page parsing a pointer back out of prose would be
/// reading a sentence written for a person.
/// </remarks>
/// <param name="Pointer">Where the fault is, as a JSON Pointer, or empty for the document itself.</param>
/// <param name="Message">What is wrong, in the words an operator reads.</param>
public sealed record RuleErrorInfo(string Pointer, string Message);
