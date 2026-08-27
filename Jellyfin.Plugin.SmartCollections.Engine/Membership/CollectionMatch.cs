using System;

namespace Jellyfin.Plugin.SmartCollections.Membership;

/// <summary>
/// One collection the lookup by mark matched, as the port answers with it.
/// </summary>
/// <remarks>
/// The identifier is what every later call takes, and the name travels beside it because the
/// resolve has to compare what the library shows against what the rule declares. Reading that
/// name through a second port member would ask the server twice for one answer, and the two
/// answers can differ: a rename by any other route between the lookup and the read makes the
/// resolve act on a title that is no longer there.
///
/// The name is what the server holds at the moment of the lookup and nothing more. It is not the
/// rule's name, it is not compared here, and a collection this plugin created carries whatever an
/// operator has since renamed it to.
/// </remarks>
/// <param name="Id">The collection, as the server identifies it.</param>
/// <param name="Name">What the library shows for it at the moment of the lookup.</param>
public sealed record CollectionMatch(Guid Id, string Name);
