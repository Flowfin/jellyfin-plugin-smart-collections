using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// Builds one condition the way the plugin builds one: by writing the document that carries it and
/// reading it back through the stages a rule is read with.
/// </summary>
/// <remarks>
/// Through the document rather than by constructing the value, because the type a value is parsed
/// against depends on the field and the operator TOGETHER - <c>withinLast</c> applies to a field
/// holding an instant and takes a length of time beside it - and a test that decided that for
/// itself would be asserting against its own answer rather than against the vocabulary's.
///
/// Shared rather than copied, because two suites now want it and a second copy is a second answer
/// to the same question, which drifts the day the vocabulary changes.
/// </remarks>
internal static class RuleConditionFixture
{
    /// <summary>
    /// Reads one condition out of a document written around it.
    /// </summary>
    /// <param name="field">The field, as a document writes it.</param>
    /// <param name="operatorName">The operator, as a document writes it.</param>
    /// <param name="written">The values the document wrote, none where the operator takes none.</param>
    /// <returns>The condition, with its values parsed.</returns>
    public static RuleConditionValue Condition(
        string field,
        string operatorName,
        IReadOnlyList<string> written)
    {
        var fieldRow = RuleFieldTable.Find(field)!;
        var operatorRow = RuleOperatorTable.Find(operatorName)!;
        var value = Json(fieldRow.ValueType, operatorRow, written);
        var text = "{\"schemaVersion\":1,\"id\":\"x\",\"name\":\"X\",\"collects\":[\"movie\"],"
                   + "\"match\":{\"allOf\":[{\"field\":\"" + field + "\",\"operator\":\"" + operatorName + "\""
                   + value + "}]}}";

        using var parsed = JsonDocument.Parse(text);
        var root = parsed.RootElement;
        var scope = RuleItemScopeReader.Read(root);
        var composition = RuleCompositionReader.Read(root.GetProperty("match"), "/match");
        var fields = RuleFieldReader.Read(root, composition.Group!, scope.Kinds);
        var operators = RuleOperatorReader.Read(root, fields.Fields);
        var values = RuleValueReader.Read(root, operators.Operators);

        Assert.True(
            values.IsAccepted,
            "The fixture is refused: " + string.Join("; ", values.Errors.Select(error => error.ToString())));

        return Assert.Single(values.Conditions);
    }

    /// <summary>
    /// The <c>value</c> member a document writes beside an operator, or nothing where the operator
    /// takes none.
    /// </summary>
    /// <param name="type">The field's declared type.</param>
    /// <param name="operatorRow">The operator's row.</param>
    /// <param name="written">The values, as a test wrote them.</param>
    /// <returns>The member, with its leading comma, or an empty string.</returns>
    private static string Json(RuleValueType type, RuleOperatorRow operatorRow, IReadOnlyList<string> written)
    {
        if (!operatorRow.TakesAValue)
        {
            return string.Empty;
        }

        var bare = type is RuleValueType.Integer or RuleValueType.Decimal or RuleValueType.Boolean;
        var members = written.Select(value => bare ? value : "\"" + value + "\"").ToArray();

        return operatorRow.TakesAList
            ? ",\"value\":[" + string.Join(",", members) + "]"
            : ",\"value\":" + members[0];
    }
}
