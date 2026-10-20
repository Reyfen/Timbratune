using System.Text.Json.Serialization;

namespace Reyfen.Timbratune.Core.Models;

/// <summary>
/// The device a take was recorded or exported on (take.json <c>take.recorded_on</c>,
/// <c>exported_on</c>). The microphone and the system's audio path change the numbers
/// (formants by hundreds of Hz), so takes from different devices can be told apart.
/// Each part is null when the system doesn't say.
/// </summary>
public sealed class DeviceDescription
{
    /// <summary>"windows", "linux", "macos", "android", "ios" or "browser".</summary>
    public string Os { get; set; } = "";

    /// <summary>
    /// The system's version: "10.0.26100.4061" (Windows), "15" (Android), "18.1" (iOS), "14.5.0" (macOS),
    /// the distribution's name and version on Linux ("Ubuntu 24.04.1 LTS").
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OsVersion { get; set; }

    /// <summary>"Google", "Apple", "LENOVO" (as the firmware or system reports it).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Manufacturer { get; set; }

    /// <summary>"Pixel 9", "iPhone17,1", "20XW0055GE" (as the firmware or system reports it).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; set; }
}
