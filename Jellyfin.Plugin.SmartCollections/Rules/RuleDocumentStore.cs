using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;

namespace Jellyfin.Plugin.SmartCollections.Rules;

/// <summary>
/// Reads and writes rule documents as files in one directory, one document per collection.
/// </summary>
/// <remarks>
/// One document per collection rather than one blob holding all of them: a single file means one
/// bad edit takes down every collection on the server, and a directory means a broken document
/// breaks only itself.
///
/// The directory is supplied rather than looked up. On a server it is the plugin's own directory
/// under the path the server hands out, and taking it as a constructor argument is what lets the
/// suite exercise the store in a temporary directory with no server, no display and no elevated
/// rights. Which directory the running plugin passes is decided where the plugin's services are
/// registered.
///
/// Bytes go in and the same bytes come out. Nothing here parses, reformats or re-serialises a
/// document, so a document written by an operator is the document that is read back, down to its
/// line endings and its byte order mark.
/// </remarks>
public sealed class RuleDocumentStore
{
    /// <summary>
    /// The extension every rule document carries.
    /// </summary>
    public const string Extension = ".json";

    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuleDocumentStore"/> class.
    /// </summary>
    /// <param name="directory">The directory holding the rule documents.</param>
    public RuleDocumentStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>
    /// Gets the directory this store reads and writes.
    /// </summary>
    public string Directory => _directory;

    /// <summary>
    /// Lists the documents in the directory, by name and without their extension.
    /// </summary>
    /// <remarks>
    /// Ordinal order, so two servers listing the same directory list it the same way. A directory
    /// that does not exist lists nothing rather than throwing, because a server that has never had
    /// a rule written on it is an ordinary state and not a fault.
    /// </remarks>
    /// <returns>The document names, in ordinal order.</returns>
    public IReadOnlyList<string> ListNames()
    {
        if (!System.IO.Directory.Exists(_directory))
        {
            return [];
        }

        var names = new List<string>();
        foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*" + Extension))
        {
            names.Add(Path.GetFileNameWithoutExtension(path));
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>
    /// Gets a value indicating whether a document of that name is in the directory.
    /// </summary>
    /// <param name="name">The document name, without its extension.</param>
    /// <returns><see langword="true"/> where the file exists.</returns>
    public bool Exists(string name) => File.Exists(PathFor(name));

    /// <summary>
    /// Reads a document exactly as it sits on disk.
    /// </summary>
    /// <param name="name">The document name, without its extension.</param>
    /// <returns>The file's bytes.</returns>
    public byte[] Read(string name) => File.ReadAllBytes(PathFor(name));

    /// <summary>
    /// Writes a document exactly as it was handed over.
    /// </summary>
    /// <param name="name">The document name, without its extension.</param>
    /// <param name="content">The bytes to write.</param>
    [SuppressMessage(
        "Security",
        "CA3003:Review code for file path injection vulnerabilities",
        Justification = "The path is composed by PathFor, which refuses every name that is not a bare file name before anything is written, on both supported platforms. The analyser does not recognise that check as a sanitizer and reports the name as it arrived from the administrator API. Two refusals stand in front of that write and each has its own test: the API refuses the name before it reaches this store, held by CreatingUnderAnEscapingNameIsRefusedAndWritesNothing, which asserts against the directory rather than against a status; and this store refuses it again, held by ANameThatIsNotABareFileNameIsRefused and AnEscapingNameWritesNothingOutsideTheDirectory, which are what red when the check here is removed. THIS IS THE ONLY SUPPRESSION IN THIS TREE, and it is here rather than on the four other sinks because those take a name the store itself produced by listing its own directory.")]
    public void Write(string name, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        System.IO.Directory.CreateDirectory(_directory);
        File.WriteAllBytes(PathFor(name), content);
    }

    /// <summary>
    /// Removes a document from the directory.
    /// </summary>
    /// <remarks>
    /// It answers whether there was one rather than throwing on a name the directory does not
    /// hold, because the caller that asks for a delete is an administrator acting on a listing
    /// that may be a moment old, and a second delete of one rule is not a fault. The name goes
    /// through the same path check as every read and every write, so a name composing outside the
    /// directory is refused here as it is there rather than deleting a file elsewhere.
    ///
    /// Nothing is kept. A copy left behind would be a second document in a directory the loader
    /// scans, under a name nobody chose.
    /// </remarks>
    /// <param name="name">The document name, without its extension.</param>
    /// <returns><see langword="true"/> where a document was removed.</returns>
    /// <exception cref="ArgumentException">The name is not a bare file name.</exception>
    public bool Delete(string name)
    {
        var path = PathFor(name);

        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Whether a string is a name this store will read or write a document under.
    /// </summary>
    /// <remarks>
    /// The same clauses <see cref="PathFor"/> refuses on, asked as a question rather than raised as
    /// an exception, because the API in front of this store has to turn a bad name into a message
    /// an operator reads rather than into an unhandled fault. It is ONE implementation with two
    /// callers rather than a second copy: <see cref="PathFor"/> asks this and builds its refusal
    /// from <see cref="NameRefusal"/>, so a clause added here is added to both routes at once.
    ///
    /// It touches no file. Whether a name is legal and whether a document exists under it are two
    /// questions, and a caller that has to ask the first before composing a path needs an answer
    /// that reaches no file system.
    ///
    /// Both separators are named, and not because one of them is redundant. A backslash is an
    /// ordinary character in a file name on Linux, so <see cref="Path.GetFileName(string)"/> and
    /// <see cref="Path.GetInvalidFileNameChars"/> let one through there and refuse it on Windows.
    /// This plugin ships to both, and a name that is a bare file name on one server and a path on
    /// the other is worse than either answer: it is accepted where it is written and escapes where
    /// it is read. The store answers the same way on every platform instead.
    /// </remarks>
    /// <param name="name">The name to judge.</param>
    /// <returns><see langword="true"/> where a document may be read or written under it.</returns>
    public static bool IsDocumentName(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && !name.Contains('/', StringComparison.Ordinal)
           && !name.Contains('\\', StringComparison.Ordinal)
           && string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
           && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
           && !string.Equals(name, "..", StringComparison.Ordinal)
           && !string.Equals(name, ".", StringComparison.Ordinal);

    /// <summary>
    /// What a caller is told about a name this store will not use.
    /// </summary>
    /// <remarks>
    /// Here rather than at each caller, so the sentence is the same one whether the name arrived
    /// from a directory listing or from a request.
    /// </remarks>
    /// <param name="name">The name as it was written.</param>
    /// <returns>The refusal, naming what was written.</returns>
    public static string NameRefusal(string? name)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"A rule document name is a bare file name, and '{name}' is not one.");

    /// <summary>
    /// Builds the path a document of that name sits at, refusing a name that would leave the
    /// directory.
    /// </summary>
    /// <remarks>
    /// A name reaches this store from a rules directory listing and from the administrator API,
    /// and the second of those is a caller-supplied string. A name carrying a separator or a
    /// parent segment would compose into a path outside the directory this store was given, which
    /// turns "save a rule" into "write a file wherever the server process can reach". The check is
    /// here, in the one place every read and every write goes through, rather than at each caller.
    /// </remarks>
    /// <param name="name">The document name, without its extension.</param>
    /// <returns>The full path of the document.</returns>
    /// <exception cref="ArgumentException">The name is not a bare file name.</exception>
    private string PathFor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!IsDocumentName(name))
        {
            throw new ArgumentException(NameRefusal(name), nameof(name));
        }

        // Composed from Path.GetFileName's answer rather than from the name itself, although the
        // check above has already established the two are the same string. What that buys is a
        // reader, human or static, who can see the last-segment reduction on the line that builds
        // the path rather than inside a predicate above it. The equality clause stays: it is what
        // makes a name that would have been reduced a REFUSAL rather than a quiet truncation, and
        // a store that silently wrote "b" for "a/b" would be answering a request nobody made.
        return Path.Combine(_directory, Path.GetFileName(name) + Extension);
    }
}
