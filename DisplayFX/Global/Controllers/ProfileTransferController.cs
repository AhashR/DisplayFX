using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
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
    public const int FormatVersion = 2;
    public const int MaximumNameLength = 80;
    public const int MaximumProfilesPerMonitor = 100;
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
            var candidate = GetCopyName(name, suffix);
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
        return new Profile(target, ValidateName(name), source.ProfileSetting.Copy(), false)
        {
            HotkeyModifiers = includeTriggers ? source.HotkeyModifiers : null,
            HotkeyKey = includeTriggers ? source.HotkeyKey : null,
            LinkedExecutablePath = includeTriggers ? source.LinkedExecutablePath : null
            , Notes = source.Notes, Icon = source.Icon, MarkerColor = source.MarkerColor,
            AdditionalExecutablePaths = includeTriggers ? new(source.AdditionalExecutablePaths) : new(),
            RulePriority = includeTriggers ? source.RulePriority : 0,
            FullscreenOnly = includeTriggers && source.FullscreenOnly,
            PowerCondition = includeTriggers ? source.PowerCondition : "Any"
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

    public static Profile ReadProfile(string path, Monitor target)
    {
        var payload = ReadExport(path, ProfileFormat);
        ValidateProfilePayload(payload);
        var profile = payload.ToObject<Profile>(JsonSerializer.Create(SerializerSettings))
            ?? throw new InvalidDataException("The file contains no profile.");
        ValidateProfile(profile);
        // Import color settings only; importing a trigger must never steal an existing shortcut/application.
        return CloneProfile(profile, target, GetUniqueName(target, profile.Name));
    }

    public static void ValidateStoredComputer(Computer computer) => ValidateComputer(computer, false);

    public static string? GetExecutableConflict(Profile profile)
    {
        var paths = profile.AdditionalExecutablePaths.Concat(new[] { profile.LinkedExecutablePath }).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (paths.Count == 0) return null;
        return profile.Monitor.Profiles.Any(other => !ReferenceEquals(other, profile) && other.RulePriority == profile.RulePriority &&
            (profile.PowerCondition == "Any" || other.PowerCondition == "Any" || profile.PowerCondition == other.PowerCondition) &&
            other.AdditionalExecutablePaths.Concat(new[] { other.LinkedExecutablePath }).Any(otherPath => paths.Any(path =>
                ProcessController.MatchesExecutable(otherPath, path) || ProcessController.MatchesExecutable(path, otherPath))))
            ? "This application is already linked to another profile on this monitor. Unlink it there first." : null;
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
        var profileMap = new Dictionary<(string Path, Guid Id), (string Path, Guid Id)>();
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
                foreach (var profile in disconnected.Profiles) profileMap[(source.DisplayDevicePath.ToUpperInvariant(), profile.Id)] = (disconnected.DisplayDevicePath, profile.Id);
                continue;
            }

            foreach (var profile in source.Profiles)
            {
                var equivalent = target.Profiles.FirstOrDefault(existing => ProfilesEquivalent(existing, profile, source.Profiles));
                if (equivalent != null)
                {
                    profileMap[(source.DisplayDevicePath.ToUpperInvariant(), profile.Id)] = (target.DisplayDevicePath, equivalent.Id);
                    continue;
                }
                if (target.Profiles.Count >= MaximumProfilesPerMonitor)
                    throw new InvalidDataException($"Import would exceed {MaximumProfilesPerMonitor} profiles on {target.DisplayName}. Export or remove an unused profile, then retry.");
                var copy = CloneProfile(profile, target, GetUniqueName(target, profile.Name), true);
                copy.ImportedProfileName = profile.Name;
                target.Profiles.Add(copy);
                profileMap[(source.DisplayDevicePath.ToUpperInvariant(), profile.Id)] = (target.DisplayDevicePath, copy.Id);
            }
        }

        foreach (var entry in merged.Features.ProfileSets.SelectMany(set => set.Entries))
            if (profileMap.TryGetValue((entry.DevicePath.ToUpperInvariant(), entry.ProfileId), out var mapped))
            { entry.DevicePath = mapped.Path; entry.ProfileId = mapped.Id; }
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

    private static string GetCopyName(string name, string suffix) =>
        name.Substring(0, Math.Min(name.Length, MaximumNameLength - suffix.Length)) + suffix;

    private static bool IsImportedCopyName(string candidate, string sourceName)
    {
        var suffixStart = candidate.LastIndexOf(" (", StringComparison.Ordinal);
        if (suffixStart < 0 || !candidate.EndsWith(')') ||
            !int.TryParse(candidate.AsSpan(suffixStart + 2, candidate.Length - suffixStart - 3),
                NumberStyles.None, CultureInfo.InvariantCulture, out var copy) || copy < 2)
            return false;
        var suffix = $" ({copy})";
        return string.Equals(candidate, GetCopyName(sourceName, suffix), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ProfilesEquivalent(Profile first, Profile second, IReadOnlyList<Profile> importedProfiles) =>
        ProfileValuesEquivalent(first, second) &&
        (string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(first.ImportedProfileName, second.Name, StringComparison.OrdinalIgnoreCase) ||
         // Recognize copies made by older releases only when their truncated name is unambiguous.
         first.ImportedProfileName == null && IsImportedCopyName(first.Name, second.Name) &&
         !importedProfiles.Any(other => other != second && IsImportedCopyName(first.Name, other.Name) &&
             ProfileValuesEquivalent(first, other)));

    private static bool ProfileValuesEquivalent(Profile first, Profile second) =>
        first.ProfileSetting.Brightness == second.ProfileSetting.Brightness &&
        first.ProfileSetting.Contrast == second.ProfileSetting.Contrast &&
        first.ProfileSetting.Gamma == second.ProfileSetting.Gamma &&
        first.ProfileSetting.DigitalVibrance == second.ProfileSetting.DigitalVibrance &&
        first.ProfileSetting.RedGain == second.ProfileSetting.RedGain && first.ProfileSetting.GreenGain == second.ProfileSetting.GreenGain && first.ProfileSetting.BlueGain == second.ProfileSetting.BlueGain && first.ProfileSetting.TemperatureKelvin == second.ProfileSetting.TemperatureKelvin &&
        first.HotkeyKey == second.HotkeyKey && first.HotkeyModifiers == second.HotkeyModifiers &&
        first.AdditionalExecutablePaths.SequenceEqual(second.AdditionalExecutablePaths, StringComparer.OrdinalIgnoreCase) &&
        first.RulePriority == second.RulePriority && first.FullscreenOnly == second.FullscreenOnly && first.PowerCondition == second.PowerCondition &&
        first.Notes == second.Notes && first.Icon == second.Icon && first.MarkerColor == second.MarkerColor &&
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
        if (profile.Id == Guid.Empty) throw new InvalidDataException("Profile identity is invalid.");
        if (profile.Notes == null || profile.Notes.Length > 4000 || profile.Icon == null || profile.Icon.Length > 8 ||
            profile.AdditionalExecutablePaths == null || profile.AdditionalExecutablePaths.Count > 32 ||
            profile.AdditionalExecutablePaths.Any(path => string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.Any(char.IsControl)) ||
            profile.PowerCondition is not ("Any" or "AC" or "Battery") || profile.RulePriority is < -100 or > 100 ||
            profile.MarkerColor == null || !System.Text.RegularExpressions.Regex.IsMatch(profile.MarkerColor, "^#[0-9a-fA-F]{6}$"))
            throw new InvalidDataException("Profile metadata or application rules are invalid.");
        if (profile.ImportedProfileName != null) ValidateName(profile.ImportedProfileName);
        var settings = profile.ProfileSetting;
        ValidateRange(settings.Brightness, 0, 1, "brightness");
        ValidateRange(settings.Contrast, 0, 1, "contrast");
        ValidateRange(settings.Gamma, 0.4, 2.8, "gamma");
        ValidateRange(settings.DigitalVibrance, 0, 1, "digital vibrance");
        ValidateRange(settings.RedGain, 0, 1, "red gain"); ValidateRange(settings.GreenGain, 0, 1, "green gain"); ValidateRange(settings.BlueGain, 0, 1, "blue gain");
        ValidateRange(settings.TemperatureKelvin, 2500, 10000, "temperature tint");
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

    private static void ValidateComputer(Computer? computer, bool validateTriggers = true)
    {
        if (computer?.Monitors == null || computer.Monitors.Count > 64)
            throw new InvalidDataException("The file contains an invalid monitor list.");
        if (computer.Features == null || computer.Features.Validate() is string)
            throw new InvalidDataException(computer.Features?.Validate() ?? "Missing application preferences.");
        var devicePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in computer.Monitors)
        {
            if (monitor == null || string.IsNullOrWhiteSpace(monitor.DisplayDevicePath) ||
                monitor.DisplayDevicePath.Length > 2048 || monitor.DisplayDevicePath.Any(char.IsControl) ||
                !devicePaths.Add(monitor.DisplayDevicePath) || string.IsNullOrWhiteSpace(monitor.Name) ||
                monitor.Name.Length > 256 || monitor.Name.Any(char.IsControl) || monitor.Profiles == null ||
                monitor.Profiles.Count == 0 || monitor.Profiles.Count > MaximumProfilesPerMonitor || monitor.LastBrightness is < 0 or > 100)
                throw new InvalidDataException("The file contains an invalid monitor, duplicate monitor, or brightness setting.");
            if (monitor.CustomName != null && (monitor.CustomName.Length > 80 || monitor.CustomName.Any(char.IsControl)))
                throw new InvalidDataException("Monitor labels must contain at most 80 characters and no control characters.");
            if (monitor.BrightnessGroup == null || monitor.BrightnessGroup.Length > 80 || monitor.MinimumBrightness is < 0 or > 100 ||
                monitor.MaximumBrightness is < 0 or > 100 || monitor.MinimumBrightness > monitor.MaximumBrightness || monitor.BrightnessOffset is < -100 or > 100)
                throw new InvalidDataException("Monitor brightness limits, offset or group are invalid.");
            if (monitor.HardwareIdentity != null && (monitor.HardwareIdentity.Length > 256 || monitor.HardwareIdentity.Any(char.IsControl)))
                throw new InvalidDataException("Monitor identity is invalid.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identities = new HashSet<Guid>();
            foreach (var profile in monitor.Profiles)
            {
                ValidateProfile(profile);
                profile.Monitor = monitor;
                if (validateTriggers && GetExecutableConflict(profile) is string conflict) throw new InvalidDataException(conflict);
                if (!names.Add(profile.Name.Trim()))
                    throw new InvalidDataException("The file contains duplicate profile names on one monitor.");
                if (!identities.Add(profile.Id)) throw new InvalidDataException("The file contains duplicate profile identities on one monitor.");
            }
        }
        if (computer.BrightnessShortcutStep is < 1 or > 100 || computer.BrightnessSchedules == null ||
            computer.BrightnessSchedules.Count > 100 || computer.BrightnessSchedules.Any(schedule => schedule == null || !schedule.IsValid) ||
            BrightnessAutomationController.HasScheduleConflicts(computer.BrightnessSchedules))
            throw new InvalidDataException("Brightness schedules must have unique HH:mm times and levels from 0 to 100; shortcut steps must be from 1 to 100.");
        ValidateHotkey(computer.BrightnessIncreaseKey, computer.BrightnessIncreaseModifiers);
        if (computer.IsBrightnessScheduleEnabled && computer.BrightnessSchedules.Any(schedule => schedule.SolarEvent != "Fixed") && !computer.Features.SolarLatitude.HasValue)
            throw new InvalidDataException("Solar schedules need a saved latitude and longitude before they can be enabled.");
        ValidateHotkey(computer.BrightnessDecreaseKey, computer.BrightnessDecreaseModifiers);
        if (validateTriggers && computer.IsBrightnessShortcutsEnabled)
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
            document["Version"]?.Type != JTokenType.Integer || document.Value<int>("Version") is not (1 or FormatVersion) ||
            document["Data"] is not JObject payload)
            throw new InvalidDataException("Select a supported DisplayFX export file (version 1 or 2).");
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
        var serialized = document.ToString(Formatting.Indented);
        if (Encoding.UTF8.GetByteCount(serialized) > MaximumFileSize)
            throw new InvalidDataException("The export exceeds the 5 MB import limit. Export fewer profiles.");
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                    writer.Write(serialized);
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
