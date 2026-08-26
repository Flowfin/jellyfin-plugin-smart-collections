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
/// The id is carried rather than left in the text, and it is the only member that is. It is the
/// one thing a caller asks about a document without wanting to read it: the scan compares ids to
/// refuse a collision, and what stamps a collection writes this string onto it. Every other
/// member is read by whatever stage understands it, which is the reason this record holds none of
/// them.
/// </remarks>
/// <param name="SchemaVersion">The version the document declares.</param>
/// <param name="Id">The identity the document declares.</param>
/// <param name="Text">The document exactly as it was read.</param>
public sealed record RuleDocument(int SchemaVersion, string Id, string Text);
