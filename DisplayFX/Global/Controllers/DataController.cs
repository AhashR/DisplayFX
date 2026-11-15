using System;
using System.IO;
using System.Linq;
using System.Text;
using FluentResults;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Global.Controllers;

public class DataController
{
    private readonly object _sync = new();
    private readonly string _legacyDataPath;
    private DateTime _lastSnapshot;
    private bool _formatChecked;

    public DataController() : this(
        File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag")) ? Path.Combine(AppContext.BaseDirectory, "Data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayFX"),
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
    public string SnapshotDirectory => Path.Combine(Path.GetDirectoryName(DataPath)!, "History");
    public string[] GetSnapshots() => Directory.Exists(SnapshotDirectory) ? Directory.GetFiles(SnapshotDirectory, "*.json").OrderByDescending(path => path, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
    public Computer ReadSnapshot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(SnapshotDirectory), StringComparison.OrdinalIgnoreCase) || Path.GetExtension(fullPath) != ".json")
            throw new InvalidDataException("Select a DisplayFX history snapshot.");
        return Read(fullPath);
    }
    public string CreateSnapshot(Computer computer)
    {
        lock (_sync)
        {
            ProfileTransferController.ValidateStoredComputer(computer);
            Directory.CreateDirectory(SnapshotDirectory);
            var path = Path.Combine(SnapshotDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(computer), new UTF8Encoding(false));
            PruneSnapshots(computer.Features.BackupRetention);
            _lastSnapshot = DateTime.UtcNow;
            return path;
        }
    }
    private void PruneSnapshots(int retention)
    {
        foreach (var path in GetSnapshots().Where(path => !Path.GetFileName(path).StartsWith("upgrade-before-", StringComparison.Ordinal)).Skip(Math.Clamp(retention, 1, 100))) File.Delete(path);
    }

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
        if (new FileInfo(path).Length > 5 * 1024 * 1024) throw new InvalidDataException("Settings exceed 5 MB.");
        using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { MaxDepth = 64 };
        var document = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new InvalidDataException("Settings contain trailing data.");
        var computer = document.ToObject<Computer>();
        if (computer?.Monitors == null)
            throw new InvalidDataException("Settings contain no computer or monitor list.");
        if (computer.DataFormatVersion > 2) throw new NotSupportedException("These settings were saved by a newer DisplayFX format. Upgrade DisplayFX; the file was not replaced.");
        ProfileTransferController.ValidateStoredComputer(computer);
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
        if (Encoding.UTF8.GetByteCount(json) > 5 * 1024 * 1024) throw new InvalidDataException("Settings exceed the supported 5 MB size. Shorten notes or remove unused profiles before saving.");
        Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
        var formatSource = keepBackup && File.Exists(DataPath) ? DataPath : File.Exists(BackupPath) ? BackupPath : null;
        if (!_formatChecked && formatSource != null)
        {
            JObject? previous = null;
            try { previous = JObject.Parse(File.ReadAllText(formatSource)); } catch (JsonException) { }
            if (previous != null && previous["DataFormatVersion"]?.ToString() != "2")
            {
                Directory.CreateDirectory(SnapshotDirectory);
                var protectedBackup = Path.Combine(SnapshotDirectory, "upgrade-before-format-2.json");
                if (!File.Exists(protectedBackup)) File.Copy(formatSource, protectedBackup);
            }
        }
        _formatChecked = true;
        if (keepBackup && data.Features.VersionedBackups && File.Exists(DataPath) && DateTime.UtcNow - _lastSnapshot >= TimeSpan.FromMinutes(1))
        {
            try
            {
                Directory.CreateDirectory(SnapshotDirectory);
                File.Copy(DataPath, Path.Combine(SnapshotDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json"));
                PruneSnapshots(data.Features.BackupRetention);
                _lastSnapshot = DateTime.UtcNow;
            }
            catch (Exception ex) { NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Could not create an automatic settings snapshot; primary atomic save will continue."); }
        }
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
