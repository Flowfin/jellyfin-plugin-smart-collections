using System.IO;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.SmartCollections.Tests;

/// <summary>
/// The paths a server would hand out, rooted wherever a test wants them.
/// </summary>
/// <remarks>
/// Every path this plugin uses comes from <see cref="IApplicationPaths"/> and none is composed from
/// the process's working directory, so pointing this at a temporary directory is the whole of what
/// a test needs in order to exercise a path without a server, without a display and without
/// elevated rights. That is the rule <c>docs/testing.md</c> states, and this class is what makes it
/// cheap to keep.
///
/// Every member of the interface is answered rather than only the one member under test. A stub
/// throwing on the rest would pass today and fail the day a service reads a second path, with a
/// stack trace pointing at the test rather than at the reading.
/// </remarks>
internal sealed class FakeApplicationPaths : IApplicationPaths
{
    public FakeApplicationPaths(string root)
    {
        ProgramDataPath = root;
    }

    public string ProgramDataPath { get; }

    public string WebPath => Path.Join(ProgramDataPath, "web");

    public string ProgramSystemPath => Path.Join(ProgramDataPath, "system");

    public string DataPath => Path.Join(ProgramDataPath, "data");

    public string ImageCachePath => Path.Join(CachePath, "images");

    public string PluginsPath => Path.Join(ProgramDataPath, "plugins");

    public string PluginConfigurationsPath => Path.Join(PluginsPath, "configurations");

    public string LogDirectoryPath => Path.Join(ProgramDataPath, "log");

    public string ConfigurationDirectoryPath => Path.Join(ProgramDataPath, "config");

    public string SystemConfigurationFilePath => Path.Join(ConfigurationDirectoryPath, "system.xml");

    public string CachePath => Path.Join(ProgramDataPath, "cache");

    public string TempDirectory => Path.Join(ProgramDataPath, "temp");

    public string VirtualDataPath => "%AppDataPath%";

    public string TrickplayPath => Path.Join(DataPath, "trickplay");

    public string BackupPath => Path.Join(ProgramDataPath, "backups");

    public void MakeSanityCheckOrThrow()
    {
        // The server checks its own paths are usable before it starts. Nothing this plugin
        // registers calls it, and a fake that threw would be inventing a failure to test.
    }

    public void CreateAndCheckMarker(string path, string markerName, bool recursive = false)
    {
        // Nothing in this plugin asks a path to be created, and a fake that created directories
        // would make a test that never touches the disk start touching it.
    }
}
