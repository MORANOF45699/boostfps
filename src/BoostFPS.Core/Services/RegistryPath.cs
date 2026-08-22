using Microsoft.Win32;

namespace BoostFPS.Core.Services;

/// <summary>Splits a "HKLM\Sub\Key" string into a hive plus subkey, and opens it 64-bit.</summary>
public static class RegistryPath
{
    public static (RegistryKey Hive, string SubKey) Split(string fullPath)
    {
        var path = fullPath.Replace('/', '\\').TrimStart('\\');
        var sep = path.IndexOf('\\');
        var hiveName = (sep < 0 ? path : path[..sep]).ToUpperInvariant();
        var sub = sep < 0 ? "" : path[(sep + 1)..];

        var hive = hiveName switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
            "HKCU" or "HKEY_CURRENT_USER" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64),
            "HKU" or "HKEY_USERS" => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64),
            "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryKey.OpenBaseKey(RegistryHive.CurrentConfig, RegistryView.Registry64),
            _ => throw new ArgumentException($"Unknown registry hive in path: {fullPath}")
        };

        return (hive, sub);
    }

    /// <summary>Opens the key for reading. Returns null when the key does not exist.</summary>
    public static RegistryKey? OpenRead(string fullPath)
    {
        var (hive, sub) = Split(fullPath);
        using (hive)
        {
            return hive.OpenSubKey(sub, writable: false);
        }
    }

    /// <summary>Opens the key for writing, creating it when missing.</summary>
    public static RegistryKey OpenWrite(string fullPath)
    {
        var (hive, sub) = Split(fullPath);
        using (hive)
        {
            return hive.CreateSubKey(sub, writable: true)
                   ?? throw new InvalidOperationException($"Cannot open or create {fullPath}");
        }
    }

    public static bool KeyExists(string fullPath)
    {
        using var key = OpenRead(fullPath);
        return key is not null;
    }

    /// <summary>Names of the immediate subkeys, or an empty array when the key is missing.</summary>
    public static string[] SubKeyNames(string fullPath)
    {
        using var key = OpenRead(fullPath);
        return key?.GetSubKeyNames() ?? [];
    }

    /// <summary>The reg.exe form of a path, for `reg export`.</summary>
    public static string ToRegExeForm(string fullPath)
    {
        var path = fullPath.Replace('/', '\\').TrimStart('\\');
        var sep = path.IndexOf('\\');
        var hiveName = (sep < 0 ? path : path[..sep]).ToUpperInvariant();
        var sub = sep < 0 ? "" : path[(sep + 1)..];

        var full = hiveName switch
        {
            "HKLM" => "HKEY_LOCAL_MACHINE",
            "HKCU" => "HKEY_CURRENT_USER",
            "HKCR" => "HKEY_CLASSES_ROOT",
            "HKU" => "HKEY_USERS",
            "HKCC" => "HKEY_CURRENT_CONFIG",
            _ => hiveName
        };

        return sub.Length == 0 ? full : $"{full}\\{sub}";
    }
}
