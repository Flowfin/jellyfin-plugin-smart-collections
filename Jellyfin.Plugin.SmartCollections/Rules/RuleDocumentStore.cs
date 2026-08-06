using System;
using System.Collections.Generic;
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
    public void Write(string name, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        System.IO.Directory.CreateDirectory(_directory);
        File.WriteAllBytes(PathFor(name), content);
    }

    /// <summary>
    /// Builds the path a document of that name sits at, refusing a name that would leave the
    /// directory.
    /// </summary>
    /// <remarks>
    /// A name reaches this store from a rules directory listing today and from an administrator
    /// API later, and the second of those is a caller-supplied string. A name carrying a separator
    /// or a parent segment would compose into a path outside the directory this store was given,
    /// which turns "save a rule" into "write a file wherever the server process can reach". The
    /// check is here, in the one place every read and every write goes through, rather than at
    /// each caller.
    /// </remarks>
    /// <param name="name">The document name, without its extension.</param>
    /// <returns>The full path of the document.</returns>
    /// <exception cref="ArgumentException">The name is not a bare file name.</exception>
    private string PathFor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || string.Equals(name, "..", StringComparison.Ordinal)
            || string.Equals(name, ".", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A rule document name is a bare file name, and '{name}' is not one."),
                nameof(name));
        }

        return Path.Combine(_directory, name + Extension);
    }
}
