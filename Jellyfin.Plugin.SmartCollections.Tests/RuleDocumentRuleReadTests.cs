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
    /// WHAT THIS WIRING DELIBERATELY DOES NOT DECIDE. A document declaring a scope and no rule is
    /// accepted, which is the answer it got before the stages were wired in. Refusing it and
    /// reading it as a rule that collects the whole declared scope are both defensible, the schema
    /// requires the member neither way, and no issue on this tracker takes the question, so the
    /// behaviour is held where it was rather than moved by a change that is about something else.
    /// This test exists so that whoever takes that decision meets a named assertion rather than a
    /// silence.
    /// </summary>
    [Fact]
    public void ADocumentDeclaringNoRuleIsAcceptedAndThatIsUndecidedRatherThanIntended()
    {
        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Christmas\", \"collects\": [\"movie\"]}");

        Assert.True(result.IsValid, Because(result));
    }

    private static string Because(RuleDocumentValidation result)
        => "Refused with: " + string.Join(" | ", result.Errors.Select(error => error.ToString()));
}
