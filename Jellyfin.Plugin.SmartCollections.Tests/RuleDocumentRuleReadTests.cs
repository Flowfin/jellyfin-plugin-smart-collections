using System;
using System.Linq;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// What the validator answers about what the envelope carries. The envelope's own refusals are
/// held in <see cref="RuleDocumentValidatorTests"/>; these are the ones a document earns by
/// declaring a scope or a rule the vocabulary does not.
/// </summary>
/// <remarks>
/// The failure every test here is against is one shape: a document whose envelope is intact and
/// whose rule is nonsense being reported as loaded. A loaded rule owns a collection, so an
/// operator whose document is accepted and does nothing has no row to look at, no error to read
/// and a collection that quietly stopped updating, which is the state one document per collection
/// exists to prevent.
///
/// The stages themselves are asserted in their own suites, over the same vocabulary. What is
/// asserted here is that a document reaches them at all, so each test names one stage and reads
/// only that the refusal came back with the pointer or the name that stage writes.
/// </remarks>
public class RuleDocumentRuleReadTests
{
    /// <summary>
    /// A rule every stage accepts, so the refusals below are about the member each test changes
    /// rather than about something else in the same document.
    /// </summary>
    private const string Sound = """
        {
          "schemaVersion": 1,
          "id": "thrillers-of-1994",
          "name": "Thrillers of 1994",
          "collects": ["movie"],
          "match": {
            "allOf": [
              { "field": "genres", "operator": "contains", "value": "Thriller" },
              { "field": "productionYear", "operator": "equals", "value": 1994 }
            ]
          }
        }
        """;

    [Fact]
    public void ADocumentWhoseRuleEveryStageAcceptsIsAccepted()
    {
        var result = RuleDocumentValidator.Read(Sound);

        Assert.True(result.IsValid, Because(result));
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// The document the reading on the tracker was taken with: a group carrying two kinds at once,
    /// a field no table declares, an operator this plugin refuses by name and an empty value list.
    /// Every one of those is refused by a stage that exists, and the validator accepted the whole
    /// of it until the stages were wired in.
    /// </summary>
    [Fact]
    public void ADocumentWhoseRuleIsNonsenseIsRefusedRatherThanLoaded()
    {
        const string Text = """
            {
              "schemaVersion": 1,
              "id": "a",
              "name": "A",
              "collects": ["movie"],
              "match": { "allOf": [ { "field": "nosuchfield", "operator": "matchRegex", "value": [] } ], "anyOf": [] }
            }
            """;

        var result = RuleDocumentValidator.Read(Text);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// The composition stage. A group writing two kinds at once is the shape it refuses, and the
    /// pointer says which group rather than which document.
    /// </summary>
    [Fact]
    public void AGroupWritingTwoKindsAtOnceIsRefusedByTheCompositionStage()
    {
        var result = RuleDocumentValidator.Read(
            Sound.Replace(
                "\"allOf\": [",
                "\"anyOf\": [], \"allOf\": [",
                StringComparison.Ordinal));

        Assert.False(result.IsValid);
        Assert.All(
            result.Errors,
            error => Assert.StartsWith("/" + RuleDocumentValidator.MatchMember, error.Pointer, StringComparison.Ordinal));
    }

    /// <summary>
    /// The field stage, which is where a name outside the vocabulary is caught.
    /// </summary>
    [Fact]
    public void AFieldNoTableDeclaresIsRefusedByTheFieldStage()
    {
        var result = RuleDocumentValidator.Read(
            Sound.Replace("\"genres\"", "\"nosuchfield\"", StringComparison.Ordinal));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Message.Contains("nosuchfield", StringComparison.Ordinal));
    }

    /// <summary>
    /// The operator stage. <c>matchRegex</c> is the name this plugin refuses deliberately, so it is
    /// the one worth asserting reaches a refusal rather than a collection nobody can explain.
    /// </summary>
    [Fact]
    public void AnOperatorNoOperatorSetHoldsIsRefusedByTheOperatorStage()
    {
        var result = RuleDocumentValidator.Read(
            Sound.Replace("\"contains\"", "\"matchRegex\"", StringComparison.Ordinal));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Message.Contains("matchRegex", StringComparison.Ordinal));
    }

    /// <summary>
    /// The value stage, which is reached only once the three stages before it accepted, so this is
    /// the assertion that the wiring runs all four rather than the first.
    /// </summary>
    [Fact]
    public void AValueTheFieldCannotTakeIsRefusedByTheValueStage()
    {
        var result = RuleDocumentValidator.Read(
            Sound.Replace("\"value\": 1994", "\"value\": \"nineteen ninety four\"", StringComparison.Ordinal));

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// The scope stage. This is the member the shipped schema requires and the validator did not
    /// read, so a document an editor refuses against the schema was one the plugin loaded.
    /// </summary>
    [Fact]
    public void ADocumentDeclaringNoScopeIsRefusedByTheScopeStage()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Christmas\"}");

        var error = Assert.Single(result.Errors);
        Assert.Equal("/" + RuleItemScopeReader.CollectsMember, error.Pointer);
    }

    /// <summary>
    /// The scope is read before the rule, so a document wrong in both is reported for its scope.
    /// Held as a test rather than left to the order of two calls, because an operator repairing a
    /// rule against a message about a scope repairs the member they were not told about.
    /// </summary>
    [Fact]
    public void ADocumentWrongInBothItsScopeAndItsRuleIsReportedForItsScope()
    {
        var result = RuleDocumentValidator.Read(
            Sound
                .Replace("[\"movie\"]", "[]", StringComparison.Ordinal)
                .Replace("\"genres\"", "\"nosuchfield\"", StringComparison.Ordinal));

        var error = Assert.Single(result.Errors);
        Assert.Equal("/" + RuleItemScopeReader.CollectsMember, error.Pointer);
    }

    /// <summary>
    /// THE QUESTION THIS WIRING LEFT OPEN IS DECIDED. A document declaring a scope and no rule is
    /// REFUSED, decided on #231 on 2026-09-04. It used to be accepted, and the test that stood
    /// here said so and said the question was open; the assertion it asks whoever takes the
    /// decision to move is this one, moved.
    ///
    /// The two readings it was decided against are worth keeping beside the answer. Reading it as
    /// a rule that collects the whole declared scope makes a misspelled member name a collection
    /// holding every film somebody owns, which is the expensive silent failure. Leaving it
    /// accepted makes a document that collects nothing, which nobody writes on purpose, and makes
    /// it indistinguishable from the misspelling.
    /// </summary>
    [Fact]
    public void ADocumentDeclaringNoRuleIsRefusedAndTheMessageNamesTheMember()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Christmas\", \"collects\": [\"movie\"]}");

        Assert.False(result.IsValid, "The document was accepted.");

        var error = Assert.Single(result.Errors);

        Assert.Equal("/" + RuleDocumentValidator.MatchMember, error.Pointer);
        Assert.Contains(
            "The document declares no " + RuleDocumentValidator.MatchMember + ".",
            error.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The near miss the decision is about: a document whose rule member is MISSPELLED is refused
    /// too, and for a different reason, so the two answers are told apart. This is the pair #231
    /// opened on, where both documents got one answer.
    /// </summary>
    [Fact]
    public void AMisspelledRuleMemberIsRefusedForBeingAMemberThisVersionDoesNotDeclare()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Christmas\", \"collects\": [\"movie\"], \"mach\": {}}");

        var error = Assert.Single(result.Errors);

        Assert.Equal("/mach", error.Pointer);
        Assert.Contains("declares no member called \"mach\"", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("declares no match.", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A document that carries a rule is unmoved by the refusal above. Without this the pair could
    /// pass on a stage that refuses every document it reads.
    /// </summary>
    [Fact]
    public void ADocumentThatCarriesARuleIsStillAccepted()
        => Assert.True(RuleDocumentValidator.Read(Sound).IsValid);

    private static string Because(RuleDocumentValidation result)
        => "Refused with: " + string.Join(" | ", result.Errors.Select(error => error.ToString()));
}
