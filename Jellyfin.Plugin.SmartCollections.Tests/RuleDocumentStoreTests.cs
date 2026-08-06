using System;
using System.IO;
using System.Text;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The store is where a document an operator wrote either survives contact with this plugin or
/// does not. These tests run against a temporary directory, so they need no server, no display,
/// no elevated rights and no machine trust store.
/// </summary>
public sealed class RuleDocumentStoreTests : IDisposable
{
    // The rules directory is nested one level inside a root this class owns, so the escape test
    // below has somewhere to look that is not the machine's temporary directory. A test that
    // asserted about a file directly in the temporary directory would red on a leftover from
    // anything else, and would leave one behind on the run where the guard it tests is broken.
    private readonly string _root;
    private readonly string _directory;

    public RuleDocumentStoreTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "smart-collections-rules-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
        _directory = Path.Combine(_root, "rules");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A document goes through save and load unchanged byte for byte. The bytes chosen are the
    /// ones a reformatting round trip destroys: a byte order mark, carriage returns, two-space
    /// indentation and a member no version of this format declares.
    /// </summary>
    [Fact]
    public void AValidDocumentRoundTripsThroughSaveAndLoadUnchangedByteForByte()
    {
        const string Text = "{\r\n  \"schemaVersion\": 1,\r\n  \"somethingLaterVersionsMayAdd\": {\"kept\": true}\r\n}\r\n";
        var written = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = Encoding.UTF8.GetBytes(Text);
        Array.Resize(ref written, written.Length + body.Length);
        body.CopyTo(written, 3);

        var store = new RuleDocumentStore(_directory);
        store.Write("christmas", written);
        var read = store.Read("christmas");

        Assert.Equal(written, read);

        // And what came back is still a document this plugin accepts, so the round trip is not
        // byte-exact by having produced something unreadable.
        // Decoded with a UTF-8 encoding that consumes the byte order mark, rather than by
        // trimming a character out of the text: the mark is an encoding detail of the file and
        // never a character of the document.
        var result = RuleDocumentValidator.Read(new UTF8Encoding(false).GetString(read, 3, read.Length - 3));
        Assert.True(result.IsValid, "Refused with: " + string.Join(" | ", result.Errors));
    }

    [Fact]
    public void ListingNamesIsOrdinalAndCoversOnlyDocuments()
    {
        var store = new RuleDocumentStore(_directory);
        store.Write("zulu", Encoding.UTF8.GetBytes("{\"schemaVersion\": 1}"));
        store.Write("Alpha", Encoding.UTF8.GetBytes("{\"schemaVersion\": 1}"));
        store.Write("mike", Encoding.UTF8.GetBytes("{\"schemaVersion\": 1}"));
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "not a rule");

        Assert.Equal(new[] { "Alpha", "mike", "zulu" }, store.ListNames());
    }

    /// <summary>
    /// A server that has never had a rule written on it has no rules directory, which is an
    /// ordinary state rather than a fault.
    /// </summary>
    [Fact]
    public void ADirectoryThatDoesNotExistListsNothing()
    {
        var store = new RuleDocumentStore(Path.Combine(_directory, "never-created"));

        Assert.Empty(store.ListNames());
        Assert.False(store.Exists("christmas"));
    }

    /// <summary>
    /// A name reaches this store from a directory listing today and from an administrator API
    /// later. A name carrying a separator or a parent segment would compose into a path outside
    /// the directory the store was given, which turns saving a rule into writing a file wherever
    /// the server process can reach.
    ///
    /// The backslash cases are the reason this is a theory rather than one assertion. A backslash
    /// is an ordinary character in a file name on Linux, so the framework's own file-name helpers
    /// accept one there and refuse it on Windows. This plugin ships to both, and these cases hold
    /// the store to one answer on either.
    /// </summary>
    [Theory]
    [InlineData("../escaped")]
    [InlineData("..\\escaped")]
    [InlineData("nested/christmas")]
    [InlineData("nested\\christmas")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("   ")]
    public void ANameThatIsNotABareFileNameIsRefused(string name)
    {
        var store = new RuleDocumentStore(_directory);

        Assert.Throws<ArgumentException>(() => store.Read(name));
        Assert.Throws<ArgumentException>(() => store.Write(name, Encoding.UTF8.GetBytes("{}")));
        Assert.Throws<ArgumentException>(() => store.Exists(name));
    }

    [Fact]
    public void AnEscapingNameWritesNothingOutsideTheDirectory()
    {
        var store = new RuleDocumentStore(_directory);
        var outside = Path.Combine(_root, "escaped.json");

        Assert.Throws<ArgumentException>(
            () => store.Write("../escaped", Encoding.UTF8.GetBytes("{\"schemaVersion\": 1}")));

        Assert.False(File.Exists(outside), outside + " was written and the name was supposed to be refused.");
    }

    [Fact]
    public void TheStoreReportsTheDirectoryItWasGiven()
    {
        var store = new RuleDocumentStore(_directory);

        Assert.Equal(_directory, store.Directory, StringComparer.Ordinal);
    }

    [Fact]
    public void WritingNoContentIsRefusedRatherThanWritingAnEmptyFile()
    {
        var store = new RuleDocumentStore(_directory);

        Assert.Throws<ArgumentNullException>(() => store.Write("christmas", null!));
        Assert.False(store.Exists("christmas"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AStoreWithNoDirectoryIsRefusedWhereItIsMade(string directory)
    {
        Assert.Throws<ArgumentException>(() => new RuleDocumentStore(directory));
    }

    [Fact]
    public void WritingTheSameNameTwiceLeavesTheSecondDocument()
    {
        var store = new RuleDocumentStore(_directory);
        store.Write("christmas", Encoding.UTF8.GetBytes("{\"schemaVersion\": 1}"));
        store.Write("christmas", Encoding.UTF8.GetBytes("{\"schemaVersion\": 1, \"second\": true}"));

        Assert.Equal(
            "{\"schemaVersion\": 1, \"second\": true}",
            Encoding.UTF8.GetString(store.Read("christmas")),
            StringComparer.Ordinal);
        Assert.Single(store.ListNames());
    }
}
