using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Platform;

/// <summary>
/// The device the app runs on, written into the takes it records and the files it exports.
/// <see cref="SystemDeviceInfo"/> reads it on the desktop systems; the Android and iOS heads
/// register their own (the model is only in their system APIs).
/// </summary>
public interface IDeviceInfo
{
    /// <summary>A fresh copy each time (it ends up in a take, which may change it).</summary>
    DeviceDescription Describe();
}

/// <summary>
/// <see cref="IDeviceInfo"/> for Windows (registry), Linux (os-release, DMI) and macOS. Read once,
/// on first use; nothing it reads may fail the caller, so a part that can't be read is null.
/// </summary>
public sealed class SystemDeviceInfo : IDeviceInfo
{
    // What firmware without a real name says instead.
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "To be filled by O.E.M.", "To Be Filled By O.E.M", "O.E.M.", "OEM", "System manufacturer", "System Product Name",
        "Default string", "Not Applicable", "Not Specified", "None", "Unknown", "Undefined", "INVALID", "xxxxx",
    };

    private readonly Lazy<DeviceDescription> _device = new(Detect);

    public DeviceDescription Describe() => Copy(_device.Value);

    /// <summary>A copy of <paramref name="d"/>.</summary>
    public static DeviceDescription Copy(DeviceDescription d) => new()
    {
        Os = d.Os,
        OsVersion = d.OsVersion,
        Manufacturer = d.Manufacturer,
        Model = d.Model,
    };

    private static DeviceDescription Detect()
    {
        if (OperatingSystem.IsWindows()) return Windows();
        if (OperatingSystem.IsLinux()) return Linux();
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            return new DeviceDescription { Os = "macos", OsVersion = Environment.OSVersion.Version.ToString(3), Manufacturer = "Apple" };
        if (OperatingSystem.IsBrowser()) return new DeviceDescription { Os = "browser" };
        return new DeviceDescription { Os = Environment.OSVersion.Platform.ToString().ToLowerInvariant() };
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static DeviceDescription Windows()
    {
        var version = Environment.OSVersion.Version.ToString(3);
        // The update revision ("10.0.26100.4061"), which Environment.OSVersion leaves out.
        if (Registry(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR") is int ubr) version += $".{ubr}";
        return new DeviceDescription
        {
            Os = "windows",
            OsVersion = version,
            Manufacturer = Clean(Registry(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer") as string),
            Model = Clean(Registry(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName") as string),
        };
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static object? Registry(string key, string value)
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
            return k?.GetValue(value);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static DeviceDescription Linux() => new()
    {
        Os = "linux",
        OsVersion = OsRelease("PRETTY_NAME") ?? Environment.OSVersion.Version.ToString(),
        Manufacturer = Clean(ReadLine("/sys/class/dmi/id/sys_vendor")),
        Model = Clean(ReadLine("/sys/class/dmi/id/product_name")),
    };

    /// <summary>A value of /etc/os-release (or /usr/lib/os-release), without its quotes.</summary>
    private static string? OsRelease(string key)
    {
        foreach (var file in (string[])["/etc/os-release", "/usr/lib/os-release"])
        {
            try
            {
                if (!File.Exists(file)) continue;
                foreach (var line in File.ReadLines(file))
                    if (line.StartsWith(key + "=", StringComparison.Ordinal))
                        return Clean(line[(key.Length + 1)..].Trim().Trim('"', '\''));
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    private static string? ReadLine(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadLines(path).FirstOrDefault() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Trimmed; null when empty or a firmware placeholder.</summary>
    public static string? Clean(string? value)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) || Placeholders.Contains(v) ? null : v;
    }
}
