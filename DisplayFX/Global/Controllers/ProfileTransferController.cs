using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;
using DisplayFX.Objects.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Monitor = DisplayFX.Objects.Entities.Monitor;

namespace DisplayFX.Global.Controllers;

/// <summary>Versioned backups and validated, detached profile imports. Never talks to monitor hardware.</summary>
public static class ProfileTransferController
{
    public const int FormatVersion = 1;
    public const int MaximumNameLength = 80;
    private const int MaximumFileSize = 5 * 1024 * 1024;
    private const string ProfileFormat = "DisplayFX.Profile";
    private const string SettingsFormat = "DisplayFX.Settings";
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        MaxDepth = 32
    };

    public static string GetUniqueName(Monitor monitor, string desiredName)
    {
        var name = ValidateName(desiredName);
        if (!monitor.Profiles.Any(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            return name;

        for (var copy = 2; ; copy++)
        {
            var suffix = $" ({copy})";
            var candidate = name.Substring(0, Math.Min(name.Length, MaximumNameLength - suffix.Length)) + suffix;
            if (!monitor.Profiles.Any(profile => string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    public static void RenameProfile(Profile profile, string name)
    {
        if (profile.IsDefault)
            throw new InvalidOperationException("The default profile cannot be renamed.");
        name = ValidateName(name);
        if (profile.Monitor.Profiles.Any(other => !ReferenceEquals(other, profile) &&
                string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A profile with this name already exists on this monitor.");
        profile.Name = name;
    }

    public static Profile CloneProfile(Profile source, Monitor target, string name, bool includeTriggers = false)
    {
        ValidateProfile(source);
        return new Profile(target, ValidateName(name), new ProfileSetting(source.ProfileSetting.Brightness,
            source.ProfileSetting.Contrast, source.ProfileSetting.Gamma, source.ProfileSetting.DigitalVibrance), false)
        {
            HotkeyModifiers = includeTriggers ? source.HotkeyModifiers : null,
            HotkeyKey = includeTriggers ? source.HotkeyKey : null,
            LinkedExecutablePath = includeTriggers ? source.LinkedExecutablePath : null
        };
    }

    public static void ExportProfile(Profile profile, string path)
    {
        ValidateProfile(profile);
        WriteExport(path, ProfileFormat, JObject.FromObject(profile, JsonSerializer.Create(SerializerSettings)));
    }

    public static void ExportSettings(Computer computer, string path)
    {
        ValidateComputer(computer);
        WriteExport(path, SettingsFormat, JObject.FromObject(computer, JsonSerializer.Create(SerializerSettings)));
    }

    public static Computer ReadSettings(string path)
    {
        var payload = ReadExport(path, SettingsFormat);
        if (payload["Monitors"] is not JArray monitors)
            throw new InvalidDataException("The file contains no monitor list.");
        foreach (var monitor in monitors)
        {
            if (monitor is not JObject monitorObject || monitorObject["Profiles"] is not JArray profiles)
                throw new InvalidDataException("The file contains an invalid monitor or profile list.");
            foreach (var profile in profiles)
                ValidateProfilePayload(profile as JObject ?? throw new InvalidDataException("The file contains an invalid profile."));
        }
        var imported = payload.ToObject<Computer>(JsonSerializer.Create(SerializerSettings))
                       ?? throw new InvalidDataException("The file does not contain settings.");
        ValidateComputer(imported);
        NormalizeProfiles(imported);
        return imported;
    }

    /// <summary>Builds a complete new model for persistence; current models and unsaved drafts remain untouched.</summary>
    public static Computer MergeSettings(Computer current, Computer imported)
    {
        ValidateComputer(current);
        ValidateComputer(imported);
        var merged = CloneComputer(imported);
        merged.Monitors = CloneComputer(current).Monitors;
        // A merge preserves the current active profile.
        var importedNameCounts = imported.Monitors.GroupBy(monitor => monitor.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var source in imported.Monitors)
        {
            var target = merged.Monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.DisplayDevicePath, source.DisplayDevicePath, StringComparison.OrdinalIgnoreCase));
            if (target == null && importedNameCounts[source.Name] == 1)
            {
                var namedTargets = merged.Monitors.Where(monitor =>
                    string.Equals(monitor.Name, source.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (namedTargets.Count == 1)
                    target = namedTargets[0];
            }

            if (target == null)
            {
                // Retain disconnected displays so a later reconnect can recover their configuration.
                var disconnected = CloneComputer(new Computer { Monitors = new List<Monitor> { source } }).Monitors[0];
                merged.Monitors.Add(disconnected);
                continue;
            }

            foreach (var profile in source.Profiles)
            {
                if (target.Profiles.Any(existing => ProfilesEquivalent(existing, profile)))
                    continue;
                if (target.Profiles.Count >= 5)
                    throw new InvalidDataException($"Import would exceed five profiles on {target.DisplayName}. Export or remove an unused profile, then retry.");
                target.Profiles.Add(CloneProfile(profile, target, GetUniqueName(target, profile.Name), true));
            }
        }

        ValidateComputer(merged);
        NormalizeProfiles(merged);
        return merged;
    }

    private static Computer CloneComputer(Computer computer)
    {
        var clone = JsonConvert.DeserializeObject<Computer>(JsonConvert.SerializeObject(computer, SerializerSettings), SerializerSettings)
                    ?? throw new InvalidDataException("Unable to clone settings.");
        foreach (var monitor in clone.Monitors)
            foreach (var profile in monitor.Profiles)
                profile.Monitor = monitor;
        return clone;
    }

    private static bool ProfilesEquivalent(Profile first, Profile second) =>
        string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase) &&
        first.ProfileSetting.Brightness == second.ProfileSetting.Brightness &&
        first.ProfileSetting.Contrast == second.ProfileSetting.Contrast &&
        first.ProfileSetting.Gamma == second.ProfileSetting.Gamma &&
        first.ProfileSetting.DigitalVibrance == second.ProfileSetting.DigitalVibrance &&
        first.HotkeyKey == second.HotkeyKey && first.HotkeyModifiers == second.HotkeyModifiers &&
        string.Equals(first.LinkedExecutablePath, second.LinkedExecutablePath, StringComparison.OrdinalIgnoreCase);

    private static string ValidateName(string? name)
    {
        if (name?.Any(char.IsControl) == true)
            throw new InvalidDataException("Profile names cannot contain control characters.");
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumNameLength || name.Any(char.IsControl))
            throw new InvalidDataException("Profile names must contain 1 to 80 characters and no control characters.");
        return name;
    }

    private static void ValidateProfile(Profile? profile)
    {
        if (profile?.ProfileSetting == null)
            throw new InvalidDataException("A profile is missing its color settings.");
        ValidateName(profile.Name);
        var settings = profile.ProfileSetting;
        ValidateRange(settings.Brightness, 0, 1, "brightness");
        ValidateRange(settings.Contrast, 0, 1, "contrast");
        ValidateRange(settings.Gamma, 0.4, 2.8, "gamma");
        ValidateRange(settings.DigitalVibrance, 0, 1, "digital vibrance");
        ValidateHotkey(profile.HotkeyKey, profile.HotkeyModifiers);
        if (profile.LinkedExecutablePath != null && (profile.LinkedExecutablePath.Length > 2048 || profile.LinkedExecutablePath.Any(char.IsControl)))
            throw new InvalidDataException("The linked executable path is invalid.");

    }

    private static void ValidateHotkey(Key? key, ModifierKeys? modifiers)
    {
        const ModifierKeys allowedModifiers = ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows;
        if (modifiers.HasValue && (modifiers.Value & ~allowedModifiers) != 0 ||
            key.HasValue && !Enum.IsDefined(typeof(Key), key.Value))
            throw new InvalidDataException("The file contains an invalid keyboard shortcut.");
    }

    private static void ValidateRange(double value, double minimum, double maximum, string setting)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < minimum || value > maximum)
            throw new InvalidDataException($"The {setting} setting must be between {minimum} and {maximum}.");
    }

    private static void ValidateComputer(Computer? computer)
    {
        if (computer?.Monitors == null || computer.Monitors.Count > 64)
            throw new InvalidDataException("The file contains an invalid monitor list.");
        var devicePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in computer.Monitors)
        {
            if (monitor == null || string.IsNullOrWhiteSpace(monitor.DisplayDevicePath) ||
                monitor.DisplayDevicePath.Length > 2048 || monitor.DisplayDevicePath.Any(char.IsControl) ||
                !devicePaths.Add(monitor.DisplayDevicePath) || string.IsNullOrWhiteSpace(monitor.Name) ||
                monitor.Name.Length > 256 || monitor.Name.Any(char.IsControl) || monitor.Profiles == null ||
                monitor.Profiles.Count == 0 || monitor.Profiles.Count > 5 || monitor.LastBrightness is < 0 or > 100)
                throw new InvalidDataException("The file contains an invalid monitor, duplicate monitor, or brightness setting.");
            if (monitor.CustomName != null && (monitor.CustomName.Length > 80 || monitor.CustomName.Any(char.IsControl)))
                throw new InvalidDataException("Monitor labels must contain at most 80 characters and no control characters.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in monitor.Profiles)
            {
                ValidateProfile(profile);
                if (!names.Add(profile.Name.Trim()))
                    throw new InvalidDataException("The file contains duplicate profile names on one monitor.");
            }
        }
        if (computer.BrightnessShortcutStep is < 1 or > 100 || computer.BrightnessSchedules == null ||
            computer.BrightnessSchedules.Count > 100 || computer.BrightnessSchedules.Any(schedule => schedule == null || !schedule.IsValid) ||
            computer.BrightnessSchedules.GroupBy(schedule => schedule.Time).Any(group => group.Count() > 1))
            throw new InvalidDataException("Brightness schedules must have unique HH:mm times and levels from 0 to 100; shortcut steps must be from 1 to 100.");
        ValidateHotkey(computer.BrightnessIncreaseKey, computer.BrightnessIncreaseModifiers);
        ValidateHotkey(computer.BrightnessDecreaseKey, computer.BrightnessDecreaseModifiers);
        if (computer.IsBrightnessShortcutsEnabled)
        {
            var error = BrightnessAutomationController.GetShortcutValidationError(computer, true) ??
                        BrightnessAutomationController.GetShortcutValidationError(computer, false);
            if (error != null)
                throw new InvalidDataException(error);
        }
    }

    private static void NormalizeProfiles(Computer computer)
    {
        foreach (var monitor in computer.Monitors)
        {
            var defaultProfile = monitor.Profiles.FirstOrDefault(profile => profile.IsDefault) ?? monitor.Profiles[0];
            var activeProfile = monitor.Profiles.FirstOrDefault(profile => profile.IsActiveBeforeAutomaticSwitch == true) ??
                                monitor.Profiles.FirstOrDefault(profile => profile.IsActive) ?? defaultProfile;
            foreach (var profile in monitor.Profiles)
            {
                profile.Monitor = monitor;
                profile.IsDefault = ReferenceEquals(profile, defaultProfile);
                profile.IsActive = ReferenceEquals(profile, activeProfile);
                profile.IsActiveBeforeAutomaticSwitch = null;
            }
        }
    }

    private static void ValidateProfilePayload(JObject profile)
    {
        if (profile["Name"]?.Type != JTokenType.String || profile["ProfileSetting"] is not JObject settings ||
            new[] { "Brightness", "Contrast", "Gamma", "DigitalVibrance" }.Any(property =>
                settings[property]?.Type is not (JTokenType.Integer or JTokenType.Float)))
            throw new InvalidDataException("The file contains a profile with missing or invalid color settings.");
    }

    private static JObject ReadExport(string path, string expectedFormat)
    {
        if (new FileInfo(path).Length > MaximumFileSize)
            throw new InvalidDataException("The import file is larger than 5 MB.");
        using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { MaxDepth = 32 };
        var document = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (document["Format"]?.Type != JTokenType.String || document.Value<string>("Format") != expectedFormat ||
            document["Version"]?.Type != JTokenType.Integer || document.Value<int>("Version") != FormatVersion ||
            document["Data"] is not JObject payload)
            throw new InvalidDataException("Select a supported DisplayFX export file (version 1).");
        if (reader.Read())
            throw new InvalidDataException("The import file contains unexpected trailing data.");
        return payload;
    }

    private static void WriteExport(string path, string format, JObject payload)
    {
        var document = new JObject
        {
            ["Format"] = format,
            ["Version"] = FormatVersion,
            ["Data"] = payload
        };
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                    writer.Write(document.ToString(Formatting.Indented));
                stream.Flush(true);
            }
            if (File.Exists(fullPath))
                File.Replace(temporaryPath, fullPath, null);
            else
                File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
