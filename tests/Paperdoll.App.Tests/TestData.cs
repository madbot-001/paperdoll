using System.Runtime.CompilerServices;

namespace Paperdoll.App.Tests;

/// <summary>
/// Points Paperdoll's own folder at a temporary one for the whole test run, before any test
/// starts, so that no test can write to the user's settings, working copy or error log.
/// </summary>
internal static class TestData
{
    public static string Directory { get; private set; } = "";

    [ModuleInitializer]
    internal static void Start()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("paperdoll-data-").FullName;
        Environment.SetEnvironmentVariable("PAPERDOLL_DATA", Directory);
        // A launcher folder that does not exist, so the Forks window never reads the real one.
        Environment.SetEnvironmentVariable("PAPERDOLL_LAUNCHER", Path.Combine(Directory, "launcher"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        };
    }
}
