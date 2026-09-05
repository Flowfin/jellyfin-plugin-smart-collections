using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.SmartCollections.Api;

/// <summary>
/// The server side the configuration page runs on.
/// </summary>
/// <remarks>
/// EVERYTHING THE PAGE DOES GOES THROUGH HERE FIRST. Logic in the page would mean the only way to
/// check what this plugin does is to open a browser, and a browser-driven test is refused by
/// <c>docs/testing.md</c> with this API named as what replaces it. So the page is a thin client
/// and this is the thing the suite exercises.
///
/// SEVEN ENDPOINTS RATHER THAN THE NINE #47 OPENS WITH. Triggering a refresh and reading the state
/// of one need something that runs a rule, and they moved to the evaluation issue that owns
/// running a compiled query, decided on #47 on 2026-09-04. The seven here stand over the document
/// store and the vocabulary tables, both of which exist.
///
/// AUTHORISATION IS THE ADMINISTRATOR ON EVERY ENDPOINT, and that is a choice rather than the only
/// answer. The server carries a separate collection management permission, and somebody who
/// granted it will expect it to reach a plugin that writes collections. It is not honoured here,
/// because honouring it means reading that permission against what each of these endpoints
/// actually exposes - a rule document decides what appears in a shared library view, which is a
/// wider thing than managing one collection - and that reading has not been made. The limitation
/// is recorded on the configuration page rather than only here, so an operator meets it where they
/// meet the plugin.
///
/// THE BYTES AN OPERATOR WROTE ARE THE BYTES THAT ARE STORED. The write endpoints read the request
/// body as bytes and hand them to the validator and then to the store, rather than binding a
/// model and re-serialising it. Binding would reformat the document, reorder its members and drop
/// the byte order mark, so what came back from a read would not be what was sent - and it would
/// answer a body that is not JSON with the framework's message instead of the validator's, which
/// names where the parse failed.
/// </remarks>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("SmartCollections")]
[Produces("application/json")]
public sealed class SmartCollectionsController : ControllerBase
{
    private readonly RuleDocumentStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmartCollectionsController"/> class.
    /// </summary>
    /// <param name="store">The rule document store, which is the plugin's single one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public SmartCollectionsController(RuleDocumentStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Lists the rule documents the store holds, in both of their states.
    /// </summary>
    /// <returns>The documents that loaded and the files that were refused.</returns>
    /// <response code="200">The listing.</response>
    [HttpGet("Rules")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<RuleListing> ListRules()
    {
        var scan = new RuleDocumentLoader(_store).Scan();

        return Ok(new RuleListing(
            scan.Loaded
                .Select(loaded => new LoadedRuleInfo(
                    loaded.Name,
                    loaded.Document.Id,
                    loaded.Document.Name,
                    loaded.Document.SchemaVersion))
                .ToArray(),
            scan.Rejected
                .Select(rejected => new RejectedRuleInfo(rejected.Name, Errors(rejected.Errors)))
                .ToArray()));
    }

    /// <summary>
    /// Reads one rule document, exactly as it sits on disk.
    /// </summary>
    /// <param name="file">The document's file name, without its extension.</param>
    /// <returns>The document's bytes.</returns>
    /// <response code="200">The document.</response>
    /// <response code="400">The name is not a bare file name.</response>
    /// <response code="404">No document of that name is in the store.</response>
    /// <remarks>
    /// The bytes rather than a re-serialised object, for the reason the class remarks give: a
    /// document an operator wrote is the thing on disk, and an editor that loaded a reformatted
    /// copy would save the reformatting back over what they wrote.
    /// </remarks>
    [HttpGet("Rules/{file}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult ReadRule(string file)
    {
        if (!TryName(file, out var refusal))
        {
            return refusal!;
        }

        var held = Held(file);
        if (held is null)
        {
            return NotFound(Missing(file));
        }

        return File(_store.Read(held), "application/json");
    }

    /// <summary>
    /// Creates a rule document under a name the store does not already hold.
    /// </summary>
    /// <param name="file">The document's file name, without its extension.</param>
    /// <param name="cancellationToken">Abandons the read of the request body.</param>
    /// <returns>Nothing on success.</returns>
    /// <response code="201">The document was written.</response>
    /// <response code="400">The name is not a bare file name, or the document was refused.</response>
    /// <response code="409">A document of that name is already in the store.</response>
    /// <remarks>
    /// A create and an update are two endpoints rather than one write, because they answer
    /// different questions about a name the caller already has: a create that quietly replaced a
    /// document would be a page's stale listing overwriting a rule somebody else wrote.
    /// </remarks>
    [HttpPost("Rules/{file}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> CreateRule(string file, CancellationToken cancellationToken)
    {
        if (!TryName(file, out var refusal))
        {
            return refusal!;
        }

        if (Held(file) is not null)
        {
            return Conflict(new RuleValidationResult(
                false,
                [new RuleErrorInfo(
                    string.Empty,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"A rule document called \"{file}\" is already in the store. Update it rather than creating it, or choose another name."))]));
        }

        return await WriteAsync(file, StatusCodes.Status201Created, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces a rule document the store already holds.
    /// </summary>
    /// <param name="file">The document's file name, without its extension.</param>
    /// <param name="cancellationToken">Abandons the read of the request body.</param>
    /// <returns>Nothing on success.</returns>
    /// <response code="200">The document was written.</response>
    /// <response code="400">The name is not a bare file name, or the document was refused.</response>
    /// <response code="404">No document of that name is in the store.</response>
    [HttpPut("Rules/{file}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateRule(string file, CancellationToken cancellationToken)
    {
        if (!TryName(file, out var refusal))
        {
            return refusal!;
        }

        var held = Held(file);
        if (held is null)
        {
            return NotFound(Missing(file));
        }

        return await WriteAsync(held, StatusCodes.Status200OK, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a rule document from the store.
    /// </summary>
    /// <param name="file">The document's file name, without its extension.</param>
    /// <returns>Nothing.</returns>
    /// <response code="204">The document was removed.</response>
    /// <response code="400">The name is not a bare file name.</response>
    /// <response code="404">No document of that name is in the store.</response>
    /// <remarks>
    /// The collection the rule owned is not touched here. What happens to a collection whose rule
    /// document disappears is its own question with its own issue, and an endpoint that deleted a
    /// library object as a side effect of deleting a file would be answering it in a diff.
    /// </remarks>
    [HttpDelete("Rules/{file}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult DeleteRule(string file)
    {
        if (!TryName(file, out var refusal))
        {
            return refusal!;
        }

        var held = Held(file);
        if (held is null)
        {
            return NotFound(Missing(file));
        }

        // The answer is not read. Held has just seen the name in the store's own listing, so a
        // false here is a document removed between that listing and this line - by a second
        // administrator, or by somebody editing the directory on the server - and the caller asked
        // for the document to be gone, which it is. Reporting a four hundred and four for a
        // successful outcome would make a page retry a delete that had already happened.
        _store.Delete(held);

        return NoContent();
    }

    /// <summary>
    /// Reads a document through the validator and writes nothing.
    /// </summary>
    /// <param name="cancellationToken">Abandons the read of the request body.</param>
    /// <returns>What the validator said.</returns>
    /// <response code="200">The verdict, whether the document was accepted or refused.</response>
    /// <remarks>
    /// TWO HUNDRED FOR A REFUSED DOCUMENT, which is the one status on this controller that is not
    /// the obvious one. The request is "tell me what you think of this", and the answer is a
    /// verdict; a document the validator refuses is a successful answer to that question, and a
    /// four hundred would make an editor's live check indistinguishable from the editor sending a
    /// malformed request.
    /// </remarks>
    [HttpPost("Rules/Validate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<RuleValidationResult>> ValidateRule(CancellationToken cancellationToken)
    {
        var validation = RuleDocumentValidator.Read(await BodyAsync(cancellationToken).ConfigureAwait(false));

        return Ok(new RuleValidationResult(validation.IsValid, Errors(validation.Errors)));
    }

    /// <summary>
    /// Reads the vocabulary a rule may be written from.
    /// </summary>
    /// <returns>The fields, the operators, the item kinds and the group names.</returns>
    /// <response code="200">The vocabulary.</response>
    /// <remarks>
    /// Every list is derived from the table that decides it, on every request. A page holding its
    /// own copy is how a form comes to offer an operator the engine refuses, and a copy built once
    /// on this side would be the same defect one process further away.
    /// </remarks>
    [HttpGet("Vocabulary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<Vocabulary> ReadVocabulary()
        => Ok(new Vocabulary(
            RuleFieldTable.Rows
                .Select(row => new VocabularyField(
                    row.Name,
                    row.ValueType.ToString(),
                    row.Operators.Select(@operator => RuleOperatorTable.Of(@operator).Name).ToArray(),
                    row.Kinds.Select(kind => RuleItemKindTable.Of(kind).Name).ToArray(),
                    row.QueryProperty,
                    row.Semantics))
                .ToArray(),
            RuleOperatorTable.Rows
                .Select(row => new VocabularyOperator(
                    row.Name,
                    row.FieldTypes.Select(type => type.ToString()).ToArray(),
                    row.ValueTypes.Select(type => type.ToString()).ToArray(),
                    row.TakesAValue,
                    row.TakesAList,
                    row.Semantics))
                .ToArray(),
            RuleItemKindTable.Rows
                .Select(row => new VocabularyItemKind(row.Name, row.Semantics))
                .ToArray(),
            RuleCompositionReader.GroupNames,
            RuleDocumentValidator.LowestSchemaVersion,
            RuleDocumentValidator.CurrentSchemaVersion,
            RuleCompositionReader.MaximumNestingDepth));

    private static RuleErrorInfo[] Errors(IReadOnlyList<RuleValidationError> errors)
    {
        var listed = new RuleErrorInfo[errors.Count];
        for (var index = 0; index < errors.Count; index++)
        {
            listed[index] = new RuleErrorInfo(errors[index].Pointer, errors[index].Message);
        }

        return listed;
    }

    private static RuleValidationResult Missing(string file)
        => new(
            false,
            [new RuleErrorInfo(
                string.Empty,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"No rule document called \"{file}\" is in the store."))]);

    // The name check is the store's, asked here so a refusal comes back as a message rather than
    // as an unhandled argument fault. Asking it rather than repeating it is what keeps one answer
    // to what a document may be called: a second copy of those clauses would be the drift the
    // store's own remarks argue against, and the sentence a caller reads comes from the store too
    // rather than being written twice in two voices.
    private bool TryName(string file, out ActionResult? refusal)
    {
        if (RuleDocumentStore.IsDocumentName(file))
        {
            refusal = null;
            return true;
        }

        refusal = BadRequest(new RuleValidationResult(
            false,
            [new RuleErrorInfo(string.Empty, RuleDocumentStore.NameRefusal(file))]));

        return false;
    }

    // The name as the STORE'S OWN LISTING spells it, or null where the listing does not hold it.
    //
    // Every endpoint that acts on a document the store already holds goes through here rather than
    // handing the route value on, so what reaches the file system is a name the store produced by
    // listing its own directory rather than a string somebody sent. The store's path check is what
    // makes that safe either way and is not weakened by this; what this adds is that three of the
    // four endpoints cannot name a file the directory does not already carry, which is a narrower
    // thing than "a bare file name".
    //
    // Ordinal, because a document name is a file name in a directory this plugin owns and not a
    // word in a language: a case-insensitive match would make two names one on Windows and two on
    // Linux, and the store answers the same way on both.
    private string? Held(string file)
    {
        foreach (var name in _store.ListNames())
        {
            if (string.Equals(name, file, StringComparison.Ordinal))
            {
                return name;
            }
        }

        return null;
    }

    private async Task<byte[]> BodyAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }

    private async Task<ActionResult> WriteAsync(string file, int status, CancellationToken cancellationToken)
    {
        var content = await BodyAsync(cancellationToken).ConfigureAwait(false);
        var validation = RuleDocumentValidator.Read(content);

        if (!validation.IsValid)
        {
            return BadRequest(new RuleValidationResult(false, Errors(validation.Errors)));
        }

        _store.Write(file, content);

        return StatusCode(status);
    }
}
