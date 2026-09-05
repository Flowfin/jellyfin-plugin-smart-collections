using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A broken rule document breaks only itself, read all the way to the collection the valid one
/// produces rather than only to the loader's answer.
/// </summary>
/// <remarks>
/// `RuleDocumentLoaderTests` holds the scan's half of this: one directory, one document that reads
/// and two that do not, two lists rather than one, and nothing on disk touched. That half was met
/// while nothing in this tree could run a rule, so what it proved was that the valid document was
/// LOADED, and the failure it exists against is a valid collection that quietly stops updating.
/// The distance between "loaded" and "updating" is the evaluation, and this is the file that
/// crosses it.
///
/// The whole thing runs against a temporary directory and a list, so it needs no server, no
/// display, no elevated right and no machine trust store.
/// </remarks>
public sealed class BrokenDocumentIsolationTests : IDisposable
{
    private static readonly DateTimeOffset Given = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly string _directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="BrokenDocumentIsolationTests"/> class.
    /// </summary>
    public BrokenDocumentIsolationTests()
        => _directory = Path.Combine(
            Path.GetTempPath(),
            "smart-collections-isolation-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// The clause this file exists for: a directory holding one valid and one malformed document
    /// EVALUATES the valid collection and reports the other.
    /// </summary>
    /// <remarks>
    /// The rule the valid document carries selects two of the three films in the library, so the
    /// answer distinguishes a rule that ran from a rule that collected everything and from one
    /// that collected nothing. A scan that let the malformed file cost the valid one its
    /// evaluation would leave this list empty, which is the failure the whole one-document-per
    /// -collection shape exists against.
    /// </remarks>
    [Fact]
    public void TheValidCollectionIsEvaluatedWhileTheMalformedDocumentIsReported()
    {
        Write("thrillers", Encoding.UTF8.GetBytes(Thrillers));

        // A missing brace, which is the hand edit this design exists against.
        Write("halloween", Encoding.UTF8.GetBytes("{\"schemaVersion\": 1"));

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        var rejected = Assert.Single(scan.Rejected);
        Assert.Equal("halloween", rejected.Name);
        Assert.NotEmpty(rejected.Errors);

        var loaded = Assert.Single(scan.Loaded);
        Assert.Equal("thrillers", loaded.Name);

        var library = Library();
        var evaluation = RuleEvaluator.Evaluate(loaded.Document, library, Given);

        Assert.True(evaluation.IsAccepted);
        Assert.Equal(
            [
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222")
            ],
            evaluation.ItemIds);

        // The pushed half reached the server rather than being applied twice in the plugin.
        Assert.Equal(["Thriller"], Assert.Single(library.Asked).Genres);
    }

    /// <summary>
    /// The same directory, and the malformed file is byte-identical afterwards. The clause names
    /// the file; what is compared is every file in the directory and the set of files, so a run
    /// that repaired a document, renamed it or wrote a report beside it fails here.
    /// </summary>
    /// <remarks>
    /// Asserted after the EVALUATION rather than after the scan, which is what this adds to the
    /// reading the loader's own suite already holds. The bytes the fixture carries are the ones a
    /// repair destroys: a byte order mark, carriage returns and two-space indentation.
    /// </remarks>
    [Fact]
    public void NothingOnDiskMovesWhileTheValidCollectionIsEvaluated()
    {
        Write("thrillers", Encoding.UTF8.GetBytes(Thrillers));

        var broken = new List<byte> { 0xEF, 0xBB, 0xBF };
        broken.AddRange(Encoding.UTF8.GetBytes("{\r\n  \"schemaVersion\": 99\r\n}\r\n"));
        Write("halloween", [.. broken]);

        var before = Fingerprint();

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();
        foreach (var document in scan.Loaded)
        {
            RuleEvaluator.Evaluate(document.Document, Library(), Given);
        }

        Assert.Equal(before, Fingerprint());
    }

    /// <summary>
    /// A document the scan refuses for a contested identifier costs its own collection its
    /// evaluation and no other collection anything, which is the fourth clause read at the
    /// evaluation rather than at the loader.
    /// </summary>
    [Fact]
    public void AContestedIdentifierCostsItsOwnCollectionAndNoOther()
    {
        Write("alpha", Encoding.UTF8.GetBytes(Thrillers));
        Write("zulu", Encoding.UTF8.GetBytes(Thrillers));
        Write("bread", Encoding.UTF8.GetBytes(Quiet));

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        Assert.Equal("zulu", Assert.Single(scan.Rejected).Name);
        Assert.Equal(["alpha", "bread"], scan.Loaded.Select(document => document.Name));

        var answers = scan.Loaded
            .Select(document => RuleEvaluator.Evaluate(document.Document, Library(), Given))
            .ToArray();

        Assert.All(answers, evaluation => Assert.True(evaluation.IsAccepted));
        Assert.Equal(
            [
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222")
            ],
            answers[0].ItemIds);
        Assert.Equal([Guid.Parse("33333333-3333-3333-3333-333333333333")], answers[1].ItemIds);
    }

    /// <summary>
    /// A directory holding nothing but broken documents evaluates nothing and throws nothing, so
    /// an operator who has broken every rule they own meets a list of reasons rather than a
    /// server-side fault.
    /// </summary>
    [Fact]
    public void ADirectoryOfBrokenDocumentsEvaluatesNothingAndThrowsNothing()
    {
        Write("halloween", Encoding.UTF8.GetBytes("{\"schemaVersion\": 1"));
        Write("summer", [0x7B, 0x80, 0x7D]);

        var scan = new RuleDocumentLoader(new RuleDocumentStore(_directory)).Scan();

        Assert.Empty(scan.Loaded);
        Assert.Equal(["halloween", "summer"], scan.Rejected.Select(rejection => rejection.Name));
    }

    /// <summary>
    /// The rule the valid document carries: one condition the server's own query answers and one
    /// it does not, so the evaluation crosses both sides of the boundary.
    /// </summary>
    private static readonly string Thrillers = Rule("thrillers", "Thrillers", "heist");

    /// <summary>
    /// A second rule over the same library, separated from the first by its post-query condition.
    /// </summary>
    private static readonly string Quiet = Rule("quiet", "Quiet", "bread");

    /// <summary>
    /// One rule document, with the word its post-query condition looks for.
    /// </summary>
    /// <param name="id">The rule identifier.</param>
    /// <param name="name">The collection name.</param>
    /// <param name="word">The word the overview has to carry.</param>
    /// <returns>The document.</returns>
    private static string Rule(string id, string name, string word)
        => "{\"schemaVersion\": 1, \"id\": \"" + id + "\", \"name\": \"" + name + "\", \"collects\": [\"movie\"], "
           + "\"match\": {\"allOf\": ["
           + "{\"field\": \"genres\", \"operator\": \"contains\", \"value\": \"Thriller\"}, "
           + "{\"field\": \"overview\", \"operator\": \"contains\", \"value\": \"" + word + "\"}]}}";

    /// <summary>
    /// Three films, all of which satisfy the pushed condition and two of which satisfy the rule.
    /// </summary>
    /// <returns>The library.</returns>
    /// <remarks>
    /// EVERY FILM HERE CARRIES THE GENRE THE RULE PUSHES INTO THE QUERY, and that is a property of
    /// the fixture rather than an accident. The step treats a pushed condition as already
    /// satisfied, because the server answered it; the fake answers with everything it holds and
    /// narrows nothing. A fixture holding a film that fails the pushed condition would therefore
    /// be asserting what a real server would never return, and a test built on it would agree with
    /// the fake rather than with the plugin. What separates the films here is the condition the
    /// query could not carry, which is the half this step actually decides.
    /// </remarks>
    private static FakeRuleItemSource Library()
    {
        var source = new FakeRuleItemSource();

        source.Put(new Movie
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "One",
            Genres = ["Thriller", "Crime"],
            Overview = "A heist in three acts."
        });
        source.Put(new Movie
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Two",
            Genres = ["Thriller"],
            Overview = "Another heist."
        });
        source.Put(new Movie
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "Three",
            Genres = ["Thriller"],
            Overview = "A quiet film about bread."
        });

        return source;
    }

    private void Write(string name, byte[] content)
        => new RuleDocumentStore(_directory).Write(name, content);

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

        return string.Join(Environment.NewLine, lines);
    }
}
