using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SmartCollections.Configuration;
using Jellyfin.Plugin.SmartCollections.Membership;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Common.Plugins;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// <c>docs/uninstall.md</c> promises that removing this plugin leaves the collections it
/// generated alone. The server gives a plugin one moment to act on that removal:
/// <c>InstallationManager.UninstallPlugin</c> calls <c>OnUninstalling</c> on the live instance
/// before the files go. This plugin keeps the promise by overriding nothing, so what runs in
/// that moment is the server's own empty implementation.
/// </summary>
/// <remarks>
/// The promise had nothing behind it until this class. A later change adding an override that
/// deletes or renames a collection would contradict the page while every other check stayed
/// green, and uninstall is also the route somebody takes while troubleshooting, so the content
/// it destroys is content nothing restores. These tests do not need a collection writer or a
/// rule identity to exist: an override that is absent cannot delete anything, whatever the rest
/// of the plugin grows into.
/// </remarks>
public class UninstallHookTests
{
    /// <summary>
    /// The hook the server calls, named once so a rename in the server SDK is a compile-time
    /// question here rather than a test that starts passing over nothing.
    /// </summary>
    private const string HookName = "OnUninstalling";

    /// <summary>
    /// Gets the two assemblies this repository ships into a server. A type overriding the hook
    /// in either of them runs on uninstall; a type in the test assembly does not.
    /// </summary>
    private static string[] Members(Type port)
        => port.GetMethods()
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<Assembly> ShippedAssemblies =>
    [
        typeof(Plugin).Assembly,
        typeof(RuleDocumentScan).Assembly
    ];

    /// <summary>
    /// The other half of the promise. An uninstall runs no code of this plugin's, which the two
    /// tests above hold, and the page also says the stamp stays on the collections so a reinstall
    /// adopts them. Nothing this plugin can call takes a mark off a collection or removes one, and
    /// that is a property of the two ports rather than of the moment: neither port has a member
    /// that could, whoever calls it and whenever.
    /// </summary>
    /// <remarks>
    /// The members are compared as a set rather than searched for by name, so a call added to
    /// either port reds this whether or not somebody thought of this page. That is the point at
    /// which it is worth being red: #57 plans an explicit action that removes the generated
    /// collections, and the port member it needs is exactly what this refuses, so that change
    /// arrives here and rewrites the page in the same motion instead of contradicting it.
    ///
    /// The two membership calls take item identifiers, so what they change is what a collection
    /// holds. Neither takes a provider entry or a collection to delete.
    ///
    /// A RENAME IS IN THE SET NOW AND IT IS NOT A REMOVAL, which is the one entry here a reader
    /// has to be told about rather than shown. #29 needs a rule's declared name to reach the
    /// collection that rule owns, and the write that does it is <c>RenameCollectionAsync</c>. It
    /// happens on a resolve, which runs on a refresh; nothing calls it on the way out, and this
    /// class holds that separately by there being no uninstall hook to call anything at all. Its
    /// parameters are asserted below for the same reason the removal's are: a rename that grew a
    /// provider dictionary would be a call that could take the mark off a collection, and the
    /// member-name set alone cannot tell those apart.
    ///
    /// The first sentence of the page says an uninstall renames nothing. That is still what it
    /// says and it is still true; what has stopped being true is that no code of this plugin's
    /// could rename a collection at any moment whatever, and the page says so where it describes
    /// this check.
    /// </remarks>
    [Fact]
    public void NoPortThisPluginWritesThroughCanRemoveACollectionOrTakeAMarkOffOne()
    {
        Assert.Equal(
            new[] { "CreateCollectionAsync", "FindCollections", "RenameCollectionAsync" },
            Members(typeof(ICollectionOwnership)));

        Assert.Equal(
            new[] { typeof(Guid), typeof(string), typeof(CancellationToken) },
            typeof(ICollectionOwnership)
                .GetMethod("RenameCollectionAsync")!
                .GetParameters()
                .Select(parameter => parameter.ParameterType));

        Assert.Equal(
            new[] { "AddToCollectionAsync", "ItemsThatStillResolve", "RemoveFromCollectionAsync" },
            Members(typeof(ICollectionMembershipWriter)));

        Assert.Equal(
            new[] { typeof(Guid), typeof(IReadOnlyList<Guid>), typeof(CancellationToken) },
            typeof(ICollectionMembershipWriter)
                .GetMethod("RemoveFromCollectionAsync")!
                .GetParameters()
                .Select(parameter => parameter.ParameterType));
    }

    /// <summary>
    /// A reinstall meets the collections the last install left and adopts them, rather than
    /// building a second set beside them.
    /// </summary>
    /// <remarks>
    /// A reinstall is a fresh process over a server that already holds the collections, so what
    /// makes this a reinstall rather than a second call is that the resolver is a new one with no
    /// memory of the first, reading the same server state. The state is what the first pass left:
    /// the collections it created, with the marks it wrote, and nothing else carried over.
    ///
    /// The duplicate this refuses is the one an operator would meet as two of every collection in
    /// their library after reinstalling a plugin, which is also what they would meet on every
    /// scheduled run if the mark were not read back.
    /// </remarks>
    [Fact]
    public async Task AReinstallAdoptsTheStampedCollectionsRatherThanCreatingDuplicates()
    {
        var server = new FakeCollectionOwnership();
        var rules = new[]
        {
            new RuleDocument(1, "nineties-thrillers", "Nineties Thrillers", "{}"),
            new RuleDocument(1, "unwatched-films", "Unwatched Films", "{}")
        };

        var firstInstall = new CollectionResolver(server);
        var before = new List<Guid>();
        foreach (var rule in rules)
        {
            before.Add(await firstInstall.ResolveAsync(rule, CancellationToken.None));
        }

        Assert.Equal(2, server.Created.Count);

        var reinstalled = new CollectionResolver(server);
        var after = new List<Guid>();
        foreach (var rule in rules)
        {
            after.Add(await reinstalled.ResolveAsync(rule, CancellationToken.None));
        }

        Assert.Equal(before, after);
        Assert.Equal(2, server.Created.Count);
        Assert.Equal(2, server.Collections.Count);
    }

    /// <summary>
    /// The hook this plugin presents to the server is the server's own. If this assertion is
    /// ever red, read <c>docs/uninstall.md</c> before changing it: the page states what an
    /// operator was told would happen to their collections.
    /// </summary>
    [Fact]
    public void ThePluginLeavesTheUninstallHookWhereTheServerDeclaredIt()
    {
        var hook = typeof(Plugin).GetMethod(
            HookName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        // A null here is not this plugin passing. It is the server having renamed or removed the
        // hook, which turns every other assertion in this class into a statement about nothing.
        Assert.True(
            hook is not null,
            $"No parameterless {HookName} is reachable from Plugin. The server's uninstall hook "
                + "has moved, and what docs/uninstall.md promises has to be re-read against the "
                + "package rather than against this test.");

        Assert.Equal(typeof(BasePlugin), hook!.DeclaringType);
    }

    /// <summary>
    /// The assertion above reads one type. This one reads every type in both shipped assemblies,
    /// because a second plugin type, or a base class between <see cref="Plugin"/> and the
    /// server's, would carry the override past a test that only looks at <see cref="Plugin"/>.
    /// </summary>
    [Fact]
    public void NoTypeThisRepositoryShipsDeclaresAnUninstallHook()
    {
        var declared = ShippedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => string.Equals(method.Name, HookName, StringComparison.Ordinal))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            declared.Count == 0,
            "Something this repository ships runs on uninstall, and docs/uninstall.md says "
                + "nothing of this plugin's does: "
                + string.Join(", ", declared));
    }

    /// <summary>
    /// The configuration type is named so the generic base above is the one the plugin actually
    /// derives from rather than a coincidence of naming, and so this file fails to compile if
    /// that pairing changes.
    /// </summary>
    [Fact]
    public void ThePluginDerivesFromTheServersPluginBase()
    {
        Assert.IsAssignableFrom<BasePlugin<PluginConfiguration>>(
            (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin)));
    }

    /// <summary>
    /// The page pastes the members a rule document declares, read out of the schema. It went a
    /// member out of date in silence when the rule member was declared, which is what this holds.
    /// </summary>
    /// <remarks>
    /// The comparison is against the schema the tree carries rather than against a list written
    /// here, so the page and the schema are one statement and not three.
    ///
    /// It reads the page as text and looks for the line, rather than parsing the fenced block,
    /// because what a reader trusts is the line under the command and not the fence around it.
    /// </remarks>
    [Fact]
    public void TheUninstallPageListsTheMembersTheSchemaDeclares()
    {
        using var schema = JsonDocument.Parse(
            RepositoryFiles.ReadFromRoot(
                "Jellyfin.Plugin.SmartCollections.Engine/Rules/rule-document.schema.json"));

        var declared = string.Join(
            ", ",
            schema.RootElement.GetProperty("properties").EnumerateObject().Select(member => member.Name));

        var page = RepositoryFiles.ReadFromRoot("docs/uninstall.md");

        Assert.Contains("Object.keys(s.properties)", page, StringComparison.Ordinal);
        Assert.Contains(
            "\n" + declared + "\n",
            page.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }
}
