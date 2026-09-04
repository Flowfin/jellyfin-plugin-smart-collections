using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A document that reaches for a construct this rule language refuses is refused, and the message
/// says which refusal it ran into.
/// </summary>
/// <remarks>
/// The refusals are written down in <c>docs/rule-language.md</c> and <see cref="RuleLanguageRefusalTests"/>
/// holds that page. What those tests cannot see is whether the plugin says anything about a
/// refusal to the person who ran into one: a document naming <c>isPlayed</c> was refused before
/// this suite existed, with a message listing the fields that do exist and nothing in it to tell a
/// construct decided against from a row nobody has added yet. These tests are that difference.
///
/// Every case below drives <see cref="RuleDocumentValidator.Read(string)"/>, which is the entry
/// point the rules directory scan hands a file to, so what is asserted is the message an operator
/// reads rather than a string built inside a test.
/// </remarks>
public class RuleRefusalMessageTests
{
    private const string Head =
        "{\"schemaVersion\":1,\"id\":\"probe\",\"name\":\"Probe\",\"collects\":[\"movie\"],";

    private static readonly Regex MarkerLine = new(
        @"^## Refusal: (?<name>.+?)\s*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// One document per refusal, written the way somebody reaching for that construct writes it.
    /// </summary>
    /// <remarks>
    /// Six rather than the seven the reference carries. The seventh is the one no document can
    /// write, and <see cref="TheOneRefusalWithNoDocumentConstructIsNamedRatherThanMissing"/> is
    /// where that is held rather than assumed.
    /// </remarks>
    /// <returns>The refusal each document reaches for, and the document.</returns>
    public static TheoryData<string, string> Documents() => new()
    {
        {
            "regular expressions",
            Head + "\"match\":{\"allOf\":[{\"field\":\"name\",\"operator\":\"matches\",\"value\":\"^The\"}]}}"
        },
        {
            "arbitrary expressions",
            Head + "\"match\":{\"allOf\":[{\"expression\":\"item.Name.Length > 5\"}]}}"
        },
        {
            "cross-item aggregates",
            Head + "\"match\":{\"allOf\":[{\"field\":\"name\",\"operator\":\"countGreaterThan\",\"value\":5}]}}"
        },
        {
            "references between collections",
            Head + "\"match\":{\"allOf\":[{\"field\":\"inCollection\",\"operator\":\"equals\",\"value\":\"Given\"}]}}"
        },
        {
            "fields describing one person's viewing",
            Head + "\"match\":{\"allOf\":[{\"field\":\"isPlayed\",\"operator\":\"equals\",\"value\":true}]}}"
        },
        {
            "pinning an item into a collection",
            Head + "\"match\":{\"allOf\":[{\"pinned\":[\"3fa85f64\"]}]}}"
        }
    };

    /// <summary>
    /// The refusal is named, and the reader is sent to where it is argued.
    /// </summary>
    /// <param name="refusal">The refusal the document reaches for.</param>
    /// <param name="text">The document.</param>
    [Theory]
    [MemberData(nameof(Documents))]
    public void ADocumentReachingForARefusedConstructIsRefusedAndTheMessageNamesTheRefusal(
        string refusal,
        string text)
    {
        var result = RuleDocumentValidator.Read(text);

        Assert.False(result.IsValid, "The document was accepted, so no message named anything.");

        var message = Assert.Single(result.Errors).Message;

        Assert.Contains("\"" + refusal + "\"", message, StringComparison.Ordinal);
        Assert.Contains(RuleRefusalTable.Reference, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The note is added to the message rather than replacing it. Somebody repairing a document
    /// needs the list they are choosing from as well as the thing they ran into.
    /// </summary>
    [Fact]
    public void TheRefusalNoteIsAddedToTheMessageRatherThanReplacingIt()
    {
        var result = RuleDocumentValidator.Read(
            Head + "\"match\":{\"allOf\":[{\"field\":\"isPlayed\",\"operator\":\"equals\",\"value\":true}]}}");

        var message = Assert.Single(result.Errors).Message;

        Assert.Contains("There is no field called \"isPlayed\".", message, StringComparison.Ordinal);
        Assert.Contains("The fields are ", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The near miss, and the reason this suite exists at all. A field the vocabulary does not
    /// hold is ABSENT rather than refused, and adding one is a row and a test. A message naming a
    /// refusal there would tell somebody their request had been decided against when nobody has
    /// looked at it.
    /// </summary>
    /// <param name="field">A field name no table declares and no refusal reaches for.</param>
    [Theory]
    [InlineData("director")]
    [InlineData("studio")]
    [InlineData("audioLanguage")]
    public void AFieldThatIsMerelyAbsentIsRefusedWithoutARefusalBeingNamed(string field)
    {
        var result = RuleDocumentValidator.Read(
            Head + "\"match\":{\"allOf\":[{\"field\":\"" + field + "\",\"operator\":\"equals\",\"value\":\"x\"}]}}");

        var message = Assert.Single(result.Errors).Message;

        Assert.Contains("There is no field called \"" + field + "\".", message, StringComparison.Ordinal);
        Assert.DoesNotContain(RuleRefusalTable.Reference, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same near miss on the operator surface.
    /// </summary>
    /// <param name="written">An operator name no table declares and no refusal reaches for.</param>
    [Theory]
    [InlineData("isOneOf")]
    [InlineData("between")]
    public void AnOperatorThatIsMerelyAbsentIsRefusedWithoutARefusalBeingNamed(string written)
    {
        var result = RuleDocumentValidator.Read(
            Head + "\"match\":{\"allOf\":[{\"field\":\"name\",\"operator\":\"" + written + "\",\"value\":\"x\"}]}}");

        var message = Assert.Single(result.Errors).Message;

        Assert.DoesNotContain(RuleRefusalTable.Reference, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name in the refusal table may not also be a declared field or operator. One that was
    /// would resolve at the lookup ahead of the refusal, so the note would be dead text, and a
    /// declared name would be the one carrying it if the order ever changed.
    /// </summary>
    [Fact]
    public void NoRefusedNameIsAlsoADeclaredFieldOrOperator()
    {
        foreach (var row in RuleRefusalTable.Rows)
        {
            foreach (var name in row.Names)
            {
                Assert.Null(RuleFieldTable.Find(name));
                Assert.Null(RuleOperatorTable.Find(name));
            }
        }
    }

    /// <summary>
    /// One name reaches one refusal. A name in two rows would make which refusal a document is
    /// told about depend on the order the table happens to be written in.
    /// </summary>
    [Fact]
    public void NoNameReachesTwoRefusals()
    {
        var names = RuleRefusalTable.Rows.SelectMany(row => row.Names).ToList();

        Assert.Equal(names.Count, new HashSet<string>(names, StringComparer.Ordinal).Count);
    }

    /// <summary>
    /// Every refusal the table declares is argued in the reference under exactly that heading. The
    /// message sends a reader to that page by name, so a heading that moved would send them
    /// looking for something that is not there.
    /// </summary>
    [Fact]
    public void EveryRefusalTheTableDeclaresIsHeadedInTheReference()
    {
        var reference = RepositoryFiles.ReadFromRoot(RuleRefusalTable.Reference);
        var headed = MarkerLine.Matches(reference).Select(m => m.Groups["name"].Value).ToArray();

        foreach (var row in RuleRefusalTable.Rows)
        {
            Assert.Contains(row.Refusal, headed, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// The other direction, with the one deliberate difference asserted rather than skipped.
    /// </summary>
    /// <remarks>
    /// The reference argues seven refusals and the table holds six. The wall clock as an implicit
    /// input is refused OF THE ENGINE rather than of a document: relative dates are allowed, and
    /// what is refused is reading the clock during a match, so there is no member, name or value a
    /// rule document can write to ask for it. What holds that refusal is the compiler taking the
    /// instant as an argument. Naming the remainder exactly is what stops a refusal added to the
    /// reference tomorrow from silently having no name at all.
    /// </remarks>
    [Fact]
    public void TheOneRefusalWithNoDocumentConstructIsNamedRatherThanMissing()
    {
        var reference = RepositoryFiles.ReadFromRoot(RuleRefusalTable.Reference);
        var headed = MarkerLine.Matches(reference).Select(m => m.Groups["name"].Value);
        var held = RuleRefusalTable.Rows.Select(row => row.Refusal).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            ["the wall clock as an implicit input"],
            headed.Where(name => !held.Contains(name)).ToArray());
    }
}
