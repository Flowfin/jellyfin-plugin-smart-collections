using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.SmartCollections.Library;
using Jellyfin.Plugin.SmartCollections.Membership;
using Jellyfin.Plugin.SmartCollections.Rules;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// What the plugin's services are, and how long each one lives.
/// </summary>
/// <remarks>
/// A lifetime is not a preference here. The refresh gate excludes a second refresh of a collection
/// only where every trigger holds the same instance of it, so a scoped or a transient registration
/// gives each trigger its own gate, each gate excludes nothing, and every test of the gate itself
/// goes on passing while the property it exists for is gone. That failure is invisible from
/// anywhere except the registration, which is why it is asserted here.
///
/// One registration cannot be resolved in this suite and is asserted as a descriptor instead. The
/// adapter over the server's library manager needs an <c>ILibraryManager</c>, and there is no
/// server here, so what is checked is that the port resolves to that adapter as a singleton; the
/// port is then substituted so that everything registered behind it resolves for real. The adapter
/// itself is six forwarding accessors and no test in this repository executes them.
/// </remarks>
public class PluginServiceRegistrationTests
{
    /// <summary>
    /// The server builds a registrator with <c>Activator.CreateInstance</c> and catches whatever
    /// that throws, logging it against the assembly. A constructor taking dependencies therefore
    /// produces a plugin that loads and registers nothing, and this is the only place that shows
    /// up before a server does it.
    /// </summary>
    [Fact]
    public void TheServerCanBuildTheRegistratorTheWayItActuallyBuildsOne()
    {
        var built = Activator.CreateInstance(typeof(PluginServiceRegistrator));

        Assert.IsAssignableFrom<IPluginServiceRegistrator>(built);
    }

    [Fact]
    public void EveryServiceThePluginDeclaresResolvesFromAPlainServiceCollection()
    {
        using var provider = Registered();

        Assert.NotNull(provider.GetRequiredService<RuleDocumentStore>());
        Assert.NotNull(provider.GetRequiredService<CollectionRefreshGate>());
        Assert.NotNull(provider.GetRequiredService<ILibraryChangeSource>());
        Assert.NotNull(provider.GetRequiredService<LibraryChangeSubscription>());
        Assert.NotEmpty(provider.GetServices<IHostedService>());
    }

    /// <summary>
    /// The guard on the lifetimes. Two resolutions of one service returning two objects is what a
    /// scoped or transient registration looks like from a caller, and it is what takes the gate's
    /// exclusion away without failing anything else.
    /// </summary>
    [Fact]
    public void TheRuleStoreAndTheGateAreOneInstanceForTheWholePlugin()
    {
        using var provider = Registered();

        Assert.Same(provider.GetRequiredService<RuleDocumentStore>(), provider.GetRequiredService<RuleDocumentStore>());
        Assert.Same(provider.GetRequiredService<CollectionRefreshGate>(), provider.GetRequiredService<CollectionRefreshGate>());
    }

    /// <summary>
    /// The subscription the host starts and stops is the same object anything else resolves.
    /// Registering it with <c>AddHostedService&lt;T&gt;()</c> as well as as itself would build two,
    /// and the one holding the handlers would not be the one anything could ask about.
    /// </summary>
    [Fact]
    public void TheHostedServiceIsTheSubscriptionAndNotASecondCopyOfIt()
    {
        using var provider = Registered();

        var subscription = provider.GetRequiredService<LibraryChangeSubscription>();
        var hosted = provider.GetServices<IHostedService>().OfType<LibraryChangeSubscription>().Single();

        Assert.Same(subscription, hosted);
    }

    [Fact]
    public void TheRuleDocumentStoreReadsADirectoryUnderThePathTheServerHandsOut()
    {
        var paths = new FakeApplicationPaths(Path.Combine(Path.GetTempPath(), "smart-collections-registration"));

        using var provider = Registered(paths);

        var store = provider.GetRequiredService<RuleDocumentStore>();

        Assert.Equal(PluginServiceRegistrator.RulesDirectory(paths), store.Directory);
        Assert.StartsWith(paths.PluginConfigurationsPath, store.Directory, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing is taken from the application host, and passing none is how that is proved rather
    /// than asserted. A registrator that reached into it would throw here.
    /// </summary>
    [Fact]
    public void TheRegistrationTakesNothingFromTheApplicationHost()
    {
        var services = new ServiceCollection();

        new PluginServiceRegistrator().RegisterServices(services, null!);

        Assert.NotEmpty(services);
    }

    [Fact]
    public void TheLibraryPortResolvesToTheAdapterOverTheServersLibraryManager()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);

        var port = services.Single(descriptor => descriptor.ServiceType == typeof(ILibraryChangeSource));

        Assert.Equal(typeof(LibraryManagerChangeSource), port.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, port.Lifetime);
    }

    /// <summary>
    /// The registration is the one place the documented defaults reach the object that runs on
    /// them, so it is the one place a plugin can be wired to intervals its own page does not
    /// describe.
    /// </summary>
    /// <remarks>
    /// Nothing else in this suite reads them. Every test of the coalescer builds one with
    /// intervals of its own, because a test using the real thirty seconds would have to wait them
    /// out, and <c>SettingsDocumentTests</c> compares <c>docs/settings.md</c> against the fields
    /// the types declare rather than against what is handed over. So a registration passing two
    /// seconds and five seconds leaves both of those green while every server running this plugin
    /// coalesces on numbers the page contradicts.
    ///
    /// The clock the registration hands over is <c>TimeProvider.System</c>, so neither interval
    /// can be observed here by driving one without waiting real minutes. What is read instead is
    /// the state the constructor stored, by name, and the read fails closed: a field that is not
    /// there is a message naming it rather than an assertion that quietly stops asserting.
    /// </remarks>
    [Fact]
    public void TheCoalescerTheServerResolvesRunsOnTheDocumentedDefaults()
    {
        using var provider = Registered();

        var coalescer = provider.GetRequiredService<LibraryChangeCoalescer>();

        Assert.Equal(LibraryChangeCoalescer.DefaultQuietPeriod, IntervalHandedTo(coalescer, "_quietPeriod"));
        Assert.Equal(LibraryChangeCoalescer.DefaultMaximumWait, IntervalHandedTo(coalescer, "_maximumWait"));
    }

    [Fact]
    public void NothingIsRegisteredWithALifetimeShorterThanThePlugin()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, null!);

        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    [Fact]
    public void TheRegistrationRefusesAServiceCollectionThatIsNotThere()
    {
        Assert.Throws<ArgumentNullException>(() => new PluginServiceRegistrator().RegisterServices(null!, null!));
        Assert.Throws<ArgumentNullException>(() => PluginServiceRegistrator.RulesDirectory(null!));
    }

    /// <summary>
    /// What one interval the coalescer was built with holds, read off the instance the container
    /// produced.
    /// </summary>
    /// <param name="coalescer">The instance the registration built.</param>
    /// <param name="field">The name of the field the constructor stores that interval in.</param>
    /// <returns>The interval that field holds.</returns>
    private static TimeSpan IntervalHandedTo(LibraryChangeCoalescer coalescer, string field)
    {
        var found = typeof(LibraryChangeCoalescer)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.True(
            found is not null,
            $"LibraryChangeCoalescer holds no field called {field}. This test reads the intervals "
            + "the registration handed over off the built instance, so a renamed field takes the "
            + "reading away rather than the property, and the new name belongs here.");
        Assert.Equal(typeof(TimeSpan), found!.FieldType);

        return (TimeSpan)found.GetValue(coalescer)!;
    }

    /// <summary>
    /// The container the plugin would get on a server, minus the server.
    /// </summary>
    /// <remarks>
    /// <see cref="IApplicationPaths"/> is added because the server supplies it and the rules
    /// directory is derived from it. The library port is replaced AFTER the registration rather
    /// than before it, so the registrator's own registration is the one being overridden and a
    /// registration that disappeared would fail the descriptor test next door rather than pass
    /// quietly here.
    /// </remarks>
    /// <param name="paths">The paths to answer with, or a temporary root when none is given.</param>
    /// <returns>A built provider.</returns>
    private static ServiceProvider Registered(IApplicationPaths? paths = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(paths ?? new FakeApplicationPaths(Path.Combine(Path.GetTempPath(), "smart-collections-registration")));

        new PluginServiceRegistrator().RegisterServices(services, null!);

        services.AddSingleton<ILibraryChangeSource>(new FakeLibraryChangeSource());

        return services.BuildServiceProvider();
    }
}
