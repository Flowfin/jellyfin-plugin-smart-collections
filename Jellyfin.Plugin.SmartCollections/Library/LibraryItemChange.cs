using System;

namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// One item, and the last thing that happened to it inside a batch.
/// </summary>
/// <remarks>
/// The kind is the LAST one seen for that item within the batch rather than the first or a list of
/// all of them, and that is a decision rather than an implementation detail. An item added and
/// then updated three times during an import has, by the time anything evaluates, been added; an
/// item added and then removed inside one quiet period is, by the time anything evaluates, gone.
/// Carrying every kind would hand a consumer a history it would have to reduce to exactly this
/// before it could do anything, and carrying the first would describe the library as it was at the
/// start of the burst rather than at the end of it.
/// </remarks>
/// <param name="ItemId">The item the server named.</param>
/// <param name="Kind">The last event this item arrived on inside the batch.</param>
public readonly record struct LibraryItemChange(Guid ItemId, LibraryChangeKind Kind);
