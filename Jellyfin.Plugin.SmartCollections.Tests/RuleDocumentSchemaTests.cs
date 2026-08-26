using System;
using System.Text.Json;
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
            + ", \"" + RuleDocumentValidator.NameMember + "\": \"Christmas\"}");

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
            + RuleDocumentValidator.NameMember + "\": \"" + new string('a', maxLength) + "\"}");

        Assert.True(result.IsValid, "Refused with: " + string.Join(" | ", result.Errors));
    }
}
