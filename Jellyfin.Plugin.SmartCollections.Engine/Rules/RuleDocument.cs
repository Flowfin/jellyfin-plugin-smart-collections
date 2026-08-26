namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// A rule document that passed validation.
/// </summary>
/// <remarks>
/// The text is kept exactly as it was read. Nothing here re-serialises a document, because a
/// document an operator wrote is the thing on disk and a plugin that rewrites it on load is a
/// plugin that, if it is wrong, destroys the original in the same motion. Saving what was loaded
/// therefore returns the same bytes, and a member this version does not understand survives a
/// round trip through the plugin instead of being dropped.
///
/// What this type deliberately does not carry is the rule itself: the fields, the operators, the
/// values and the item scope. Each of those is declared by its own issue on the tracker, and each
/// arrives as a validation stage over the same text rather than as a rewrite of this envelope.
///
/// The id and the name are carried rather than left in the text, and they are the only members
/// that are. They are what a caller asks about a document without wanting to read it: the scan
/// compares ids to refuse a collision, and resolving a rule to its collection reads both, the id
/// to find the collection the rule already owns and the name to call the one it creates. Every
/// other member is read by whatever stage understands it, which is the reason this record holds
/// none of them.
///
/// Carrying the name is what <see cref="Membership.CollectionResolver"/> needs and is not a second
/// declaration of it. The member, its accepted form and every refusal on it are the validator's,
/// and this record holds the string that got past them.
/// </remarks>
/// <param name="SchemaVersion">The version the document declares.</param>
/// <param name="Id">The identity the document declares.</param>
/// <param name="Name">The name the document gives the collection it owns.</param>
/// <param name="Text">The document exactly as it was read.</param>
public sealed record RuleDocument(int SchemaVersion, string Id, string Name, string Text);
