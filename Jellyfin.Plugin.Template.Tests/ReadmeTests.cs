using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Template.Tests;

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
}
