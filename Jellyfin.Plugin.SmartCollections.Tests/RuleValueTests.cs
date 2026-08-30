using System;
using Jellyfin.Plugin.SmartCollections.Rules;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// What a parsed value is, and what a parse result may hold. Both are small, and both carry one
/// property worth holding: a value cannot be changed after validation passed it, and a result is
/// never a value and a refusal at once.
/// </summary>
public class RuleValueTests
{
    [Fact]
    public void AValueCarriesTheDeclaredTypeBesideThePayload()
    {
        var value = RuleValue.Of(RuleValueType.Integer, 1997L);

        Assert.Equal(RuleValueType.Integer, value.Type);
        Assert.Equal(1997L, value.Value);
    }

    [Fact]
    public void AValueWithNoPayloadIsRefusedAtTheConstructor()
    {
        var thrown = Assert.Throws<ArgumentNullException>(() => RuleValue.Of(RuleValueType.String, null!));

        Assert.Equal("value", thrown.ParamName);
    }

    /// <summary>
    /// Read in a log line and in a failing assertion, where the type is what makes the payload
    /// mean anything.
    /// </summary>
    [Fact]
    public void AValueReadsAsItsTypeAndItsPayload()
    {
        Assert.Equal("Duration: 1.00:00:00", RuleValue.Of(RuleValueType.Duration, TimeSpan.FromDays(1)).ToString());
    }

    /// <summary>
    /// No setter of any kind, <c>init</c> included. A value a later stage could rebuild with one
    /// member replaced is a value that stage can change its mind about after validation passed
    /// it, and this is the reading that says so rather than a sentence in a remark.
    /// </summary>
    [Fact]
    public void NoMemberOfAValueHasASetter()
    {
        foreach (var property in typeof(RuleValue).GetProperties())
        {
            Assert.Null(property.SetMethod);
        }
    }

    [Fact]
    public void AnAcceptedParseCarriesTheValueAndNoError()
    {
        var parse = RuleValueParse.Accepted(RuleValue.Of(RuleValueType.Boolean, true));

        Assert.True(parse.IsAccepted);
        Assert.Null(parse.Error);
        Assert.Equal(true, parse.Value!.Value);
    }

    [Fact]
    public void ARefusedParseCarriesTheErrorAndNoValue()
    {
        var parse = RuleValueParse.Refused(new RuleValidationError("/value", "It is not a date."));

        Assert.False(parse.IsAccepted);
        Assert.Null(parse.Value);
        Assert.Equal("/value: It is not a date.", parse.Error!.ToString());
    }
}
