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
            "{\"" + RuleDocumentValidator.SchemaVersionMember + "\": " + maximum + "}");

        Assert.True(result.IsValid, "Refused with: " + string.Join(" | ", result.Errors));
    }
}
