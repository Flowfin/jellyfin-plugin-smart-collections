using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The front page is the first thing a visitor and the catalogue entry show, and it carries a
/// rule document a reader is meant to be able to copy. A front page describing a different
/// project, or an example that does not parse, is wrong in a way no build notices.
/// </summary>
public class ReadmeTests
{
    /// <summary>
    /// The opening sentence of the Jellyfin plugin template's own README. A repository still
    /// carrying it is showing instructions for building something else.
    /// </summary>
    private const string TemplateOpening = "So you want to make a Jellyfin plugin";

    private static readonly Regex FirstJsonBlock = new(
        "```json\r?\n(?<json>.*?)```",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    [Fact]
    public void ReadmeIsNotStillTheTemplateGuide()
    {
        Assert.DoesNotContain(
            TemplateOpening,
            RepositoryFiles.ReadFromRoot("README.md"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadmeNamesBothSupportedServerLines()
    {
        var readme = RepositoryFiles.ReadFromRoot("README.md");

        Assert.Contains("Jellyfin 10.11", readme, StringComparison.Ordinal);
        Assert.Contains("Jellyfin 12.0", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// The example is meant to be copied into a rule directory, so it has to be a document and
    /// not a sketch of one. The schema it will be validated against does not exist yet; what is
    /// checkable today is that it parses and that it carries the fields the format requires of
    /// every document.
    /// </summary>
    [Fact]
    public void ReadmeShowsARuleDocumentThatParses()
    {
        var match = FirstJsonBlock.Match(RepositoryFiles.ReadFromRoot("README.md"));

        Assert.True(match.Success, "README.md carries no fenced json block.");

        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.True(root.TryGetProperty("schemaVersion", out var schemaVersion), "The example declares no schemaVersion.");
        Assert.Equal(JsonValueKind.Number, schemaVersion.ValueKind);

        foreach (var required in new[] { "id", "name", "collects", "match" })
        {
            Assert.True(root.TryGetProperty(required, out _), "The example declares no " + required + ".");
        }
    }

    /// <summary>
    /// <summary>
    /// Every member of the example is explained beneath it. The member list is read out of the
    /// example rather than written here, so a member added to the document reds this test instead
    /// of sitting on the front page unexplained. That is not hypothetical: the member deciding
    /// what an operator sees in their library was published in that example with no clause under
    /// it, no declaration in the schema and no refusal in the validator, and nothing anywhere
    /// said so.
    ///
    /// A clause is a bullet naming the member in code ticks. Two members share one bullet where
    /// they only make sense together, so a name reached by "and" counts as explained too.
    /// </summary>
    [Fact]
    public void EveryMemberOfTheExampleHasAClauseUnderIt()
    {
        var readme = RepositoryFiles.ReadFromRoot("README.md");
        var match = FirstJsonBlock.Match(readme);

        Assert.True(match.Success, "README.md carries no fenced json block.");

        using var document = JsonDocument.Parse(match.Groups["json"].Value);

        foreach (var member in document.RootElement.EnumerateObject())
        {
            Assert.True(
                readme.Contains("- `" + member.Name + "`", StringComparison.Ordinal)
                    || readme.Contains("and `" + member.Name + "`", StringComparison.Ordinal),
                "The example declares " + member.Name + " and no clause under it explains one.");
        }
    }

    /// What happens to the generated collections when the plugin is removed is a promise about
    /// visible library content, and the moment it matters is before an install. A document
    /// nothing points at is a document read after the fact, so the link is part of the promise
    /// rather than a courtesy, and a link to a file that is not there is worse than neither.
    /// </summary>
    [Fact]
    public void ReadmeLinksTheUninstallDocument()
    {
        Assert.Contains(
            "(docs/uninstall.md)",
            RepositoryFiles.ReadFromRoot("README.md"),
            StringComparison.Ordinal);

        Assert.True(
            File.Exists(Path.Combine(RepositoryFiles.Root(), "docs", "uninstall.md")),
            "README.md links docs/uninstall.md and no such file is in the tree.");
    }
}
