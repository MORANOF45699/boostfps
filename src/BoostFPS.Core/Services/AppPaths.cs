namespace BoostFPS.Core.Services;

/// <summary>All on-disk locations the app writes to. Machine-wide, so backups survive user switches.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BoostFPS");

    public static string Backups => Path.Combine(Root, "Backups");
    public static string ChangelogFile => Path.Combine(Root, "changelog.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string ProfilesDir => Path.Combine(Root, "Profiles");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(ProfilesDir);
    }
}
