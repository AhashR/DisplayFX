using System;
using System.IO;
using System.Linq;
using System.Text;
using FluentResults;
using Newtonsoft.Json;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Global.Controllers;

public class DataController
{
    private readonly object _sync = new();
    private readonly string _legacyDataPath;

    public DataController() : this(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayFX"),
        Path.Combine(AppContext.BaseDirectory, "Data", "Data.json"))
    {
    }

    public DataController(string dataDirectory, string legacyDataPath)
    {
        DataPath = Path.Combine(dataDirectory, "Data.json");
        _legacyDataPath = legacyDataPath;
    }

    public string DataPath { get; }
    public string BackupPath => DataPath + ".bak";

    public bool HasSavedData => File.Exists(DataPath) || File.Exists(BackupPath) ||
        (File.Exists(_legacyDataPath) && new FileInfo(_legacyDataPath).Length > 0);

    public void Write(Computer data)
    {
        lock (_sync)
            WriteAtomically(data, true);
    }

    public Result<Computer> Load()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(DataPath))
                {
                    try
                    {
                        return Result.Ok(Read(DataPath));
                    }
                    catch (Exception ex) when (ex is JsonException || ex is InvalidDataException)
                    {
                        if (!File.Exists(BackupPath))
                            throw;

                        var recovered = Read(BackupPath);
                        File.Copy(DataPath, DataPath + ".corrupt-" + Guid.NewGuid().ToString("N"));
                        WriteAtomically(recovered, false);
                        return Result.Ok(recovered);
                    }
                }

                if (File.Exists(BackupPath))
                {
                    var recovered = Read(BackupPath);
                    WriteAtomically(recovered, false);
                    return Result.Ok(recovered);
                }

                // The shipped legacy template is empty; only import actual saved settings.
                if (File.Exists(_legacyDataPath) && new FileInfo(_legacyDataPath).Length > 0)
                {
                    var migrated = Read(_legacyDataPath);
                    WriteAtomically(migrated, true);
                    return Result.Ok(migrated);
                }

                return Result.Fail(new Error("No saved settings were found."));
            }
            catch (Exception ex)
            {
                return Result.Fail(new Error($"Unable to load settings: {ex.Message}").CausedBy(ex));
            }
        }
    }

    private static Computer Read(string path)
    {
        var computer = JsonConvert.DeserializeObject<Computer>(File.ReadAllText(path));
        if (computer?.Monitors == null)
            throw new InvalidDataException("Settings contain no computer or monitor list.");
        if (computer.BrightnessSchedules == null || computer.BrightnessSchedules.Any(schedule => schedule == null || !schedule.IsValid) ||
            computer.BrightnessSchedules.Count > 100 || computer.BrightnessShortcutStep is < 1 or > 100)
            throw new InvalidDataException("Settings contain an invalid brightness schedule or shortcut step.");

        foreach (var monitor in computer.Monitors)
        {
            if (monitor == null || string.IsNullOrWhiteSpace(monitor.DisplayDevicePath) ||
                monitor.Profiles == null || monitor.Profiles.Any(p => p == null || p.ProfileSetting == null))
                throw new InvalidDataException("Settings contain an invalid monitor or profile.");

            foreach (var profile in monitor.Profiles)
                profile.Monitor = monitor;

            var defaultProfile = monitor.Profiles.FirstOrDefault(p => p.IsDefault) ?? monitor.Profiles.FirstOrDefault();
            var activeProfile = monitor.Profiles.FirstOrDefault(p => p.IsActiveBeforeAutomaticSwitch == true) ??
                                monitor.Profiles.FirstOrDefault(p => p.IsActive) ?? defaultProfile;
            foreach (var profile in monitor.Profiles)
            {
                profile.IsDefault = ReferenceEquals(profile, defaultProfile);
                profile.IsActive = ReferenceEquals(profile, activeProfile);
                profile.IsActiveBeforeAutomaticSwitch = null;
            }
        }

        return computer;
    }

    private void WriteAtomically(Computer data, bool keepBackup)
    {
        var json = JsonConvert.SerializeObject(data, new JsonSerializerSettings
        {
            PreserveReferencesHandling = PreserveReferencesHandling.Objects
        });
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
        var temporaryPath = DataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                    writer.Write(json);
                stream.Flush(true);
            }

            if (File.Exists(DataPath))
                File.Replace(temporaryPath, DataPath, keepBackup ? BackupPath : null);
            else
                File.Move(temporaryPath, DataPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
