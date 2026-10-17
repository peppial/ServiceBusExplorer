namespace SbExplorer.Core;

public static class AppPaths
{
    /// <summary>
    /// Per-user data folder: %LOCALAPPDATA% on Windows, ~/Library/Application Support on macOS,
    /// ~/.local/share on Linux.
    /// </summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "ServiceBusExplorer");

    public static string ConnectionsFile => Path.Combine(DataDirectory, "connections.json");
}
