using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

    /// <summary>
    /// The refusals whose question is still open on the tracker, each with the question it belongs
    /// to. A refusal in this set is a working assumption rather than a position somebody took, so
    /// the reference has to say so and name the question. This list is the authority for the set,
    /// and the document is held to it in both directions below.
    /// </summary>
    private static readonly (string Refusal, int Question)[] RestOnAnOpenQuestion =
    [
        ("regular expressions", 6),
        ("fields describing one person's viewing", 1),
        ("pinning an item into a collection", 2),
    ];

    private static readonly Regex OpenQuestionLine = new(
        @"^This refusal is the working assumption on question (?<number>\d+) of #67, which has no answer recorded\.[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex AnyHeading = new(
        @"^## ",
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

    /// <summary>
    /// The front page states three of the refusals flatly and sends the reader somewhere else for
    /// the reason behind each. Where that somewhere else is has to be the reference, because the
    /// reference is what the tests above hold: a pointer at anything else leaves the reader
    /// reading an argument nothing is checking. A link to a file that is not in the tree is worse
    /// than no link, so both halves are asserted.
    /// </summary>
    [Fact]
    public void TheFrontPageSendsTheReaderToTheReference()
    {
        Assert.Contains(
            "(" + Reference + ")",
            RepositoryFiles.ReadFromRoot("README.md"),
            StringComparison.Ordinal);

        Assert.True(
            File.Exists(Path.Combine(RepositoryFiles.Root(), Reference.Replace('/', Path.DirectorySeparatorChar))),
            "README.md links " + Reference + " and no such file is in the tree.");
    }

    /// <summary>
    /// A refusal resting on a question nobody has answered is not the same thing as one argued from
    /// a reason and nothing else, and a reader cannot tell the two apart unless the file says which
    /// is which.
    /// </summary>
    [Fact]
    public void EveryRefusalRestingOnAnOpenQuestionSaysSoAndNamesTheQuestion()
    {
        var document = RepositoryFiles.ReadFromRoot(Reference);

        foreach (var (refusal, question) in RestOnAnOpenQuestion)
        {
            var line = OpenQuestionLine.Match(SectionUnder(document, refusal));

            Assert.True(
                line.Success,
                "The refusal '" + refusal + "' does not say that the question behind it is open.");

            Assert.Equal(
                question.ToString(CultureInfo.InvariantCulture),
                line.Groups["number"].Value);
        }
    }

    /// <summary>
    /// The other direction. A refusal that rests on nothing outstanding may not claim that it does,
    /// or the line stops separating anything and the file reads as if every refusal were
    /// provisional.
    /// </summary>
    [Fact]
    public void NoOtherRefusalSaysTheQuestionBehindItIsOpen()
    {
        var document = RepositoryFiles.ReadFromRoot(Reference);
        var recorded = RestOnAnOpenQuestion.Select(entry => entry.Refusal).ToList();

        foreach (var refusal in Refusals.Where(name => !recorded.Contains(name, StringComparer.Ordinal)))
        {
            Assert.False(
                OpenQuestionLine.IsMatch(SectionUnder(document, refusal)),
                "The refusal '" + refusal + "' says the question behind it is open, and it is not one of the recorded set.");
        }
    }

    /// <summary>
    /// The text of one refusal's section, from its marker line to the next heading of any kind. The
    /// bound is the next heading rather than the next marker, so a line in the closing section of
    /// the file is not read as part of the last refusal.
    /// </summary>
    private static string SectionUnder(string document, string refusal)
    {
        foreach (Match marker in MarkerLine.Matches(document))
        {
            if (!string.Equals(marker.Groups["name"].Value, refusal, StringComparison.Ordinal))
            {
                continue;
            }

            var start = marker.Index + marker.Length;
            var next = AnyHeading.Match(document, start);

            return next.Success ? document[start..next.Index] : document[start..];
        }

        throw new InvalidOperationException(
            "The refusal '" + refusal + "' is not in " + Reference + ".");
    }
}
