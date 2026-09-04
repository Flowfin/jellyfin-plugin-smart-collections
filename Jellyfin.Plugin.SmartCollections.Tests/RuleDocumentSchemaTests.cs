using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The shipped schema is what an editor points at, and the validator is what actually refuses a
/// document. Two declarations of one format drift, and the drift is silent: an editor keeps
/// accepting a version the plugin has stopped reading, or refuses one it has started reading.
/// These tests hold the file to the constants the validator declares, so raising a version
/// without touching the schema reds here rather than on somebody's server.
/// </summary>
public class RuleDocumentSchemaTests
{
    private const string SchemaPath =
        "Jellyfin.Plugin.SmartCollections.Engine/Rules/rule-document.schema.json";

    private static JsonElement Schema()
    {
        var text = RepositoryFiles.ReadFromRoot(SchemaPath);
        using var parsed = JsonDocument.Parse(text);
        return parsed.RootElement.Clone();
    }

    [Fact]
    public void TheSchemaShipsInTheRepositoryAndParses()
    {
        var schema = Schema();

        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.Equal("object", schema.GetProperty("type").GetString());
    }

    [Fact]
    public void TheSchemaRequiresTheVersionMemberTheValidatorRequires()
    {
        var required = Schema().GetProperty("required");

        Assert.Contains(
            required.EnumerateArray(),
            member => string.Equals(
                member.GetString(),
                RuleDocumentValidator.SchemaVersionMember,
                StringComparison.Ordinal));
    }

    [Fact]
    public void TheSchemaDeclaresTheSameVersionBoundsTheValidatorReads()
    {
        var member = Schema()
            .GetProperty("properties")
            .GetProperty(RuleDocumentValidator.SchemaVersionMember);

        Assert.Equal("integer", member.GetProperty("type").GetString());
        Assert.Equal(RuleDocumentValidator.LowestSchemaVersion, member.GetProperty("minimum").GetInt32());
        Assert.Equal(RuleDocumentValidator.CurrentSchemaVersion, member.GetProperty("maximum").GetInt32());
    }

    /// <summary>
    /// The document the schema declares is one the validator accepts. Without this the two could
    /// agree on their bounds and disagree on everything else.
    /// </summary>
    [Fact]
    public void ADocumentAtTheSchemasUpperBoundIsAcceptedByTheValidator()
    {
        var maximum = Schema()
            .GetProperty("properties")
            .GetProperty(RuleDocumentValidator.SchemaVersionMember)
            .GetProperty("maximum")
            .GetInt32();

        var result = RuleDocumentValidator.Read(
            "{\"" + RuleDocumentValidator.SchemaVersionMember + "\": " + maximum
            + ", \"" + RuleDocumentValidator.IdMember + "\": \"christmas\""
            + ", \"" + RuleDocumentValidator.NameMember + "\": \"Christmas\", \"" + RuleItemScopeReader.CollectsMember + "\": [\"movie\"], \""
            + RuleDocumentValidator.MatchMember + "\": {\"allOf\": [{\"field\": \"genres\", \"operator\": \"contains\", \"value\": \"Thriller\"}]}}");

        Assert.True(result.IsValid, "Refused with: " + string.Join(" | ", result.Errors));
    }

    [Fact]
    public void TheSchemaRequiresTheIdMemberTheValidatorRequires()
    {
        var required = Schema().GetProperty("required");

        Assert.Contains(
            required.EnumerateArray(),
            member => string.Equals(
                member.GetString(),
                RuleDocumentValidator.IdMember,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The editor's copy of the id bounds and the validator's are the same numbers. An editor
    /// accepting an id the plugin refuses costs an operator a document they were told was fine.
    /// </summary>
    [Fact]
    public void TheSchemaDeclaresTheSameIdBoundsTheValidatorReads()
    {
        var member = Schema()
            .GetProperty("properties")
            .GetProperty(RuleDocumentValidator.IdMember);

        Assert.Equal("string", member.GetProperty("type").GetString());
        Assert.Equal(1, member.GetProperty("minLength").GetInt32());
        Assert.Equal(RuleDocumentValidator.MaximumIdLength, member.GetProperty("maxLength").GetInt32());
    }

    /// <summary>
    /// The one member of this format a schema processor can express in full, and therefore the one
    /// place the two declarations can disagree silently on a document rather than on a bound. The
    /// pattern and the validator are asked the same question about the same strings, so a set
    /// widened in one and not the other reds here rather than on somebody's editor.
    /// </summary>
    [Theory]
    [InlineData("nineties-thrillers", true)]
    [InlineData("a", true)]
    [InlineData("0", true)]
    [InlineData("-", true)]
    [InlineData("Christmas", false)]
    [InlineData("christmas films", false)]
    [InlineData("christmas_films", false)]
    [InlineData("christmas.films", false)]
    [InlineData("weihnachtsfilme-f\u00fcr-alle", false)]
    [InlineData("", false)]
    public void TheSchemasPatternAndTheValidatorAgreeOnWhatAnIdMayHold(string id, bool expected)
    {
        var pattern = Schema()
            .GetProperty("properties")
            .GetProperty(RuleDocumentValidator.IdMember)
            .GetProperty("pattern")
            .GetString();

        // A timeout because this pattern is read out of a file rather than written here, which is
        // the same reason the engine may not construct one without a bound.
        var bySchema = Regex.IsMatch(id, pattern!, RegexOptions.None, TimeSpan.FromSeconds(1));

        var byValidator = RuleDocumentValidator
            .Read("{\"schemaVersion\": 1, \"id\": \"" + id + "\", \"name\": \"Christmas\", \"collects\": [\"movie\"], \"match\": {\"allOf\": [{\"field\": \"genres\", \"operator\": \"contains\", \"value\": \"Thriller\"}]}}")
            .IsValid;

        Assert.Equal(expected, bySchema);
        Assert.Equal(bySchema, byValidator);
    }

    /// <summary>
    /// The same crossing as the version bound, on the length rather than on the set: the longest
    /// id the schema permits is one the validator takes.
    /// </summary>
    [Fact]
    public void AnIdAtTheSchemasLengthBoundIsAcceptedByTheValidator()
    {
        var maxLength = Schema()
            .GetProperty("properties")
            .GetProperty(RuleDocumentValidator.IdMember)
            .GetProperty("maxLength")
            .GetInt32();

        var result = RuleDocumentValidator.Read(
            "{\"" + RuleDocumentValidator.SchemaVersionMember + "\": 1, \""
            + RuleDocumentValidator.IdMember + "\": \"" + new string('a', maxLength) + "\", \""
            + RuleDocumentValidator.NameMember + "\": \"Christmas\", \"" + RuleItemScopeReader.CollectsMember + "\": [\"movie\"], \""
            + RuleDocumentValidator.MatchMember + "\": {\"allOf\": [{\"field\": \"genres\", \"operator\": \"contains\", \"value\": \"Thriller\"}]}}");

        Assert.True(result.IsValid, "Refused with: " + string.Join(" | ", result.Errors));
    }

    [Fact]
    public void TheSchemaRequiresTheNameMemberTheValidatorRequires()
    {
        var required = Schema().GetProperty("required");

        Assert.Contains(
            required.EnumerateArray(),
            member => string.Equals(
                member.GetString(),
                RuleDocumentValidator.NameMember,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The editor's copy of the length bound and the validator's are the same number. An editor
    /// accepting a name the plugin refuses is the drift that costs an operator a document they
    /// were told was fine, and it is silent in exactly the direction nobody checks.
    /// </summary>
    [Fact]
    public void TheSchemaDeclaresTheSameNameBoundsTheValidatorReads()
    {
        var member = Schema()
            .GetProperty("properties")
            .GetProperty(RuleDocumentValidator.NameMember);

        Assert.Equal("string", member.GetProperty("type").GetString());
        Assert.Equal(1, member.GetProperty("minLength").GetInt32());
        Assert.Equal(RuleDocumentValidator.MaximumNameLength, member.GetProperty("maxLength").GetInt32());
    }

    /// <summary>
    /// The same crossing as the version bound above, on the member the schema cannot express in
    /// full. A schema processor can hold the length and the type and knows nothing about edge
    /// whitespace or a control character, so what is asserted here is that the longest name the
    /// schema permits is one the validator takes.
    /// </summary>
    [Fact]
    public void ANameAtTheSchemasLengthBoundIsAcceptedByTheValidator()
    {
        var maxLength = Schema()
            .GetProperty("properties")
            .GetProperty(RuleDocumentValidator.NameMember)
            .GetProperty("maxLength")
            .GetInt32();

        var result = RuleDocumentValidator.Read(
            "{\"" + RuleDocumentValidator.SchemaVersionMember + "\": 1, \""
            + RuleDocumentValidator.IdMember + "\": \"christmas\", \""
            + RuleDocumentValidator.NameMember + "\": \"" + new string('a', maxLength) + "\", \"" + RuleItemScopeReader.CollectsMember + "\": [\"movie\"], \""
            + RuleDocumentValidator.MatchMember + "\": {\"allOf\": [{\"field\": \"genres\", \"operator\": \"contains\", \"value\": \"Thriller\"}]}}");

        Assert.True(result.IsValid, "Refused with: " + string.Join(" | ", result.Errors));
    }

    [Fact]
    public void TheSchemaRequiresTheScopeMemberTheScopeStageRequires()
    {
        var required = Schema().GetProperty("required");

        Assert.Contains(
            required.EnumerateArray(),
            member => string.Equals(
                member.GetString(),
                RuleItemScopeReader.CollectsMember,
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The rule member, required by the validator since #231 and required here for the same
    /// answer. A schema that stopped requiring it would let an editor tell somebody their document
    /// is complete while the plugin refuses it.
    /// </summary>
    [Fact]
    public void TheSchemaRequiresTheRuleMemberTheValidatorRequires()
    {
        var required = Schema().GetProperty("required");

        Assert.Contains(
            required.EnumerateArray(),
            member => string.Equals(
                member.GetString(),
                RuleDocumentValidator.MatchMember,
                StringComparison.Ordinal));

        Assert.False(
            RuleDocumentValidator.Read(
                "{\"schemaVersion\": 1, \"id\": \"christmas\", \"name\": \"Christmas\", \"collects\": [\"movie\"]}").IsValid,
            "The validator accepted a document the schema requires a member of.");
    }

    /// <summary>
    /// The editor's copy of the accepted kinds and the table's are the same list in the same
    /// order. This is the second member of the format a schema processor can express in full, so
    /// it is the second place the two declarations could disagree silently on a document rather
    /// than on a bound: a kind added to the table and not to the file leaves an editor refusing a
    /// document the plugin takes.
    /// </summary>
    [Fact]
    public void TheSchemaDeclaresTheSameItemKindsTheTableDeclares()
    {
        var member = Schema()
            .GetProperty("properties")
            .GetProperty(RuleItemScopeReader.CollectsMember);

        Assert.Equal("array", member.GetProperty("type").GetString());
        Assert.Equal(1, member.GetProperty("minItems").GetInt32());
        Assert.True(member.GetProperty("uniqueItems").GetBoolean());

        var items = member.GetProperty("items");
        Assert.Equal("string", items.GetProperty("type").GetString());
        Assert.Equal(
            RuleItemKindTable.Names,
            items.GetProperty("enum").EnumerateArray().Select(kind => kind.GetString()!).ToArray());
    }

    /// <summary>
    /// The crossing the two members above take on a bound, taken here on the values themselves:
    /// every kind the schema permits is one the scope stage accepts, and a scope the schema
    /// permits compiles.
    /// </summary>
    [Fact]
    public void EveryKindTheSchemaPermitsIsOneTheScopeStageAccepts()
    {
        var permitted = Schema()
            .GetProperty("properties")
            .GetProperty(RuleItemScopeReader.CollectsMember)
            .GetProperty("items")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(kind => kind.GetString())
            .ToArray();

        Assert.NotEmpty(permitted);

        foreach (var kind in permitted)
        {
            using var document = JsonDocument.Parse(
                "{\"" + RuleItemScopeReader.CollectsMember + "\": [\"" + kind + "\"]}");

            var read = RuleItemScopeReader.Read(document.RootElement);

            Assert.True(read.IsAccepted, "Refused with: " + string.Join(" | ", read.Errors));
            Assert.Equal(kind, Assert.Single(read.Kinds).Name);
        }
    }

    /// <summary>
    /// The comparison every other test in this class makes for one member, made over the SET of
    /// members. Each of those holds a bound or a list the two declarations share; none of them
    /// asks whether the two declare the same members at all, so a stage that starts reading a new
    /// member without touching the schema left both the file and its description behind and every
    /// route stayed green. That is what #234 records happening three times inside one sentence.
    /// </summary>
    /// <remarks>
    /// A member read and not declared is admitted only where the schema's own description names
    /// it, so the file is where the exemption lives rather than this test. The phrase that has to
    /// be there says what the exemption is, and it is asserted as well: a description that names
    /// the member while dropping the phrase would leave a reader unable to tell a member awaiting
    /// a decision from one somebody forgot.
    ///
    /// A member declared and not read has no exemption. An editor refusing a document the plugin
    /// accepts is the drift in the direction that costs an operator a document they cannot write,
    /// and there is no reason to declare a member nothing reads.
    ///
    /// WHAT THIS CANNOT SEE is a top-level member declared somewhere other than the two types
    /// below. The readers under them declare members of a CONDITION - a field, an operator, a
    /// value - which are not members of the document, so reflecting over every reader would
    /// compare this file against names that never appear at the top level. The two types are
    /// therefore named by hand and this paragraph is the bound: a document member introduced on a
    /// third type is outside the comparison until that type is added here.
    /// </remarks>
    [Fact]
    public void TheSchemaDeclaresEveryMemberTheValidatorReadsOrIsNamedAsNotDeclaringIt()
    {
        const string Exemption = "read by the validator and deliberately not declared here";

        var schema = Schema();
        var description = schema.GetProperty("description").GetString() ?? string.Empty;

        var declared = schema.GetProperty("properties")
            .EnumerateObject()
            .Select(member => member.Name)
            .ToArray();

        var read = DocumentMembersTheValidatorReads();

        Assert.NotEmpty(declared);
        Assert.NotEmpty(read);

        var undeclared = read.Except(declared, StringComparer.Ordinal).ToArray();

        if (undeclared.Length > 0)
        {
            Assert.Contains(Exemption, description, StringComparison.Ordinal);

            foreach (var member in undeclared)
            {
                Assert.Contains("\"" + member + "\"", description, StringComparison.Ordinal);
            }
        }

        Assert.Empty(declared.Except(read, StringComparer.Ordinal));
    }

    /// <summary>
    /// The schema closes the object and the validator refuses a member it does not declare, so an
    /// editor pointed at this file and the plugin reading the document give one answer.
    /// </summary>
    /// <remarks>
    /// The two halves are asserted together on purpose. A schema that closed the object while the
    /// validator accepted anything would red an editor over a document the plugin takes, and a
    /// validator that refused while the schema stayed open would take a document an editor said
    /// was fine. Either way somebody is told two different things about one file, and which of the
    /// two is louder depends on which tool they happened to open.
    /// </remarks>
    [Fact]
    public void TheSchemaClosesTheObjectAndTheValidatorRefusesAnUndeclaredMember()
    {
        var schema = Schema();

        Assert.True(schema.TryGetProperty("additionalProperties", out var additional));
        Assert.Equal(JsonValueKind.False, additional.ValueKind);

        var result = RuleDocumentValidator.Read(
            "{\"schemaVersion\":1,\"id\":\"christmas\",\"name\":\"Christmas\",\"collects\":[\"movie\"],"
            + "\"aMemberNoVersionDeclares\":1}");

        Assert.False(result.IsValid);
        Assert.Equal("/aMemberNoVersionDeclares", Assert.Single(result.Errors).Pointer);
    }

    /// <summary>
    /// The document members the validator reads, off the constants that declare them rather than
    /// a list written here, so a member added to either type is compared on the day its constant
    /// arrives.
    /// </summary>
    /// <returns>The member names, sorted.</returns>
    private static string[] DocumentMembersTheValidatorReads()
    {
        var members = new[] { typeof(RuleDocumentValidator), typeof(RuleItemScopeReader) }
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.IsLiteral
                && field.FieldType == typeof(string)
                && field.Name.EndsWith("Member", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Array.Sort(members, StringComparer.Ordinal);

        return members;
    }
}
