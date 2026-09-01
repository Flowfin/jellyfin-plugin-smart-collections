using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A logger that keeps what was written to it, so a test can assert on the level as well as on
/// the text.
/// </summary>
/// <remarks>
/// The level is the property under test here rather than a detail of the message. What
/// <see cref="Jellyfin.Plugin.SmartCollections.Membership.MembershipApplier"/> owes is that a
/// refresh which changed nothing writes no information-level line, and a fake that only captured
/// rendered text could not tell that from one that wrote the line at a quieter level.
///
/// The message is captured rendered rather than as a template with arguments beside it, because
/// what an operator greps is the rendered line. A template holding the rule identifier as a
/// placeholder and a formatter that dropped it would pass a test written against the template.
/// </remarks>
public sealed class RecordingLogger : ILogger
{
    private readonly List<LoggedLine> _lines = [];

    /// <summary>
    /// Gets every line written to this logger, in the order it was written.
    /// </summary>
    public IReadOnlyList<LoggedLine> Lines => _lines;

    /// <summary>
    /// Gets the lines written at one level.
    /// </summary>
    /// <param name="level">The level to read.</param>
    /// <returns>The lines written at <paramref name="level"/>, in order.</returns>
    public IReadOnlyList<LoggedLine> At(LogLevel level)
        => [.. _lines.FindAll(line => line.Level == level)];

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
        => NoScope.Instance;

    /// <summary>
    /// Answers that every level is enabled.
    /// </summary>
    /// <param name="logLevel">The level being asked about.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    /// <remarks>
    /// A fake that answered <see langword="false"/> for a level would let a caller writing at that
    /// level pass a test asserting the level is not written, which is the assertion this type
    /// exists for.
    /// </remarks>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _lines.Add(new LoggedLine(logLevel, formatter(state, exception), exception));
    }

    /// <summary>
    /// One line written to a <see cref="RecordingLogger"/>.
    /// </summary>
    /// <param name="Level">The level it was written at.</param>
    /// <param name="Message">The rendered message.</param>
    /// <param name="Exception">What was written with it, or <see langword="null"/>.</param>
    public sealed record LoggedLine(LogLevel Level, string Message, Exception? Exception);

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
}
