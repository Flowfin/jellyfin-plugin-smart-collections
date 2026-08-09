using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// The changes that accumulated between one evaluation and the next.
/// </summary>
/// <remarks>
/// The list is ordered by item identifier rather than by arrival, so the same set of changes
/// produces the same batch whichever order the server raised them in. Arrival order is a property
/// of how a scan happened to walk a directory, and a consumer that behaved differently for it
/// would be a consumer whose answer depends on something nobody can reproduce. The order costs one
/// sort per batch, and a batch is produced once per burst rather than once per event.
///
/// The two instants are on the batch rather than read from a clock by whoever handles it. They say
/// what window the batch covers, and an evaluation that wants to report what it was reacting to
/// has the window in front of it rather than a time it looked up afterwards.
/// </remarks>
public sealed class LibraryChangeBatch
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangeBatch"/> class.
    /// </summary>
    /// <param name="changes">The changed items, ordered by identifier.</param>
    /// <param name="firstChangeAt">When the first change in this batch arrived.</param>
    /// <param name="settledAt">When the batch was closed.</param>
    /// <param name="reason">Why it was closed then.</param>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is <see langword="null"/>.</exception>
    public LibraryChangeBatch(
        IReadOnlyList<LibraryItemChange> changes,
        DateTimeOffset firstChangeAt,
        DateTimeOffset settledAt,
        LibraryChangeBatchReason reason)
    {
        ArgumentNullException.ThrowIfNull(changes);

        Changes = changes;
        FirstChangeAt = firstChangeAt;
        SettledAt = settledAt;
        Reason = reason;
    }

    /// <summary>
    /// Gets the changed items, ordered by identifier, one entry per item however many events it arrived on.
    /// </summary>
    public IReadOnlyList<LibraryItemChange> Changes { get; }

    /// <summary>
    /// Gets the instant the first change in this batch arrived.
    /// </summary>
    public DateTimeOffset FirstChangeAt { get; }

    /// <summary>
    /// Gets the instant the batch was closed and handed on.
    /// </summary>
    public DateTimeOffset SettledAt { get; }

    /// <summary>
    /// Gets the reason the batch was closed when it was.
    /// </summary>
    public LibraryChangeBatchReason Reason { get; }
}
