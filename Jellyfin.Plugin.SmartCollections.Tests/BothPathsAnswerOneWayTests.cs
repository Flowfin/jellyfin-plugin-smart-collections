using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Api;
using Jellyfin.Plugin.SmartCollections.Rules;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A rule arrives two ways - an operator drops a file into the rules directory, or the page posts
/// one through the API - and the two have to refuse the same document with the same words.
/// </summary>
/// <remarks>
/// A document accepted on one path and refused on the other is the failure this holds off: the
/// form says the rule is fine, the next scan of the directory says it is not, and the operator has
/// no way to tell which answer is the real one. One validator is what makes that possible and it
/// is not what makes it true, because either caller can reword what it was handed. So this drives
/// both paths for real - a write into a temporary directory read back by the loader, and a request
/// body through the controller - and compares the answers rather than the call sites.
///
/// <para>
/// WHAT THIS DOES NOT COVER IS THE CONDITION NEITHER PATH CAN ANSWER ON ITS OWN BYTES, and it is
/// named here rather than left to be discovered. The loader refuses a document whose id another
/// document in the directory already holds, and the API has no such refusal: a document posted
/// under an id another file holds is accepted, written, and refused by the next scan. That is the
/// exact shape of failure the paragraph above describes, it is one condition rather than a class
/// of them, and which way it is repaired - the API running the same scan, or the loader's message
/// growing a form that does not name a neighbouring file - is a question about what the API
/// promises rather than about validation. It is #51's, it is open there, and nothing below asserts
/// anything about it.
/// </para>
///
/// <para>
/// The fixtures are one per refusal family the validator can raise on a document read alone, and
/// each one is a shape somebody actually writes: a file that is not JSON at all, a version from
/// the future, a member the vocabulary does not declare, a field name that does not exist, an
/// operator the field does not accept, and a value that will not parse.
/// </para>
/// </remarks>
public sealed class BothPathsAnswerOneWayTests : IDisposable
{
    private readonly string _directory = Path.Join(
        Path.GetTempPath(),
        "smart-collections-both-paths-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

    private readonly RuleDocumentStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="BothPathsAnswerOneWayTests"/> class.
    /// </summary>
    public BothPathsAnswerOneWayTests() => _store = new RuleDocumentStore(_directory);

    /// <summary>
    /// The invalid documents both paths are handed, by the name each one is given.
    /// </summary>
    public static TheoryData<string> Refused => new(Fixtures.Keys);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// The done condition this test carries: every invalid fixture is refused with the same errors,
    /// in the same order, whichever way it arrived.
    /// </summary>
    /// <param name="name">The fixture.</param>
    /// <returns>The assertion.</returns>
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task AnInvalidDocumentIsRefusedWithTheSameWordsWhicheverWayItArrived(string name)
    {
        var text = Fixtures[name];

        // The API first, and the file afterwards, because the create refuses a name the store
        // already holds. Writing the file first would make every fixture a name conflict and this
        // comparison would be of one message rather than of six refusals.
        var throughTheApi = await ThroughTheApi(name, text).ConfigureAwait(true);
        var throughTheFile = ThroughTheFile(name, text);

        Assert.NotEmpty(throughTheFile);
        Assert.Equal(throughTheFile, throughTheApi);
    }

    /// <summary>
    /// Without this the comparison above passes on a fixture set somebody emptied, and on one whose
    /// documents are all valid: two paths that accept a document agree about nothing.
    /// </summary>
    [Fact]
    public void EveryFixtureIsADocumentTheValidatorRefuses()
    {
        Assert.NotEmpty(Fixtures);

        foreach (var (name, text) in Fixtures)
        {
            Assert.False(
                RuleDocumentValidator.Read(Encoding.UTF8.GetBytes(text)).IsValid,
                name + " is a document the validator accepts, so it holds neither path to anything.");
        }
    }

    /// <summary>
    /// The fixtures reach different refusals rather than six spellings of one, which is what makes
    /// the comparison above a comparison of the two paths and not of one message.
    /// </summary>
    [Fact]
    public void TheFixturesReachARefusalEach()
    {
        var messages = Fixtures.Values
            .Select(text => RuleDocumentValidator.Read(Encoding.UTF8.GetBytes(text)))
            .Select(validation => validation.Errors[0].Message)
            .ToArray();

        Assert.Equal(messages.Length, messages.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A valid document arrives the same way too, which is the other direction of the property and
    /// is what stops the comparison above passing on two paths that refuse everything.
    /// </summary>
    /// <returns>The assertion.</returns>
    [Fact]
    public async Task AValidDocumentIsAcceptedWhicheverWayItArrived()
    {
        var created = await Controller(Valid)
            .CreateRule("accepted", CancellationToken.None)
            .ConfigureAwait(true);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<StatusCodeResult>(created).StatusCode);

        var scan = new RuleDocumentLoader(_store).Scan();

        Assert.Empty(scan.Rejected);
        Assert.Equal("accepted", Assert.Single(scan.Loaded).Name);
    }

    private static readonly IReadOnlyDictionary<string, string> Fixtures = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["not-json"] = "this is not a rule document",
        ["a-version-from-the-future"] = """
            {
              "schemaVersion": 99,
              "id": "future",
              "name": "Future",
              "collects": ["movie"],
              "match": { "allOf": [ { "field": "name", "operator": "equals", "value": "x" } ] }
            }
            """,
        ["a-member-that-is-not-declared"] = """
            {
              "schemaVersion": 1,
              "id": "extra",
              "name": "Extra",
              "collects": ["movie"],
              "colour": "blue",
              "match": { "allOf": [ { "field": "name", "operator": "equals", "value": "x" } ] }
            }
            """,
        ["a-field-that-is-not-declared"] = """
            {
              "schemaVersion": 1,
              "id": "unknown-field",
              "name": "Unknown field",
              "collects": ["movie"],
              "match": { "allOf": [ { "field": "studio", "operator": "equals", "value": "Ghibli" } ] }
            }
            """,
        ["an-operator-the-field-refuses"] = """
            {
              "schemaVersion": 1,
              "id": "wrong-operator",
              "name": "Wrong operator",
              "collects": ["movie"],
              "match": { "allOf": [ { "field": "overview", "operator": "equals", "value": "x" } ] }
            }
            """,
        ["a-value-that-will-not-parse"] = """
            {
              "schemaVersion": 1,
              "id": "bad-value",
              "name": "Bad value",
              "collects": ["movie"],
              "match": { "allOf": [ { "field": "productionYear", "operator": "equals", "value": "nineteen" } ] }
            }
            """,
    };

    private static readonly string Valid = """
        {
          "schemaVersion": 1,
          "id": "accepted-rule",
          "name": "Accepted",
          "collects": ["movie"],
          "match": { "allOf": [ { "field": "name", "operator": "equals", "value": "The Job" } ] }
        }
        """;

    /// <summary>
    /// One error as a reader meets it, so a difference in a pointer is a difference this comparison
    /// sees rather than one it renders away.
    /// </summary>
    private static string Rendered(string pointer, string message) => pointer + " " + message;

    private IReadOnlyList<string> ThroughTheFile(string name, string text)
    {
        _store.Write(name, Encoding.UTF8.GetBytes(text));

        var rejected = Assert.Single(new RuleDocumentLoader(_store).Scan().Rejected);

        Assert.Equal(name, rejected.Name);

        return [.. rejected.Errors.Select(error => Rendered(error.Pointer, error.Message))];
    }

    private async Task<IReadOnlyList<string>> ThroughTheApi(string name, string text)
    {
        var refusal = await Controller(text).CreateRule(name, CancellationToken.None).ConfigureAwait(true);

        var result = Assert.IsType<RuleValidationResult>(Assert.IsType<BadRequestObjectResult>(refusal).Value);

        Assert.False(result.Valid);

        return [.. result.Errors.Select(error => Rendered(error.Pointer, error.Message))];
    }

    private SmartCollectionsController Controller(string? body = null)
    {
        var controller = new SmartCollectionsController(_store)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        if (body is not null)
        {
            controller.ControllerContext.HttpContext.Request.Body =
                new MemoryStream(Encoding.UTF8.GetBytes(body));
        }

        return controller;
    }
}
