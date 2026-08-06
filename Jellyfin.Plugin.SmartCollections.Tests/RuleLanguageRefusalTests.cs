using System;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The refusals are the part of the rule language a reader cannot infer from the vocabulary, so
/// they are written down rather than remembered. A refusal that leaves the document leaves no
/// trace anywhere else, which is what these tests are against.
/// </summary>
public class RuleLanguageRefusalTests
{
    private const string Reference = "docs/rule-language.md";

    /// <summary>
    /// The refusals recorded so far. Adding one here without adding it to the document, or the
    /// other way round, fails. Lifting a refusal is a change to both, which is the point.
    /// </summary>
    private static readonly string[] Refusals =
    [
        "regular expressions",
        "arbitrary expressions",
        "cross-item aggregates",
        "references between collections",
        "the wall clock as an implicit input",
        "fields describing one person's viewing",
        "pinning an item into a collection",
    ];

    private static readonly Regex MarkerLine = new(
        @"^## Refusal: (?<name>.+?)\s*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void EveryRecordedRefusalIsInTheReference()
    {
        var declared = MarkerLine
            .Matches(RepositoryFiles.ReadFromRoot(Reference))
            .Select(m => m.Groups["name"].Value)
            .ToArray();

        Assert.Equal(Refusals, declared);
    }

    [Fact]
    public void EveryRefusalCarriesAReason()
    {
        var document = RepositoryFiles.ReadFromRoot(Reference);
        var markers = MarkerLine.Matches(document);

        Assert.NotEmpty(markers);

        for (var i = 0; i < markers.Count; i++)
        {
            var start = markers[i].Index + markers[i].Length;
            var end = i + 1 < markers.Count ? markers[i + 1].Index : document.Length;
            var body = document[start..end].Trim();

            Assert.True(
                body.Length > 0,
                "The refusal '" + markers[i].Groups["name"].Value + "' records no reason.");
        }
    }

    /// <summary>
    /// The front page names a subset of the refusals in its own words. A reader who meets one
    /// there and goes looking for the reason has to find it, so the front page may not name a
    /// refusal the reference does not hold.
    /// </summary>
    [Fact]
    public void TheFrontPageDoesNotPromiseARefusalTheReferenceLacks()
    {
        var readme = RepositoryFiles.ReadFromRoot("README.md");
        var document = RepositoryFiles.ReadFromRoot(Reference);

        foreach (var claimed in new[] { "regular expressions", "per-user state", "pinned" })
        {
            Assert.Contains(claimed, readme, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Refusal: regular expressions", document, StringComparison.Ordinal);
        Assert.Contains("one person's viewing", document, StringComparison.Ordinal);
        Assert.Contains("Refusal: pinning an item into a collection", document, StringComparison.Ordinal);
    }
}
