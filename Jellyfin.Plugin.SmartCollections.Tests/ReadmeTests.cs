using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
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
    /// not a sketch of one. This test reads the envelope: that the example parses, and that it
    /// carries the members every document carries. What the rule inside it says is read by
    /// <see cref="TheExampleWritesConditionsInTheVocabularyTheEngineDeclares"/>, against the
    /// tables rather than against a list written here.
    ///
    /// THIS REMARK SAID THE SCHEMA THE EXAMPLE WOULD BE VALIDATED AGAINST DID NOT EXIST YET, and
    /// that a parse was all that was checkable. A schema is in the tree and so are the tables the
    /// example's own values answer to. What is still absent is a validator that reads a rule, and
    /// that is a different absence from the one this sentence named.
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
    /// The scope in the example is one the scope stage takes. Reading the member and stopping
    /// there passes on a front page naming a kind the plugin refuses, which is worse than naming
    /// none: a reader copies the example, meets a refusal, and looks for the fault in their own
    /// document.
    /// </summary>
    [Fact]
    public void TheExampleDeclaresAScopeTheEngineAccepts()
    {
        var match = FirstJsonBlock.Match(RepositoryFiles.ReadFromRoot("README.md"));

        Assert.True(match.Success, "README.md carries no fenced json block.");

        using var document = JsonDocument.Parse(match.Groups["json"].Value);
        var scope = RuleItemScopeReader.Read(document.RootElement);

        Assert.True(scope.IsAccepted, "Refused with: " + string.Join(" | ", scope.Errors));
        Assert.NotEmpty(scope.Kinds);
    }

    /// <summary>
    /// The rule inside the example is written in the vocabulary the engine declares: every group
    /// is one of the composition stage's members, every field and every operator is a row of its
    /// table, and each field's row accepts the operator written beside it.
    /// </summary>
    /// <remarks>
    /// The names are read out of the tables rather than listed here, so a name removed from a
    /// table tomorrow reds the front page instead of leaving it behind. What this test cannot see
    /// is the members no table decides yet, which are the ones the envelope does not declare
    /// either, so it reads the rule and stops there.
    ///
    /// The condition count is asserted because the walk over an empty group agrees with the walk
    /// over a correct one, and an example somebody emptied would pass a comparison that only
    /// refuses what it meets.
    /// </remarks>
    [Fact]
    public void TheExampleWritesConditionsInTheVocabularyTheEngineDeclares()
    {
        var match = FirstJsonBlock.Match(RepositoryFiles.ReadFromRoot("README.md"));

        Assert.True(match.Success, "README.md carries no fenced json block.");

        using var document = JsonDocument.Parse(match.Groups["json"].Value);

        Assert.True(
            document.RootElement.TryGetProperty("match", out var rule),
            "The example declares no match.");

        var conditions = 0;
        ReadGroup(rule, "/match", ref conditions);

        Assert.True(conditions > 0, "The example's rule declares no condition.");
    }

    /// <summary>
    /// Walks one group of the example, counting the conditions it reaches.
    /// </summary>
    /// <param name="group">The group object.</param>
    /// <param name="pointer">Where it sits, for a message that names the place.</param>
    /// <param name="conditions">Running count of the conditions reached.</param>
    private static void ReadGroup(JsonElement group, string pointer, ref int conditions)
    {
        Assert.Equal(JsonValueKind.Object, group.ValueKind);

        foreach (var member in group.EnumerateObject())
        {
            Assert.True(
                RuleCompositionReader.GroupNames.Contains(member.Name, StringComparer.Ordinal),
                pointer + " carries " + member.Name + ", which is not one of "
                    + string.Join(", ", RuleCompositionReader.GroupNames) + ".");

            Assert.Equal(JsonValueKind.Array, member.Value.ValueKind);

            var index = 0;

            foreach (var element in member.Value.EnumerateArray())
            {
                var at = pointer + "/" + member.Name + "/" + index.ToString(CultureInfo.InvariantCulture);

                if (element.TryGetProperty(RuleFieldReader.FieldMember, out _))
                {
                    ReadCondition(element, at);
                    conditions++;
                }
                else
                {
                    ReadGroup(element, at, ref conditions);
                }

                index++;
            }
        }
    }

    /// <summary>
    /// Reads one condition of the example against the two tables.
    /// </summary>
    /// <param name="condition">The condition object.</param>
    /// <param name="pointer">Where it sits, for a message that names the place.</param>
    private static void ReadCondition(JsonElement condition, string pointer)
    {
        var fieldName = condition.GetProperty(RuleFieldReader.FieldMember).GetString();
        var field = fieldName is null ? null : RuleFieldTable.Find(fieldName);

        Assert.True(
            field is not null,
            pointer + " names the field " + fieldName + ", which the field table does not declare. It declares "
                + string.Join(", ", RuleFieldTable.Names) + ".");

        Assert.True(
            condition.TryGetProperty(RuleOperatorReader.OperatorMember, out var written),
            pointer + " names no operator.");

        var operatorName = written.GetString();
        var row = operatorName is null ? null : RuleOperatorTable.Find(operatorName);

        Assert.True(
            row is not null,
            pointer + " names the operator " + operatorName + ", which the operator table does not declare. It declares "
                + string.Join(", ", RuleOperatorTable.Names) + ".");

        Assert.True(
            RuleFieldTable.Accepts(field!.Field, row!.Operator),
            pointer + " applies " + row.Name + " to " + field.Name + ", which accepts "
                + RuleFieldTable.OperatorNames(field) + ".");
    }

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

    /// <summary>
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
