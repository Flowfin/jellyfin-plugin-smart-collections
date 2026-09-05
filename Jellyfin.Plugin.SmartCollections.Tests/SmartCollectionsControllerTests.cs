using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Api;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The API the configuration page runs on, exercised with no browser and no server.
/// </summary>
/// <remarks>
/// Every success path below drives the controller against a real store in a temporary directory
/// this class creates and removes, so what is asserted is what a request would do to the file
/// system rather than what a mock was told to return.
///
/// WHAT THE AUTHORISATION TESTS PROVE, AND WHAT THEY DO NOT. They read the attributes the
/// controller declares and assert that every action is behind the server's elevation policy. That
/// is the declaration; whether the server's authorisation middleware then refuses a
/// non-administrator is the server's behaviour, and observing it would need a booted server, which
/// <c>docs/testing.md</c> refuses with this API named as what replaces a browser-driven test. So
/// the claim these make is exactly "every endpoint declares the policy" and not "a
/// non-administrator was turned away", and the difference is stated here rather than left for a
/// reader to assume the stronger one.
///
/// The declaration is the thing that can be lost in an edit - an action added without the
/// attribute, or the attribute moved off the class - and it is the thing a test can hold.
/// </remarks>
public sealed class SmartCollectionsControllerTests : IDisposable
{
    private const string Sound = """
        {
          "schemaVersion": 1,
          "id": "thrillers-of-1994",
          "name": "Thrillers of 1994",
          "collects": ["movie"],
          "match": { "allOf": [{ "field": "genres", "operator": "contains", "value": "Thriller" }] }
        }
        """;

    private const string Broken = """
        {
          "schemaVersion": 1,
          "id": "thrillers-of-1994",
          "name": "Thrillers of 1994",
          "collects": ["movie"],
          "match": { "allOf": [{ "field": "studio", "operator": "equals", "value": "Ghibli" }] }
        }
        """;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "smart-collections-api-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));

    private readonly RuleDocumentStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmartCollectionsControllerTests"/> class.
    /// </summary>
    public SmartCollectionsControllerTests() => _store = new RuleDocumentStore(_directory);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static IEnumerable<MethodInfo> Actions()
        => typeof(SmartCollectionsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);

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

    private void Given(string file, string text)
        => _store.Write(file, Encoding.UTF8.GetBytes(text));

    // ---- authorisation ----

    /// <summary>
    /// Without this the sweep below passes over an empty population, which is what a rename or a
    /// namespace move would leave behind.
    /// </summary>
    [Fact]
    public void TheSweepReadsEveryActionTheControllerDeclares()
    {
        var names = Actions().Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                "CreateRule",
                "DeleteRule",
                "ListRules",
                "ReadRule",
                "ReadVocabulary",
                "UpdateRule",
                "ValidateRule"
            ],
            names);
    }

    /// <summary>
    /// Every endpoint is behind the server's elevation policy. The attribute is on the class, so
    /// this reads it there and asserts that no action opts out of it, which is the way it would be
    /// lost.
    /// </summary>
    [Fact]
    public void EveryEndpointDeclaresTheAdministratorPolicyAndNoneOptsOut()
    {
        var onTheClass = typeof(SmartCollectionsController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .ToArray();

        Assert.Contains(onTheClass, attribute => string.Equals(attribute.Policy, Policies.RequiresElevation, StringComparison.Ordinal));

        foreach (var action in Actions())
        {
            Assert.Empty(action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));

            var overridden = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();

            Assert.All(
                overridden,
                attribute => Assert.Equal(Policies.RequiresElevation, attribute.Policy));
        }
    }

    /// <summary>
    /// The near miss, and the reason the sweep above is worth anything: the policy name it asserts
    /// against is the server's own constant rather than a string typed here, so a server that
    /// renamed it would red rather than leaving the sweep comparing two copies of a stale name.
    /// </summary>
    [Fact]
    public void ThePolicyAssertedAgainstIsTheServersOwn()
        => Assert.Equal("RequiresElevation", Policies.RequiresElevation);

    // ---- listing ----

    /// <summary>
    /// The listing carries what loaded and what was refused, and the two are told apart.
    /// </summary>
    [Fact]
    public void TheListingCarriesTheLoadedDocumentsAndTheRefusedFiles()
    {
        Given("thrillers", Sound);
        Given("broken", Broken);

        var listing = Assert.IsType<RuleListing>(Assert.IsType<OkObjectResult>(Controller().ListRules().Result).Value);

        var loaded = Assert.Single(listing.Loaded);
        Assert.Equal("thrillers", loaded.File);
        Assert.Equal("thrillers-of-1994", loaded.Id);
        Assert.Equal("Thrillers of 1994", loaded.Name);
        Assert.Equal(1, loaded.SchemaVersion);

        var rejected = Assert.Single(listing.Rejected);
        Assert.Equal("broken", rejected.File);
        Assert.Contains(rejected.Errors, error => error.Message.Contains("studio", StringComparison.Ordinal));
    }

    /// <summary>
    /// An empty store lists nothing rather than failing. A server that has never had a rule
    /// written on it is an ordinary state.
    /// </summary>
    [Fact]
    public void AnEmptyStoreListsNothing()
    {
        var listing = Assert.IsType<RuleListing>(Assert.IsType<OkObjectResult>(Controller().ListRules().Result).Value);

        Assert.Empty(listing.Loaded);
        Assert.Empty(listing.Rejected);
    }

    // ---- read ----

    /// <summary>
    /// The read returns the bytes on disk, which is what lets an editor load a document and save
    /// it back without reformatting what somebody wrote.
    /// </summary>
    [Fact]
    public void ReadingADocumentReturnsItsBytesUnchanged()
    {
        Given("thrillers", Sound);

        var file = Assert.IsType<FileContentResult>(Controller().ReadRule("thrillers"));

        Assert.Equal("application/json", file.ContentType);
        Assert.Equal(Sound, Encoding.UTF8.GetString(file.FileContents), StringComparer.Ordinal);
    }

    [Fact]
    public void ReadingADocumentTheStoreDoesNotHoldIsNotFound()
        => Assert.IsType<NotFoundObjectResult>(Controller().ReadRule("absent"));

    /// <summary>
    /// An escaping name is refused before anything composes a path, on the read as on the write. A
    /// four hundred and four here would tell a caller the file is not there, which is a different
    /// statement from refusing to look.
    /// </summary>
    [Fact]
    public void ReadingUnderAnEscapingNameIsRefused()
        => Assert.IsType<BadRequestObjectResult>(Controller().ReadRule("../escaped"));

    // ---- create ----

    /// <summary>
    /// The success path, asserted against the directory rather than against the status alone.
    /// </summary>
    [Fact]
    public async Task CreatingADocumentWritesTheBytesThatWereSent()
    {
        var result = await Controller(Sound).CreateRule("thrillers", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(["thrillers"], _store.ListNames());
        Assert.Equal(Sound, Encoding.UTF8.GetString(_store.Read("thrillers")), StringComparer.Ordinal);
    }

    /// <summary>
    /// A document the validator refuses is not written. Without this the endpoint would store a
    /// file the scan then reports as rejected, which is a refusal an operator meets twice.
    /// </summary>
    [Fact]
    public async Task CreatingARefusedDocumentWritesNothingAndReturnsTheReasons()
    {
        var result = await Controller(Broken).CreateRule("thrillers", CancellationToken.None).ConfigureAwait(true);

        var refusal = Assert.IsType<RuleValidationResult>(Assert.IsType<BadRequestObjectResult>(result).Value);

        Assert.False(refusal.Valid);
        Assert.Contains(refusal.Errors, error => error.Message.Contains("studio", StringComparison.Ordinal));
        Assert.Empty(_store.ListNames());
    }

    /// <summary>
    /// A create over a name the store holds is refused rather than replacing it, so a page acting
    /// on a stale listing cannot overwrite a rule somebody else wrote.
    /// </summary>
    [Fact]
    public async Task CreatingOverAnExistingNameIsRefusedAndLeavesTheDocumentAlone()
    {
        Given("thrillers", Sound);

        var result = await Controller(Sound.Replace("Thrillers of 1994", "Something else", StringComparison.Ordinal))
            .CreateRule("thrillers", CancellationToken.None)
            .ConfigureAwait(true);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(Sound, Encoding.UTF8.GetString(_store.Read("thrillers")), StringComparer.Ordinal);
    }

    /// <summary>
    /// THE GUARD THIS ENDPOINT MOST NEEDS. A name composing into a path outside the rules
    /// directory is refused, and the assertion is against the file system rather than against the
    /// status: a four hundred beside a written file would pass a status-only test.
    /// </summary>
    /// <param name="file">A name that is not a bare file name.</param>
    [Theory]
    [InlineData("../escaped")]
    [InlineData("..\\escaped")]
    [InlineData("sub/escaped")]
    [InlineData("..")]
    [InlineData(".")]
    public async Task CreatingUnderAnEscapingNameIsRefusedAndWritesNothing(string file)
    {
        var before = Directory.Exists(_directory)
            ? Directory.GetFiles(_directory, "*", SearchOption.AllDirectories)
            : [];

        var result = await Controller(Sound).CreateRule(file, CancellationToken.None).ConfigureAwait(true);

        Assert.IsType<BadRequestObjectResult>(result);

        var after = Directory.Exists(_directory)
            ? Directory.GetFiles(_directory, "*", SearchOption.AllDirectories)
            : [];

        Assert.Equal(before, after);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escaped.json")), "A file was written outside the store.");
    }

    // ---- update ----

    [Fact]
    public async Task UpdatingADocumentReplacesItsBytes()
    {
        Given("thrillers", Sound);
        var replacement = Sound.Replace("Thrillers of 1994", "Thrillers", StringComparison.Ordinal);

        var result = await Controller(replacement).UpdateRule("thrillers", CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal(replacement, Encoding.UTF8.GetString(_store.Read("thrillers")), StringComparer.Ordinal);
    }

    [Fact]
    public async Task UpdatingUnderAnEscapingNameIsRefusedAndWritesNothing()
    {
        var result = await Controller(Sound).UpdateRule("../escaped", CancellationToken.None).ConfigureAwait(true);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escaped.json")), "A file was written outside the store.");
    }

    [Fact]
    public async Task UpdatingADocumentTheStoreDoesNotHoldIsNotFoundAndCreatesNothing()
    {
        var result = await Controller(Sound).UpdateRule("absent", CancellationToken.None).ConfigureAwait(true);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(_store.ListNames());
    }

    /// <summary>
    /// A refused document does not replace a good one. The document on disk is the one an
    /// operator's collections are built from, so a failed save that half-wrote it would break a
    /// working rule.
    /// </summary>
    [Fact]
    public async Task UpdatingWithARefusedDocumentLeavesTheOneOnDiskAlone()
    {
        Given("thrillers", Sound);

        var result = await Controller(Broken).UpdateRule("thrillers", CancellationToken.None).ConfigureAwait(true);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(Sound, Encoding.UTF8.GetString(_store.Read("thrillers")), StringComparer.Ordinal);
    }

    // ---- delete ----

    [Fact]
    public void DeletingADocumentRemovesItAndLeavesTheOthers()
    {
        Given("thrillers", Sound);
        Given("keep", Sound.Replace("thrillers-of-1994", "keep-this", StringComparison.Ordinal));

        Assert.IsType<NoContentResult>(Controller().DeleteRule("thrillers"));
        Assert.Equal(["keep"], _store.ListNames());
    }

    [Fact]
    public void DeletingADocumentTheStoreDoesNotHoldIsNotFound()
        => Assert.IsType<NotFoundObjectResult>(Controller().DeleteRule("absent"));

    [Fact]
    public void DeletingUnderAnEscapingNameIsRefused()
        => Assert.IsType<BadRequestObjectResult>(Controller().DeleteRule("../escaped"));

    // ---- validate ----

    /// <summary>
    /// The verdict, and nothing written either way. A live check in an editor that saved what it
    /// was checking would be a surprise nobody asked for.
    /// </summary>
    [Fact]
    public async Task ValidatingAnAcceptedDocumentSaysSoAndWritesNothing()
    {
        var result = await Controller(Sound).ValidateRule(CancellationToken.None).ConfigureAwait(true);
        var verdict = Assert.IsType<RuleValidationResult>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.True(verdict.Valid);
        Assert.Empty(verdict.Errors);
        Assert.Empty(_store.ListNames());
    }

    /// <summary>
    /// A refused document comes back as a verdict rather than as a failed request, which is the
    /// one status on this controller that is not the obvious one.
    /// </summary>
    [Fact]
    public async Task ValidatingARefusedDocumentIsAVerdictRatherThanAFailedRequest()
    {
        var result = await Controller(Broken).ValidateRule(CancellationToken.None).ConfigureAwait(true);
        var verdict = Assert.IsType<RuleValidationResult>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(verdict.Valid);
        Assert.Contains(verdict.Errors, error => error.Message.Contains("studio", StringComparison.Ordinal));
        Assert.Empty(_store.ListNames());
    }

    /// <summary>
    /// A body that is not JSON is answered by the validator rather than by the framework, which is
    /// what reading the body as bytes buys.
    /// </summary>
    [Fact]
    public async Task ValidatingSomethingThatIsNotJsonCarriesTheValidatorsMessage()
    {
        var result = await Controller("not json at all").ValidateRule(CancellationToken.None).ConfigureAwait(true);
        var verdict = Assert.IsType<RuleValidationResult>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(verdict.Valid);
        Assert.Contains(verdict.Errors, error => error.Message.StartsWith("The document is not JSON", StringComparison.Ordinal));
    }

    // ---- vocabulary ----

    /// <summary>
    /// The done condition this test carries: the vocabulary endpoint returns exactly the contents
    /// of the tables, compared against them rather than against a list written here.
    /// </summary>
    [Fact]
    public void TheVocabularyIsExactlyWhatTheTablesDeclare()
    {
        var vocabulary = Assert.IsType<Vocabulary>(
            Assert.IsType<OkObjectResult>(Controller().ReadVocabulary().Result).Value);

        Assert.Equal(
            RuleFieldTable.Rows.Select(row => row.Name),
            vocabulary.Fields.Select(field => field.Name));

        foreach (var row in RuleFieldTable.Rows)
        {
            var field = vocabulary.Fields.Single(candidate => string.Equals(candidate.Name, row.Name, StringComparison.Ordinal));

            Assert.Equal(row.ValueType.ToString(), field.ValueType);
            Assert.Equal(row.Operators.Select(@operator => RuleOperatorTable.Of(@operator).Name), field.Operators);
            Assert.Equal(row.Kinds.Select(kind => RuleItemKindTable.Of(kind).Name), field.Kinds);
            Assert.Equal(row.QueryProperty, field.ReachesTheLibrary);
            Assert.Equal(row.Semantics, field.Semantics);
        }

        Assert.Equal(
            RuleOperatorTable.Rows.Select(row => row.Name),
            vocabulary.Operators.Select(@operator => @operator.Name));

        foreach (var row in RuleOperatorTable.Rows)
        {
            var declared = vocabulary.Operators.Single(candidate => string.Equals(candidate.Name, row.Name, StringComparison.Ordinal));

            Assert.Equal(row.FieldTypes.Select(type => type.ToString()), declared.FieldTypes);
            Assert.Equal(row.ValueTypes.Select(type => type.ToString()), declared.ValueTypes);
            Assert.Equal(row.TakesAValue, declared.TakesAValue);
            Assert.Equal(row.TakesAList, declared.TakesAList);
            Assert.Equal(row.Semantics, declared.Semantics);
        }

        Assert.Equal(
            RuleItemKindTable.Rows.Select(row => row.Name),
            vocabulary.ItemKinds.Select(kind => kind.Name));
        Assert.Equal(
            RuleItemKindTable.Rows.Select(row => row.Semantics),
            vocabulary.ItemKinds.Select(kind => kind.Semantics));

        Assert.Equal(RuleCompositionReader.GroupNames, vocabulary.Groups);
        Assert.Equal(RuleDocumentValidator.LowestSchemaVersion, vocabulary.LowestSchemaVersion);
        Assert.Equal(RuleDocumentValidator.CurrentSchemaVersion, vocabulary.CurrentSchemaVersion);
        Assert.Equal(RuleCompositionReader.MaximumNestingDepth, vocabulary.MaximumNestingDepth);
    }

    /// <summary>
    /// The endpoint touches no store, so a server with no rules directory answers it. Without this
    /// a page could not draw its editor until somebody had written a rule.
    /// </summary>
    [Fact]
    public void TheVocabularyIsAnsweredWithNoRulesDirectory()
    {
        Assert.False(Directory.Exists(_directory));

        var vocabulary = Assert.IsType<Vocabulary>(
            Assert.IsType<OkObjectResult>(Controller().ReadVocabulary().Result).Value);

        Assert.NotEmpty(vocabulary.Fields);
    }

    /// <summary>
    /// A document written through the API is one the loader then loads. Each half of that is
    /// asserted above; this is the two together, which is what the page's own round trip is.
    /// </summary>
    [Fact]
    public async Task ADocumentCreatedThroughTheApiIsOneTheLoaderLoads()
    {
        await Controller(Sound).CreateRule("thrillers", CancellationToken.None).ConfigureAwait(true);

        var listing = Assert.IsType<RuleListing>(Assert.IsType<OkObjectResult>(Controller().ListRules().Result).Value);

        Assert.Empty(listing.Rejected);
        Assert.Equal("thrillers-of-1994", Assert.Single(listing.Loaded).Id);
    }

    /// <summary>
    /// The authorisation choice is recorded where an operator meets it and not only in a source
    /// comment. Somebody who granted the server's collection management permission and finds it
    /// does not reach this plugin needs to be told that on the page rather than in a tracker
    /// comment they will never read.
    /// </summary>
    [Fact]
    public void TheConfigurationPageRecordsTheAuthorisationLimitation()
    {
        var page = RepositoryFiles.ReadFromRoot(
            "Jellyfin.Plugin.SmartCollections/Configuration/configPage.html");

        Assert.Contains("requires a server administrator", page, StringComparison.Ordinal);
        Assert.Contains("collection management permission", page, StringComparison.Ordinal);
        Assert.Contains("known limitation", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The store the controller is handed is the one it acts on. A controller that built its own
    /// would write somewhere the rest of the plugin does not read.
    /// </summary>
    [Fact]
    public void AControllerIsRefusedWithoutAStore()
        => Assert.Throws<ArgumentNullException>(() => new SmartCollectionsController(null!));
}
