using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// What a document compiles into is immutable once it exists. A compiled rule that could be
/// changed after validation is a rule that means one thing when it was checked and another when it
/// runs, which is the defect the prior art in this space has: it rewrites dates into another form
/// by mutating the parsed rule in place before evaluating it.
/// </summary>
/// <remarks>
/// The population is DERIVED from the engine assembly rather than listed here. A list would pass on
/// the day somebody adds a type to the rule pipeline and forgets to add it, which is the only day
/// this test matters.
///
/// WHAT "NO SETTER" MEANS HERE is a property that cannot be assigned after the object exists. An
/// init-only accessor is allowed and is counted as no setter: it can be used by the code that
/// constructs the value and by nothing afterwards, which is the property this test is for. An
/// ordinary setter is refused.
///
/// THE ONE THING THIS CANNOT COVER is named rather than left out. A compilation carries the
/// server's own item query, which is a type this plugin does not declare and which is settable by
/// construction, because setting its properties is how a query is expressed at all. What is
/// asserted about it below is the half that is this plugin's: the property carrying it cannot be
/// replaced.
/// </remarks>
public class RuleCompiledFormTests
{
    private static bool IsInitOnly(PropertyInfo property)
        => property.SetMethod is not null
           && property.SetMethod.ReturnParameter
               .GetRequiredCustomModifiers()
               .Contains(typeof(IsExternalInit));

    private static Type[] RulePipelineTypes()
        => typeof(RuleQueryCompilation).Assembly
            .GetExportedTypes()
            .Where(type => string.Equals(
                type.Namespace,
                typeof(RuleQueryCompilation).Namespace,
                StringComparison.Ordinal))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Without this the sweep below passes over an empty population, which is what a namespace
    /// rename or a scope that stopped matching would leave behind.
    /// </summary>
    [Fact]
    public void TheSweepReadsTheTypesTheRulePipelineIsMadeOf()
    {
        var names = RulePipelineTypes().Select(type => type.Name).ToArray();

        Assert.Contains("RuleQueryCompilation", names);
        Assert.Contains("RuleConditionValue", names);
        Assert.Contains("RuleValue", names);
        Assert.True(names.Length > 20, "The sweep read " + names.Length + " types.");
    }

    /// <summary>
    /// The done condition this test carries: the compiled rule exposes no setter.
    /// </summary>
    [Fact]
    public void NothingTheRulePipelineProducesCanBeAssignedAfterItExists()
    {
        var settable = RulePipelineTypes()
            .SelectMany(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => property.SetMethod is not null
                                   && property.SetMethod.IsPublic
                                   && !IsInitOnly(property))
                .Select(property => type.Name + "." + property.Name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(settable);
    }

    /// <summary>
    /// The query a compilation carries is the server's own type and is settable, because setting
    /// its properties is how a query is expressed. What is this plugin's to hold is that the
    /// compilation cannot be pointed at a different one.
    /// </summary>
    [Fact]
    public void TheQueryACompilationCarriesCannotBeReplaced()
    {
        var query = typeof(RuleQueryCompilation).GetProperty(nameof(RuleQueryCompilation.Query));

        Assert.NotNull(query);
        Assert.Null(query!.SetMethod);
        Assert.Equal(typeof(InternalItemsQuery), query.PropertyType);
    }

    /// <summary>
    /// A guard that could not have failed proves nothing, and this one is a sweep with a rule
    /// about which accessors count. So the rule itself is asserted against a type that has each
    /// kind, which the server's query does: it is full of ordinary setters, and it is not in the
    /// population above.
    /// </summary>
    [Fact]
    public void TheSweepWouldReportAnOrdinarySetter()
    {
        var settable = typeof(InternalItemsQuery)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is not null
                               && property.SetMethod.IsPublic
                               && !IsInitOnly(property))
            .ToArray();

        Assert.NotEmpty(settable);
        Assert.DoesNotContain(typeof(InternalItemsQuery), RulePipelineTypes());
    }

    /// <summary>
    /// The other half of that rule: an init-only accessor is counted as no setter, and this reads
    /// one that exists rather than trusting the helper.
    /// </summary>
    [Fact]
    public void TheSweepCountsAnInitOnlyAccessorAsNoSetter()
    {
        var pointer = typeof(RuleConditionValue).GetProperty(nameof(RuleConditionValue.Pointer));

        Assert.NotNull(pointer);
        Assert.NotNull(pointer!.SetMethod);
        Assert.True(IsInitOnly(pointer));
    }
}
