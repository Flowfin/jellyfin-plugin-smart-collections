using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The reference tells a reader what a name means; the worked examples tell them what an
/// assembled document looks like. An example the plugin would refuse is worse than no example at
/// all: somebody copies it, meets a refusal, and looks for the fault in their own document.
/// </summary>
/// <remarks>
/// These tests hand every document on the page to <see cref="RuleDocumentValidator"/>, which is
/// the type the rules directory scan hands a file to, rather than comparing against the shipped
/// schema. The schema declares the envelope and tolerates anything beside it, so a comparison
/// against it alone is met by a document whose rule names a field no table declares and an
/// operator this plugin refuses by name. The validator reads the rule as well, so what it accepts
/// is what a server would load.
///
/// What is asserted about the vocabulary is deliberately narrower than what the field and
/// operator pages assert. Those two are held to their tables in both directions and are the
/// exhaustive lists. Requiring every field and every operator to appear in an example would put a
/// second copy of both tables on this page, and the copy would be the one that drifts. The two
/// closed sets small enough for an example to teach are held in both directions here: the
/// composition groups and the item kinds.
/// </remarks>
public class RuleExampleDocumentTests
{
    private const string Page = "docs/rule-examples.md";

    private static readonly Regex Example = new(
        @"^## Example: (?<title>.+?)\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex JsonBlock = new(
        "^```json\r?\n(?<json>.*?)^```\r?$",
        RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// The members the validator reads, derived from the constants that declare them rather than
    /// written out, so a member added to the format tomorrow is admitted here on the day the
    /// validator starts reading it.
    /// </summary>
    private static readonly string[] MembersTheValidatorReads =
    [
        RuleDocumentValidator.SchemaVersionMember,
        RuleDocumentValidator.IdMember,
        RuleDocumentValidator.NameMember,
        RuleDocumentValidator.MatchMember,
        RuleItemScopeReader.CollectsMember,
    ];

    private static string PageText() => RepositoryFiles.ReadFromRoot(Page);

    private static string[] Titles()
        => Example.Matches(PageText())
            .Select(match => match.Groups["title"].Value)
            .ToArray();

    private static string[] Documents()
        => JsonBlock.Matches(PageText())
            .Select(match => match.Groups["json"].Value)
            .ToArray();

    /// <summary>
    /// Without this every comparison below passes on a page somebody emptied, because a walk over
    /// no documents agrees with a walk over correct ones.
    /// </summary>
    [Fact]
    public void ThePageCarriesWorkedDocuments()
    {
        Assert.True(File.Exists(Path.Combine(RepositoryFiles.Root(), Page)), Page + " is not in the tree.");
        Assert.NotEmpty(Titles());
        Assert.NotEmpty(Documents());
    }

    /// <summary>
    /// One document per section, so a section whose example was deleted and a document sitting
    /// under no heading are both refused. Either would leave a reader matching prose against the
    /// wrong block.
    /// </summary>
    [Fact]
    public void EverySectionCarriesOneDocumentAndEveryDocumentSitsUnderASection()
    {
        Assert.Equal(Titles().Length, Documents().Length);
    }

    /// <summary>
    /// The property this page exists for. Every example is read by the validator, not merely
    /// parsed, so an example naming a field no table declares, an operator the field does not
    /// accept or a value that will not parse reds the suite with what the refusing stage said.
    /// </summary>
    [Fact]
    public void EveryExampleIsAcceptedByTheValidatorThatReadsARule()
    {
        var titles = Titles();
        var documents = Documents();

        for (var i = 0; i < documents.Length; i++)
        {
            var read = RuleDocumentValidator.Read(documents[i]);

            Assert.True(
                read.IsValid,
                "The example under \"" + titles[i] + "\" is refused: "
                    + string.Join(
                        " | ",
                        read.Errors.Select(error => error.Pointer + " " + error.Message)));
        }
    }

    /// <summary>
    /// The examples are a directory as much as a page: somebody copies several of them into one
    /// rules directory, and the scan refuses a document claiming an id another document already
    /// holds. Two examples sharing an id would make the second one impossible to use beside the
    /// first, which nothing on the page would say.
    /// </summary>
    [Fact]
    public void NoTwoExamplesClaimOneIdentity()
    {
        var ids = Documents()
            .Select(text => JsonDocument.Parse(text).RootElement
                .GetProperty(RuleDocumentValidator.IdMember).GetString())
            .ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Held in both directions, because this page's own claim is that the shape of every group is
    /// visible somewhere on it. A group declared with no example showing it leaves the claim
    /// false, and a group written in an example the reader does not accept would have been refused
    /// by the validator already.
    /// </summary>
    [Fact]
    public void EveryGroupTheReaderAcceptsAppearsInSomeExample()
    {
        var written = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var text in Documents())
        {
            CollectGroups(JsonDocument.Parse(text).RootElement, written);
        }

        Assert.Equal(
            RuleCompositionReader.GroupNames.OrderBy(name => name, StringComparer.Ordinal),
            written);
    }

    /// <summary>
    /// The same in both directions for the item kinds, so a third kind declared tomorrow reds
    /// this page until an example collects it. A reader whose library holds a kind no example
    /// names has nothing to copy.
    /// </summary>
    [Fact]
    public void EveryItemKindTheTableDeclaresAppearsInSomeExample()
    {
        var collected = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var text in Documents())
        {
            foreach (var kind in JsonDocument.Parse(text).RootElement
                .GetProperty(RuleItemScopeReader.CollectsMember).EnumerateArray())
            {
                collected.Add(kind.GetString()!);
            }
        }

        Assert.Equal(
            RuleItemKindTable.Names.OrderBy(name => name, StringComparer.Ordinal),
            collected);
    }

    /// <summary>
    /// The page says no example writes a member nothing reads, and this is what holds it. A
    /// document may carry an undeclared member without being refused, so an example carrying one
    /// would show a reader a rule doing something the engine ignores, and the validator would
    /// never say so.
    /// </summary>
    [Fact]
    public void NoExampleWritesAMemberTheValidatorDoesNotRead()
    {
        var titles = Titles();
        var documents = Documents();

        for (var i = 0; i < documents.Length; i++)
        {
            foreach (var member in JsonDocument.Parse(documents[i]).RootElement.EnumerateObject())
            {
                Assert.True(
                    MembersTheValidatorReads.Contains(member.Name, StringComparer.Ordinal),
                    "The example under \"" + titles[i] + "\" writes \"" + member.Name
                        + "\", which the validator does not read. The members it reads are "
                        + string.Join(", ", MembersTheValidatorReads) + ".");
            }
        }
    }

    /// <summary>
    /// The pages a reader is sent to for a name, named here so this page cannot outlive them.
    /// </summary>
    [Fact]
    public void ThePageSendsAReaderToTheFieldAndOperatorPages()
    {
        var page = PageText();

        foreach (var reference in new[] { "rule-fields.md", "rule-operators.md", "rule-language.md" })
        {
            Assert.Contains(reference, page, StringComparison.Ordinal);
            Assert.True(
                File.Exists(Path.Combine(RepositoryFiles.Root(), "docs", reference)),
                Page + " names " + reference + " and no such file is in the tree.");
        }
    }

    /// <summary>
    /// A page nobody is sent to is a page nobody reads. The front page carries the pointer and
    /// this holds both halves of it, so a link to a file that left the tree reds as loudly as a
    /// file the front page stopped naming.
    /// </summary>
    [Fact]
    public void TheFrontPageSendsTheReaderToTheWorkedDocuments()
    {
        Assert.Contains(
            "(" + Page + ")",
            RepositoryFiles.ReadFromRoot("README.md"),
            StringComparison.Ordinal);

        Assert.True(
            File.Exists(Path.Combine(RepositoryFiles.Root(), Page.Replace('/', Path.DirectorySeparatorChar))),
            "README.md links " + Page + " and no such file is in the tree.");
    }

    /// <summary>
    /// Walks a rule for the group members the composition reader accepts. It descends rather than
    /// reading the top level alone, because the nesting is the thing one of the examples exists to
    /// show and a top-level read would not see it.
    /// </summary>
    /// <param name="element">The element to walk.</param>
    /// <param name="found">The group names found so far.</param>
    private static void CollectGroups(JsonElement element, SortedSet<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    if (RuleCompositionReader.GroupNames.Contains(member.Name, StringComparer.Ordinal))
                    {
                        found.Add(member.Name);
                    }

                    CollectGroups(member.Value, found);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectGroups(item, found);
                }

                break;

            default:
                break;
        }
    }
}
