using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One refusal, as the table declares it.
/// </summary>
/// <remarks>
/// Three things and no more: what the refusal is called, the names a document writes that this
/// plugin reads as reaching for it, and the sentence a refusal message adds when one of those
/// names arrives.
///
/// The refusal's name is the heading <c>docs/rule-language.md</c> carries, character for
/// character, because the message sends a reader to that page and a name that does not match the
/// heading sends them to look for something that is not there. A test holds the two together in
/// both directions.
///
/// The names are written down rather than derived, because there is nothing to derive them from:
/// a refused construct is by definition absent from the field table and from the operator table,
/// so the only record of what somebody would write for it is this one.
/// </remarks>
public sealed class RuleRefusalRow
{
    internal RuleRefusalRow(string refusal, IReadOnlyList<string> names, string message)
    {
        Refusal = refusal;
        Names = names;
        Message = message;
    }

    /// <summary>
    /// Gets what the refusal is called, as <c>docs/rule-language.md</c> heads it.
    /// </summary>
    public string Refusal { get; }

    /// <summary>
    /// Gets the names a document writes that this plugin reads as reaching for the refusal.
    /// </summary>
    /// <remarks>
    /// A field name, an operator name or a member of a condition, all in the one list, because
    /// the question a refusal message asks is whether the token somebody wrote names a refused
    /// construct, and the answer does not depend on which of the three places it was written in.
    /// A name that appears here appears nowhere in the field table or the operator table, which
    /// the suite holds, so a lookup here can never shadow a declared name.
    /// </remarks>
    public IReadOnlyList<string> Names { get; }

    /// <summary>
    /// Gets the sentence a refusal message adds when one of the names arrives.
    /// </summary>
    public string Message { get; }
}
