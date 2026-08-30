using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The shape of a composition: which groups hold what, how deep it goes, and what is refused. A
/// condition is a place in the document here and nothing more, so every fixture below writes one
/// as an object the reader has no opinion about.
/// </summary>
public class RuleCompositionReaderTests
{
    private const string Root = "/match";

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static RuleConditionGroup Accepted(string text)
    {
        var read = RuleCompositionReader.Read(Json(text), Root);

        Assert.True(read.IsAccepted, string.Join(" | ", read.Errors.Select(e => e.ToString())));
        Assert.Empty(read.Errors);
        Assert.NotNull(read.Group);
        return read.Group!;
    }

    private static RuleCompositionRead Refused(string text)
    {
        var read = RuleCompositionReader.Read(Json(text), Root);

        Assert.False(read.IsAccepted);
        Assert.Null(read.Group);
        Assert.NotEmpty(read.Errors);
        return read;
    }

    [Theory]
    [InlineData("allOf", RuleConditionGroupKind.All)]
    [InlineData("anyOf", RuleConditionGroupKind.Any)]
    [InlineData("noneOf", RuleConditionGroupKind.None)]
    public void EachGroupIsReadAsItsKind(string name, RuleConditionGroupKind kind)
    {
        var group = Accepted("{\"" + name + "\": [{\"field\": \"studio\"}]}");

        Assert.Equal(kind, group.Kind);
        Assert.Equal(Root, group.Pointer);
        Assert.Equal(name, RuleCompositionReader.NameOf(kind));
    }

    /// <summary>
    /// A condition is carried as the place it sits and not as its content, so the pointer is what
    /// a later stage reads it back by.
    /// </summary>
    [Fact]
    public void AConditionIsCarriedAsWhereItIs()
    {
        var group = Accepted("{\"allOf\": [{\"field\": \"studio\"}, {\"field\": \"year\"}]}");

        Assert.Equal(new[] { "/match/allOf/0", "/match/allOf/1" }, group.ConditionPointers);
        Assert.Empty(group.Groups);
        Assert.Equal(2, group.MemberCount);
    }

    [Fact]
    public void AGroupInsideAGroupIsReadAsAGroup()
    {
        var group = Accepted("{\"allOf\": [{\"field\": \"studio\"}, {\"anyOf\": [{\"field\": \"year\"}]}]}");

        Assert.Equal(new[] { "/match/allOf/0" }, group.ConditionPointers);
        var nested = Assert.Single(group.Groups);
        Assert.Equal(RuleConditionGroupKind.Any, nested.Kind);
        Assert.Equal("/match/allOf/1", nested.Pointer);
        Assert.Equal(new[] { "/match/allOf/1/anyOf/0" }, nested.ConditionPointers);
        Assert.Equal(2, group.MemberCount);
    }

    /// <summary>
    /// The tree keeps the order the document wrote. That is not the compiled form being
    /// independent of that order, which is the compiler's property and is owed where the compiler
    /// is; this is the weaker thing that has to hold first.
    /// </summary>
    [Fact]
    public void TheTreeKeepsTheOrderTheDocumentWrote()
    {
        var first = Accepted("{\"allOf\": [{\"field\": \"a\"}, {\"field\": \"b\"}, {\"field\": \"c\"}]}");
        var second = Accepted("{\"allOf\": [{\"field\": \"c\"}, {\"field\": \"b\"}, {\"field\": \"a\"}]}");

        Assert.Equal(first.ConditionPointers, second.ConditionPointers);
        Assert.Equal(3, first.MemberCount);
    }

    [Fact]
    public void AGroupAtTheDeepestAllowedLevelIsAccepted()
    {
        var text = "{\"field\": \"studio\"}";

        for (var level = 0; level < RuleCompositionReader.MaximumNestingDepth; level++)
        {
            text = "{\"allOf\": [" + text + "]}";
        }

        var group = Accepted(text);

        var depth = 1;
        while (group.Groups.Count > 0)
        {
            group = group.Groups[0];
            depth++;
        }

        Assert.Equal(RuleCompositionReader.MaximumNestingDepth, depth);
    }

    /// <summary>
    /// The refusal names the limit and where the document broke it, because "too deep" with
    /// neither is a message an operator cannot act on in a file of nested braces.
    /// </summary>
    [Fact]
    public void AGroupPastTheLimitIsRefusedNamingTheLimitAndTheLocation()
    {
        var text = "{\"field\": \"studio\"}";

        for (var level = 0; level <= RuleCompositionReader.MaximumNestingDepth; level++)
        {
            text = "{\"allOf\": [" + text + "]}";
        }

        var error = Assert.Single(Refused(text).Errors);

        Assert.Equal("/match/allOf/0/allOf/0/allOf/0/allOf/0", error.Pointer);
        Assert.Equal(
            "This group is nested 5 deep and a rule nests at most 4. A rule nested deeper than that is one nobody can check by reading, which is what a declared rule is for.",
            error.Message);
    }

    [Fact]
    public void AnEmptyGroupIsRefusedRatherThanReadAsEverythingOrNothing()
    {
        var error = Assert.Single(Refused("{\"anyOf\": []}").Errors);

        Assert.Equal("/match/anyOf", error.Pointer);
        Assert.StartsWith("This group holds nothing.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyGroupInsideAGroupIsRefusedToo()
    {
        var error = Assert.Single(Refused("{\"allOf\": [{\"field\": \"a\"}, {\"noneOf\": []}]}").Errors);

        Assert.Equal("/match/allOf/1/noneOf", error.Pointer);
    }

    [Fact]
    public void AnObjectCarryingTwoGroupMembersIsRefusedRatherThanOrdered()
    {
        var error = Assert.Single(Refused("{\"allOf\": [{\"field\": \"a\"}], \"anyOf\": [{\"field\": \"b\"}]}").Errors);

        Assert.Equal(Root, error.Pointer);
        Assert.Equal(
            "This object carries 2 of allOf, anyOf, noneOf, and a group carries exactly one. Nest the second one inside the first.",
            error.Message);
    }

    [Fact]
    public void AnObjectCarryingNoGroupMemberIsNotAGroup()
    {
        var error = Assert.Single(Refused("{\"field\": \"studio\"}").Errors);

        Assert.Equal(Root, error.Pointer);
        Assert.Equal("This object carries none of allOf, anyOf, noneOf, so it is not a group.", error.Message);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"allOf\"")]
    [InlineData("12")]
    [InlineData("null")]
    [InlineData("true")]
    public void SomethingThatIsNotAnObjectIsNotAGroup(string text)
    {
        var error = Assert.Single(Refused(text).Errors);

        Assert.Equal(Root, error.Pointer);
        Assert.Equal("A group is a JSON object carrying one of allOf, anyOf, noneOf.", error.Message);
    }

    [Fact]
    public void AGroupWhoseMembersAreNotAnArrayIsRefused()
    {
        var error = Assert.Single(Refused("{\"allOf\": {\"field\": \"studio\"}}").Errors);

        Assert.Equal("/match/allOf", error.Pointer);
        Assert.Equal("A group holds an array of groups and conditions.", error.Message);
    }

    [Fact]
    public void AMemberThatIsNotAnObjectIsRefusedWhereItSits()
    {
        var error = Assert.Single(Refused("{\"allOf\": [{\"field\": \"a\"}, \"year > 2000\"]}").Errors);

        Assert.Equal("/match/allOf/1", error.Pointer);
        Assert.Equal("A group holds groups and conditions, and both of those are JSON objects.", error.Message);
    }

    /// <summary>
    /// Every reason rather than the first. A composition is where typing mistakes collect, and a
    /// stage reporting one per run turns repairing a file into a sequence of edits and re-reads.
    /// </summary>
    [Fact]
    public void EveryReasonIsReportedRatherThanTheFirst()
    {
        var read = Refused("{\"allOf\": [12, {\"anyOf\": []}, true, {\"field\": \"a\"}]}");

        Assert.Equal(
            new[] { "/match/allOf/0", "/match/allOf/1/anyOf", "/match/allOf/2" },
            read.Errors.Select(error => error.Pointer).ToArray());
    }

    [Fact]
    public void ANameIsDeclaredForEveryGroupKindAndForNothingElse()
    {
        Assert.Equal(
            RuleCompositionReader.GroupNames.OrderBy(name => name, StringComparer.Ordinal),
            Enum.GetValues<RuleConditionGroupKind>()
                .Select(RuleCompositionReader.NameOf)
                .OrderBy(name => name, StringComparer.Ordinal));

        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => RuleCompositionReader.NameOf((RuleConditionGroupKind)(-1)));

        Assert.Equal("kind", thrown.ParamName);
    }

    [Fact]
    public void AReadWithNoPointerIsRefusedAtTheCall()
    {
        Assert.Throws<ArgumentNullException>(() => RuleCompositionReader.Read(Json("{\"allOf\": []}"), null!));
    }

    [Fact]
    public void ARefusedCompositionCarriesTheErrorsAndNoTree()
    {
        var read = RuleCompositionRead.Refused([new RuleValidationError("/match", "It is not a group.")]);

        Assert.False(read.IsAccepted);
        Assert.Null(read.Group);
        Assert.Equal("/match: It is not a group.", Assert.Single(read.Errors).ToString());
    }
}
