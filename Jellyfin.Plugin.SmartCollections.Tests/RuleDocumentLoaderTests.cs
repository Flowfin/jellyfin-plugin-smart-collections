using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The scan is where "a broken rule breaks only itself" either holds or does not. These tests run
/// against a temporary directory, so they need no server, no display, no elevated rights and no
/// machine trust store.
/// </summary>
public sealed class RuleDocumentLoaderTests : IDisposable
{
    private static readonly string Valid = ValidWith("christmas");

    private readonly string _directory;

    public RuleDocumentLoaderTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "smart-collections-loader-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// The property the whole shape exists for. One directory, one document that reads and two
    /// that do not, and the one that reads is loaded with its name rather than lost among the
    /// others.
    /// </summary>
    [Fact]
    public void ADirectoryHoldingOneValidAndTwoBrokenDocumentsLoadsTheOneAndReportsTheOthers()
    {
        Write("christmas", Encoding.UTF8.GetBytes(Valid));

        // A missing brace, which is the hand edit this design exists against.
        Write("halloween", Encoding.UTF8.GetBytes("{\"schemaVersion\": 1"));

        // Not UTF-8 at all: a lone continuation byte, which is what a truncated write leaves.
        Write("summer", [0x7B, 0x80, 0x7D]);

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        var loaded = Assert.Single(scan.Loaded);
        Assert.Equal("christmas", loaded.Name);
        Assert.Equal(Valid, loaded.Document.Text);
        Assert.Equal(1, loaded.Document.SchemaVersion);

        Assert.Equal(["halloween", "summer"], scan.Rejected.Select(rejection => rejection.Name));

        // Each rejection carries its own reasons rather than a shared summary, and the reasons are
        // the validator's own. A rejection with no reason on it is a collection an operator is
        // told is broken with nothing to act on.
        Assert.All(scan.Rejected, rejection => Assert.NotEmpty(rejection.Errors));
        Assert.Contains("not JSON", Reason(scan, "halloween"), StringComparison.Ordinal);
        Assert.Contains("not UTF-8", Reason(scan, "summer"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing on disk moves because of a scan. Every file's bytes and the set of files are both
    /// compared, so a scan that repaired a document, renamed it or wrote a report beside it fails
    /// here.
    /// </summary>
    [Fact]
    public void NothingOnDiskChangesWhenAScanRefusesADocument()
    {
        Write("christmas", Encoding.UTF8.GetBytes(Valid));

        // The bytes a repair would destroy: a byte order mark, carriage returns and two-space
        // indentation, on a document that is refused for its version rather than its shape.
        var broken = new List<byte> { 0xEF, 0xBB, 0xBF };
        broken.AddRange(Encoding.UTF8.GetBytes("{\r\n  \"schemaVersion\": 99\r\n}\r\n"));
        Write("halloween", [.. broken]);

        var before = Fingerprint();

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        Assert.Single(scan.Loaded);
        Assert.Single(scan.Rejected);
        Assert.Equal(before, Fingerprint());
    }

    /// <summary>
    /// A server that has never had a rule written on it is an ordinary state, not a fault.
    /// </summary>
    [Fact]
    public void AnAbsentDirectoryScansAsEmpty()
    {
        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        Assert.Empty(scan.Loaded);
        Assert.Empty(scan.Rejected);
        Assert.False(Directory.Exists(_directory));
    }

    /// <summary>
    /// Both lists are ordinal by name whatever order the files were written in, so two servers
    /// holding the same directory report the same scan.
    /// </summary>
    [Fact]
    public void TheScanIsInOrdinalOrderByNameWhateverOrderTheFilesWereWrittenIn()
    {
        foreach (var name in new[] { "apple", "mango", "Zebra" })
        {
            Write(name + "-good", Encoding.UTF8.GetBytes(ValidWith(name.ToLowerInvariant())));
            Write(name + "-bad", Encoding.UTF8.GetBytes("["));
        }

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        var loaded = scan.Loaded.Select(document => document.Name).ToList();
        var rejected = scan.Rejected.Select(rejection => rejection.Name).ToList();

        // Ordinal rather than the machine's culture, and neither list follows the order the files
        // were written in. Every culture this plugin can be installed under sorts "apple" before
        // "Zebra"; an ordinal comparison does not, because it compares the code units and 'Z' is
        // below 'a'. The three names differ in their first letter rather than in case, so the
        // expected order is the same on a case-insensitive file system and on a case-sensitive
        // one.
        Assert.Equal(["Zebra-good", "apple-good", "mango-good"], loaded);
        Assert.Equal(["Zebra-bad", "apple-bad", "mango-bad"], rejected);
    }

    /// <summary>
    /// A file the store will not read a name out of is one rejection rather than the end of the
    /// scan. Without this arm, one file called `..json` in the rules directory stops every
    /// collection on the server refreshing.
    /// </summary>
    [Fact]
    public void AFileWhoseNameLeavesNothingWhenTheExtensionIsTakenOffIsRejectedRatherThanEndingTheScan()
    {
        Write("christmas", Encoding.UTF8.GetBytes(Valid));

        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "..json"), Encoding.UTF8.GetBytes(Valid));
        File.WriteAllBytes(Path.Combine(_directory, ".json"), Encoding.UTF8.GetBytes(Valid));

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        var loaded = Assert.Single(scan.Loaded);
        Assert.Equal("christmas", loaded.Name);

        Assert.Equal(2, scan.Rejected.Count);
        Assert.All(scan.Rejected, rejection => Assert.NotEmpty(rejection.Errors));
        Assert.All(
            scan.Rejected,
            rejection => Assert.Contains(
                "does not name a document",
                rejection.Errors[0].Message,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Two rules claiming one id are two rules claiming one collection. The first in the scan's
    /// order keeps it and the second is a rejection, so an operator who copied a file to start a
    /// second rule and forgot to change its id loses the copy rather than the original.
    /// </summary>
    [Fact]
    public void ASecondDocumentClaimingAnIdTheFirstHoldsIsRejected()
    {
        Write("christmas", Encoding.UTF8.GetBytes(ValidWith("christmas")));
        Write("winter", Encoding.UTF8.GetBytes(ValidWith("christmas")));

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        var loaded = Assert.Single(scan.Loaded);
        Assert.Equal("christmas", loaded.Name);

        var rejection = Assert.Single(scan.Rejected);
        Assert.Equal("winter", rejection.Name);

        var error = Assert.Single(rejection.Errors);
        Assert.Equal("/id", error.Pointer);

        // The file that holds the id is named, because an operator told only that an id is taken
        // has to grep the directory to find out by what.
        Assert.Contains("christmas.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("already held", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Which of the two survives is decided by the scan's order and never by the file system's, so
    /// the same directory refuses the same document on every run and on either server line. The
    /// names here are chosen so that the ordinal order and the order they are written in disagree.
    /// </summary>
    [Fact]
    public void TheDocumentThatKeepsAContestedIdIsTheFirstInOrdinalOrder()
    {
        Write("zulu", Encoding.UTF8.GetBytes(ValidWith("christmas")));
        Write("alpha", Encoding.UTF8.GetBytes(ValidWith("christmas")));

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        Assert.Equal("alpha", Assert.Single(scan.Loaded).Name);
        Assert.Equal("zulu", Assert.Single(scan.Rejected).Name);
    }

    /// <summary>
    /// The near miss for the refusal above. A name may be shared deliberately, and only the id may
    /// not, so a guard that refused on either would take a legitimate pair of rules away.
    /// </summary>
    [Fact]
    public void TwoDocumentsSharingANameButNotAnIdBothLoad()
    {
        Write("films", Encoding.UTF8.GetBytes(ValidWith("christmas-films")));
        Write("series", Encoding.UTF8.GetBytes(ValidWith("christmas-series")));

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        Assert.Equal(["films", "series"], scan.Loaded.Select(document => document.Name));
        Assert.Empty(scan.Rejected);
    }

    /// <summary>
    /// A contested id costs its own document and nothing else, which is the property the whole
    /// shape exists for read against this refusal rather than against a broken file.
    /// </summary>
    [Fact]
    public void AContestedIdCostsItsOwnDocumentAndNoOther()
    {
        Write("christmas", Encoding.UTF8.GetBytes(ValidWith("christmas")));
        Write("halloween", Encoding.UTF8.GetBytes(ValidWith("halloween")));
        Write("winter", Encoding.UTF8.GetBytes(ValidWith("christmas")));

        var before = Fingerprint();

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        Assert.Equal(["christmas", "halloween"], scan.Loaded.Select(document => document.Name));
        Assert.Equal("winter", Assert.Single(scan.Rejected).Name);

        // Nothing is rewritten, renamed or removed to resolve the contest. The document that lost
        // it is still on disk exactly as the operator wrote it, which is what they need in order
        // to give it an id of its own.
        Assert.Equal(before, Fingerprint());
    }

    /// <summary>
    /// Ids are told apart the way the validator reads them, which is by their code units. Nothing
    /// folds one, and today nothing can: the set an id is made of has one case. This holds the
    /// comparison rather than the set, so a scan that started folding would be caught here even
    /// while every id in the directory is already lowercase.
    /// </summary>
    [Fact]
    public void TwoIdsThatAreNotTheSameStringAreTwoIds()
    {
        Write("christmas", Encoding.UTF8.GetBytes(ValidWith("christmas")));
        Write("winter", Encoding.UTF8.GetBytes(ValidWith("christmas-2")));

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        Assert.Equal(2, scan.Loaded.Count);
        Assert.Empty(scan.Rejected);
    }

    /// <summary>
    /// The loader is handed its store rather than building one, which is what lets every test here
    /// run in a temporary directory.
    /// </summary>
    [Fact]
    public void AStoreIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new RuleDocumentLoader(null!));
    }

    // A rule document that reads, carrying the id it is given. Every valid document in one
    // directory needs an id of its own, because the scan refuses a second document claiming an
    // id a first one already holds, so one shared text would make most of these tests about
    // that refusal instead of about what they are named for.
    private static string ValidWith(string id)
        => "{\"schemaVersion\": 1, \"id\": \"" + id + "\", \"name\": \"Christmas\", \"collects\": [\"movie\"], \"match\": {\"allOf\": [{\"field\": \"genres\", \"operator\": \"contains\", \"value\": \"Thriller\"}]}}";

    private static string Reason(RuleDocumentScan scan, string name)
        => string.Join(
            " ",
            scan.Rejected.Single(rejection => string.Equals(rejection.Name, name, StringComparison.Ordinal))
                .Errors
                .Select(error => error.Message));

    private void Write(string name, byte[] content)
        => new RuleDocumentStore(_directory).Write(name, content);

    // Every file in the directory by name, with the SHA-256 of its bytes. Comparing this either
    // side of a scan catches a file that changed, a file that was added and a file that went away,
    // which is three ways a scan could touch what an operator wrote.
    private string Fingerprint()
    {
        var lines = new List<string>();

        foreach (var path in Directory.EnumerateFiles(_directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            lines.Add(
                Path.GetFileName(path)
                + " "
                + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }

        return string.Join("\n", lines);
    }
}
