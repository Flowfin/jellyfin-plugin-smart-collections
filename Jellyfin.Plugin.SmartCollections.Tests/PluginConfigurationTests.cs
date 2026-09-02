using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.SmartCollections.Configuration;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// A plugin setting is only real where three things agree: the property exists, a fresh
/// configuration gives it a stated default, and the settings page offers a control for it. The
/// template shipped four settings where none of that mattered because nothing read them. These
/// tests hold the three together so the next setting cannot arrive half-made.
/// </summary>
public class PluginConfigurationTests
{
    /// <summary>
    /// Every setting the configuration declares, with the value a fresh instance is documented
    /// to give it. This table is the documentation: a property missing from it is a setting
    /// whose default nobody wrote down, and an entry with no property is a setting that was
    /// removed without its row.
    /// </summary>
    /// <remarks>
    /// Empty, and that is the current state of the plugin rather than an omission. Nothing in
    /// the plugin reads a setting yet.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, object?> DocumentedDefaults =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// The form controls a settings page can carry. A control is bound to a setting by its id,
    /// which is what the page's own script passes to <c>querySelector</c>, so the id is what is
    /// compared. Buttons are not matched: a submit button carries no value.
    /// </summary>
    private static readonly Regex FormControl = new(
        "<(?:input|select|textarea)\\b[^>]*?\\bid=\"(?<id>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// One part of a name written in the casing a property is written in. The refusal below reads
    /// WORDS rather than substrings, and that is the whole of why it can be narrow enough to
    /// refuse rather than warn: <c>Normal</c> begins with <c>No</c> and <c>Notification</c> begins
    /// with <c>Not</c>, so a substring test would refuse two names that say nothing.
    /// </summary>
    private static readonly Regex NamePart = new(
        "[A-Z][a-z0-9]*",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Words that say a setting turns something off.
    /// </summary>
    private static readonly HashSet<string> SwitchingOff = new(StringComparer.Ordinal)
    {
        "Bypass",
        "Disable",
        "Disabled",
        "Ignore",
        "Ignored",
        "No",
        "Off",
        "Skip",
        "Skipped",
        "Suppress",
        "Suppressed",
        "Unchecked",
        "Unsafe",
        "Without",
    };

    /// <summary>
    /// The two checks the plan says no setting may switch off: the validation a rule document goes
    /// through, and the ownership check that stops this plugin writing to a collection it did not
    /// create.
    /// </summary>
    private static readonly HashSet<string> Guarded = new(StringComparer.Ordinal)
    {
        "Owned",
        "Owner",
        "Ownership",
        "Validate",
        "Validated",
        "Validation",
        "Validator",
    };

    /// <summary>
    /// The names the refusal below is held to before it judges anything, in the shape the
    /// pull-request hygiene check and the invariant lint already use in this repository: a test
    /// fires on its own fixtures and passes its near misses, or nothing is judged at all.
    /// </summary>
    /// <remarks>
    /// These carry the whole of the proof today, because the configuration declares no property,
    /// so the refusal itself runs over an empty set and would pass whatever it said. A setting
    /// arrives with the surface that reads it, and this is written before that surface so the
    /// first setting meets it rather than following it.
    ///
    /// The near misses are the half that matters. A name mentioning validation or ownership
    /// without switching either off is an ordinary setting, and refusing one would make this a tax
    /// on naming rather than a guard.
    /// </remarks>
    /// <returns>Each name with whether it has to be refused.</returns>
    public static TheoryData<string, bool> SettingNames() => new()
    {
        { "DisableValidation", true },
        { "SkipOwnershipCheck", true },
        { "IgnoreValidationErrors", true },
        { "UnsafeOwnershipOverride", true },
        { "MaximumNestingDepth", true },
        { "ValidationErrorLimit", false },
        { "OwnershipQueryPageSize", false },
        { "SkipEmptyCollections", false },
        { "NotificationOwner", false },
        { "NormalRefreshInterval", false },
    };

    /// <summary>
    /// Reads the settings the configuration class actually declares. Declared only, so the
    /// members every plugin configuration inherits are not mistaken for this plugin's settings.
    /// </summary>
    /// <returns>The declared public instance properties.</returns>
    private static PropertyInfo[] DeclaredSettings()
        => typeof(PluginConfiguration).GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    [Fact]
    public void EverySettingHasADocumentedDefault()
    {
        Assert.Equal(
            DocumentedDefaults.Keys.OrderBy(name => name, StringComparer.Ordinal),
            DeclaredSettings().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void AFreshConfigurationHoldsTheDocumentedDefaults()
    {
        var configuration = new PluginConfiguration();

        foreach (var (name, expected) in DocumentedDefaults)
        {
            var property = typeof(PluginConfiguration).GetProperty(name);

            Assert.True(property is not null, "No setting is named " + name + ".");
            Assert.Equal(expected, property!.GetValue(configuration));
        }
    }

    [Fact]
    public void NoneOfTheTemplatesSampleSettingsSurvive()
    {
        var declared = DeclaredSettings().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var sample in new[] { "SomeOptions", "TrueFalseSetting", "AnInteger", "AString", "Options" })
        {
            Assert.DoesNotContain(sample, declared);
        }
    }

    [Fact]
    public void EveryControlOnTheSettingsPageBindsToASetting()
    {
        var page = RepositoryFiles.ReadFromRoot(
            "Jellyfin.Plugin.SmartCollections/Configuration/configPage.html");

        foreach (var id in FormControl.Matches(page).Select(control => control.Groups["id"].Value))
        {
            Assert.True(
                DocumentedDefaults.ContainsKey(id),
                "The settings page carries a control with id " + id + ", and no setting is named that.");
        }
    }

    /// <summary>
    /// The other direction. The test above catches a control nothing reads; this one catches a
    /// setting an administrator has no way to set, which is the failure that hides longer
    /// because the page looks complete and simply does not offer the thing.
    /// </summary>
    /// <remarks>
    /// Both directions together are what <c>docs/testing.md</c> names as the replacement for a
    /// browser-driven test of this page. Both are vacuous while the plugin declares no setting,
    /// which is the same reason the table above is empty and not a gap in the pair.
    /// </remarks>
    [Fact]
    public void EverySettingHasAControlOnTheSettingsPage()
    {
        var page = RepositoryFiles.ReadFromRoot(
            "Jellyfin.Plugin.SmartCollections/Configuration/configPage.html");

        var controls = FormControl
            .Matches(page)
            .Select(control => control.Groups["id"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in DocumentedDefaults.Keys)
        {
            Assert.True(
                controls.Contains(name),
                "The setting " + name + " has no control on the settings page, so nobody can set it.");
        }
    }

    [Theory]
    [MemberData(nameof(SettingNames))]
    public void TheNameTestAgreesWithItsFixtures(string name, bool refused)
        => Assert.Equal(refused, WhySettingIsRefused(name) is not null);

    /// <summary>
    /// No setting can disable a validation or an ownership check, and there is no option to raise
    /// the nesting limit. Held over the names the configuration declares.
    /// </summary>
    /// <remarks>
    /// WHAT THIS READS IS A NAME, and the bound is worth knowing before a green run is read as the
    /// property holding. A property called <c>StrictMode</c> whose <see langword="false"/> value
    /// switches validation off passes here, and so does one that reaches the same end through its
    /// value rather than through its name. What it does refuse is the spelling somebody reaches
    /// for when they want the switch, which is the one that arrives without an argument being had
    /// about it.
    ///
    /// It is vacuous while the configuration declares nothing, for the same reason the pair above
    /// it is, and the fixtures are where it is shown to bite.
    /// </remarks>
    [Fact]
    public void NoSettingSwitchesOffAValidationOrAnOwnershipCheck()
    {
        foreach (var property in DeclaredSettings())
        {
            var why = WhySettingIsRefused(property.Name);

            Assert.True(
                why is null,
                "The setting " + property.Name + " " + why + ". A setting that lets an operator"
                + " switch off a safety property does not exist here: no option disables"
                + " validation, none raises the nesting limit, and none turns off the ownership"
                + " check that stops this plugin writing to a collection it did not create.");
        }
    }

    /// <summary>
    /// Why a setting of this name is refused, or <see langword="null"/> where it is not.
    /// </summary>
    /// <param name="name">The declared property name.</param>
    /// <returns>The clause of the failure message that says which rule fired.</returns>
    private static string? WhySettingIsRefused(string name)
    {
        var words = NamePart
            .Matches(name)
            .Select(part => part.Value)
            .ToHashSet(StringComparer.Ordinal);

        // The nesting limit is refused whatever word sits beside it. What may not exist is an
        // option to RAISE it, so a setting naming it at all is the thing being refused; requiring
        // a switching-off word here would let NestingDepthLimit through.
        if (words.Contains("Nesting"))
        {
            return "names the nesting limit, and there is no option to raise it";
        }

        var off = words.FirstOrDefault(SwitchingOff.Contains);
        var guarded = words.FirstOrDefault(Guarded.Contains);

        return off is null || guarded is null
            ? null
            : "pairs " + off + " with " + guarded + ", which reads as a switch over a check";
    }
}
