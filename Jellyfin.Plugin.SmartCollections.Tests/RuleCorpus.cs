using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The corpus of rule documents kept beside the suite, and the rendering of what each one
/// compiles to.
/// </summary>
/// <remarks>
/// A test written alongside a change tends to agree with it. What this corpus holds instead is
/// the answer a document got BEFORE the change, in a file the change has to move in the same
/// commit, so a moved answer is a diff somebody reads rather than a green run.
///
/// WHAT AN EXPECTED FILE HOLDS IS THE COMPILED QUERY and not the ordered identifier list, decided
/// on #45 on 2026-09-04. The two are different guards: this one catches a change that moves what
/// a document compiles to and is blind to everything after the query, and the list is a second
/// expected file over the same documents, owned by the issue that runs a compiled query. The
/// directory's own README says which of the two a reader is looking at.
///
/// THE INSTANT IS AN ARGUMENT AND NOT A CLOCK. A rule saying "in the last thirty days" compiles
/// against the instant the evaluation was given, so a corpus reading the machine's clock would
/// rewrite its own expected files every day and prove nothing. <see cref="EvaluatedAt"/> is that
/// instant, fixed here, and it is the only reason a <c>withinLast</c> document can be in this
/// corpus at all.
///
/// WHAT THE RENDERING CANNOT SEE is what <see cref="QuerySnapshot"/> cannot see, which that type
/// records: a property whose value has no value equality renders as its type name, and a property
/// with no getter is not read. Beyond that, only the properties a freshly constructed query does
/// NOT carry are written, so the two supported server lines - which declare different numbers of
/// properties - produce one expected file rather than one each.
/// </remarks>
internal static class RuleCorpus
{
    /// <summary>
    /// The corpus directory, relative to the repository root.
    /// </summary>
    public const string Directory = "Jellyfin.Plugin.SmartCollections.Tests/rules";

    /// <summary>
    /// The instant every document in this corpus is compiled at.
    /// </summary>
    /// <remarks>
    /// A fixed instant rather than the machine's clock, and written with an explicit offset for
    /// the reason a rule document's own dates are: a value with no offset names an instant only
    /// once somebody supplies a zone, and the only zone available here is the machine's.
    /// </remarks>
    public static DateTimeOffset EvaluatedAt { get; } =
        new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Gets the documents in the corpus, by name, sorted.
    /// </summary>
    /// <returns>The document names without their extension.</returns>
    public static string[] Names()
    {
        var names = System.IO.Directory
            .GetFiles(Path.Combine(RepositoryFiles.Root(), Directory), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToArray();

        Array.Sort(names, StringComparer.Ordinal);

        return names;
    }

    /// <summary>
    /// The path of a document in the corpus.
    /// </summary>
    /// <param name="name">The document name without its extension.</param>
    /// <returns>The absolute path.</returns>
    public static string DocumentPath(string name)
        => Path.Combine(RepositoryFiles.Root(), Directory, name + ".json");

    /// <summary>
    /// The path of a document's expected file.
    /// </summary>
    /// <param name="name">The document name without its extension.</param>
    /// <returns>The absolute path.</returns>
    public static string ExpectedPath(string name)
        => Path.Combine(RepositoryFiles.Root(), Directory, name + ".expected.txt");

    /// <summary>
    /// The lines an expected file holds for a document, in the order they are written.
    /// </summary>
    /// <param name="name">The document name without its extension.</param>
    /// <returns>The rendering.</returns>
    public static IReadOnlyList<string> Render(string name)
        => RenderText(File.ReadAllText(DocumentPath(name)));

    /// <summary>
    /// The lines an expected file holds for a document's text.
    /// </summary>
    /// <param name="text">The document exactly as it is on disk.</param>
    /// <returns>The rendering.</returns>
    /// <remarks>
    /// The validator is asked first and the stages are walked afterwards, which is the order the
    /// plugin itself reads a document in. A document the validator refuses renders its refusals
    /// and nothing else: there is no query to render, and rendering a partial one would put a
    /// half-compiled answer in a file whose whole purpose is to be trusted.
    ///
    /// THE COMPILER IS REACHED ONLY BY AN ACCEPTED DOCUMENT AND CAN STILL REFUSE ONE. Two
    /// conditions writing one query property are refused there rather than in validation, so that
    /// refusal is rendered under its own prefix and a reader can tell which of the two stages
    /// produced it.
    /// </remarks>
    private static IReadOnlyList<string> RenderText(string text)
    {
        var validation = RuleDocumentValidator.Read(text);
        if (!validation.IsValid)
        {
            return validation.Errors.Select(error => "refused: " + error).ToArray();
        }

        using var parsed = JsonDocument.Parse(text);
        var root = parsed.RootElement;

        var scope = RuleItemScopeReader.Read(root);
        var conditions = ReadConditions(root);
        var compilation = RuleQueryCompiler.Compile(scope.Kinds, conditions, EvaluatedAt);

        if (!compilation.IsAccepted)
        {
            return compilation.Errors
                .Select(error => "refused by the compiler: " + error)
                .ToArray();
        }

        var lines = new List<string>
        {
            "scope: " + string.Join(", ", scope.Kinds.Select(kind => kind.Name))
        };

        var snapshot = QuerySnapshot.Of(compilation.Query);
        foreach (var property in QuerySnapshot.Moved(compilation.Query))
        {
            lines.Add("query: " + property + "=" + snapshot[property]);
        }

        foreach (var condition in compilation.AfterTheQuery)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"after the query: {condition.Pointer} {condition.Field.Name} {condition.Operator.Name} [{string.Join(", ", condition.Values)}]"));
        }

        return lines;
    }

    /// <summary>
    /// The conditions a document in the corpus writes, or none where the read refuses it.
    /// </summary>
    /// <param name="name">The document name without its extension.</param>
    /// <returns>The conditions, with their values parsed.</returns>
    /// <remarks>
    /// Read rather than parsed out of the rendering, because the rendering says which query
    /// property a condition wrote and never which pair wrote it: two pairs on one field write one
    /// property, so a coverage claim taken off the rendered lines would count either of them for
    /// both.
    /// </remarks>
    public static IReadOnlyList<RuleConditionValue> Conditions(string name)
    {
        var text = File.ReadAllText(DocumentPath(name));
        if (!RuleDocumentValidator.Read(text).IsValid)
        {
            return [];
        }

        using var parsed = JsonDocument.Parse(text);

        return ReadConditions(parsed.RootElement);
    }

    // The conditions the rule stages read, over a document validation has already accepted. Each
    // stage is handed what the one before it produced, which is why this is a walk rather than
    // four independent calls; a document with no rule at all reads as no conditions, which the
    // compiler bounds by the scope alone.
    private static IReadOnlyList<RuleConditionValue> ReadConditions(JsonElement root)
    {
        if (!root.TryGetProperty("match", out var match))
        {
            return [];
        }

        var composition = RuleCompositionReader.Read(match, "/match");
        var fields = RuleFieldReader.Read(root, composition.Group!, RuleItemScopeReader.Read(root).Kinds);
        var operators = RuleOperatorReader.Read(root, fields.Fields);

        return RuleValueReader.Read(root, operators.Operators).Conditions;
    }
}
