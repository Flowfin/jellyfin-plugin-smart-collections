using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// One item kind, as the table declares it.
/// </summary>
/// <remarks>
/// Four things and no more: which kind this is, what a document writes to name it, the member of
/// the server's own enumeration it selects, and what it means in one sentence.
///
/// The written name is declared rather than derived from the member, for the reason
/// <see cref="RuleFieldRow"/> gives about its own: deriving it would make the wire format a
/// property of a C# identifier, so renaming the member for a compiler warning would silently
/// break every rule document on every server.
///
/// It is nonetheless the server's own name for the kind, lowercased. A rule that collects
/// <c>movie</c> selects what the library calls a movie, and a token this plugin invented for the
/// same thing would be one more name for an operator to hold while reading their own library.
/// </remarks>
public sealed class RuleItemKindRow
{
    internal RuleItemKindRow(RuleItemKind kind, string name, BaseItemKind serverKind, string semantics)
    {
        Kind = kind;
        Name = name;
        ServerKind = serverKind;
        Semantics = semantics;
    }

    /// <summary>
    /// Gets the kind this row declares.
    /// </summary>
    public RuleItemKind Kind { get; }

    /// <summary>
    /// Gets the name a rule document writes to name it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the member of the server's own item kind enumeration this kind selects.
    /// </summary>
    /// <remarks>
    /// This is what reaches the library. No member of the server's enumeration carries an explicit
    /// value, so what a compiled query asks for is the member's POSITION in that declaration
    /// rather than its name, and the two supported lines agree on that position only as long as
    /// neither inserts a member above it. That is held by a test against a checked-in ordered list
    /// rather than by this column, which cannot see it.
    /// </remarks>
    public BaseItemKind ServerKind { get; }

    /// <summary>
    /// Gets what the kind is, in one sentence.
    /// </summary>
    public string Semantics { get; }
}
