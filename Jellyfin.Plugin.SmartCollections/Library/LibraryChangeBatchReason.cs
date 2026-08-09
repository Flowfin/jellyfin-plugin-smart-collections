namespace Jellyfin.Plugin.SmartCollections.Library;

/// <summary>
/// Why a batch of library changes was closed when it was.
/// </summary>
/// <remarks>
/// The two reasons are not decoration. A batch closed because the stream went quiet describes a
/// library that has stopped changing, and one closed at the maximum wait describes a library that
/// is still changing and has been kept waiting long enough. A run that keeps reporting the second
/// reason is a server whose import is longer than the maximum, which is a thing an operator can
/// act on by raising it, and a coalescer that reported only "a batch happened" would give them
/// nothing to act on.
/// </remarks>
public enum LibraryChangeBatchReason
{
    /// <summary>
    /// No further change arrived for the quiet period, so the burst is treated as finished.
    /// </summary>
    StreamWentQuiet,

    /// <summary>
    /// Changes were still arriving, and the maximum wait since the first one has passed.
    /// </summary>
    MaximumWaitReached
}
