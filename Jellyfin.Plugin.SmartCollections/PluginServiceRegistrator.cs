using System;
using System.IO;
using Jellyfin.Plugin.SmartCollections.Evaluation;
using Jellyfin.Plugin.SmartCollections.Library;
using Jellyfin.Plugin.SmartCollections.Membership;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SmartCollections;

/// <summary>
/// Where every service this plugin runs on is built, and how long each one lives.
/// </summary>
/// <remarks>
/// The scheduled refresh, the library subscription, the manual refresh endpoint and the
/// configuration API all need the same rule store and the same refresh gate. An entry point that
/// built its own would give the plugin as many gates as it has triggers, and a gate held by one
/// trigger excludes nothing: two refreshes of one collection would interleave their writes and
/// leave a membership neither of their rules describes. Every one of them is therefore a singleton
/// here rather than a <c>new</c> at a call site, and
/// <c>TheRuleStoreAndTheGateAreOneInstanceForTheWholePlugin</c> is what goes red if one becomes
/// scoped or transient.
///
/// The parameterless constructor is a requirement rather than a style. The server builds this type
/// with <c>Activator.CreateInstance</c> and catches whatever that throws, so a constructor taking
/// dependencies produces a plugin that loads, registers nothing, and fails later somewhere with no
/// mention of this file:
///
/// <code>
/// gh api "repos/jellyfin/jellyfin/contents/Emby.Server.Implementations/Plugins/PluginManager.cs?ref=v10.11.11" \
///   --jq .content | base64 -d | sed -n '225,226p'
/// </code>
///
/// Nothing is taken from <c>applicationHost</c>. Everything registered below resolves what it needs
/// from the container the server is still building, which is the seam that lets the suite call this
/// method on a plain <see cref="ServiceCollection"/> with no server behind it.
/// </remarks>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <summary>
    /// The rules directory, under the path the server hands out.
    /// </summary>
    /// <remarks>
    /// Beside the plugin's own configuration file rather than in a directory of its own elsewhere:
    /// rule documents are what an operator wrote, so an operator backing up the server's
    /// configuration directory takes their rules with it and a restore brings the collections back.
    /// The path comes from <see cref="IApplicationPaths"/> and is never composed from the process's
    /// working directory or from an environment variable, which is what lets every test that
    /// touches the store point it at a temporary directory and need no elevated rights.
    ///
    /// Nothing is created here. The store lists a directory that does not exist as empty and
    /// creates it on the first write, so a server that has never had a rule written on it grows no
    /// directory it does not use.
    /// </remarks>
    /// <param name="applicationPaths">The paths the server hands out.</param>
    /// <returns>The directory the rule documents live in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="applicationPaths"/> is <see langword="null"/>.</exception>
    public static string RulesDirectory(IApplicationPaths applicationPaths)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);

        return Path.Combine(applicationPaths.PluginConfigurationsPath, "SmartCollections", "rules");
    }

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        serviceCollection.AddSingleton(
            provider => new RuleDocumentStore(RulesDirectory(provider.GetRequiredService<IApplicationPaths>())));

        serviceCollection.AddSingleton<CollectionRefreshGate>();

        serviceCollection.AddSingleton<ILibraryChangeSource, LibraryManagerChangeSource>();

        // The one question an evaluation asks the server, behind the port the engine
        // declares. Registered here rather than constructed by whatever runs an
        // evaluation, for the reason every other line in this method is: a second
        // instance would be a second forward onto the same library manager, and the
        // thing anything else resolves would not be the thing that answered.
        serviceCollection.AddSingleton<IRuleItemSource, LibraryManagerItemSource>();

        // One coalescer for the plugin, and the only observer of the subscription. A second
        // instance would accumulate a second copy of every change and close its own batches, so
        // the burst the subscription reports once would be evaluated twice. The intervals are the
        // recorded defaults; the clock is the framework's real one here and is injected so the
        // suite can drive the two intervals without waiting them out.
        serviceCollection.AddSingleton(
            provider => new LibraryChangeCoalescer(
                provider.GetServices<ILibraryChangeBatchSink>(),
                TimeProvider.System,
                LibraryChangeCoalescer.DefaultQuietPeriod,
                LibraryChangeCoalescer.DefaultMaximumWait));
        serviceCollection.AddSingleton<ILibraryChangeObserver>(
            provider => provider.GetRequiredService<LibraryChangeCoalescer>());

        // Registered as itself and handed to the host through that registration rather than
        // constructed twice. AddHostedService<T>() would build a second instance, so the object
        // holding the subscription would not be the object anything else resolves, and a later
        // caller asking whether the plugin is subscribed would be asking an object that never was.
        serviceCollection.AddSingleton<LibraryChangeSubscription>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<LibraryChangeSubscription>());
    }
}
