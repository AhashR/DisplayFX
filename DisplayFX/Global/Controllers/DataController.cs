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

    private readonly string[] _legacyDataPaths;
    public DataController() : this(
        ResolveWorkingDirectory(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayFX", "Data.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayFX", "Data.json.bak"),
        Path.Combine(AppContext.BaseDirectory, "Data", "Data.json"))
    {
    }

    public DataController(string dataDirectory, params string[] legacyDataPaths)
    {
        DataPath = Path.Combine(dataDirectory, "Data.json");
        _legacyDataPaths = legacyDataPaths.Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(DataPath), StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static string ResolveWorkingDirectory()
    {
        var legacyData = Path.Combine(AppContext.BaseDirectory, "Data");
        var installData = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"))
            ? legacyData : Path.Combine(AppContext.BaseDirectory, "Settings");
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var userData = string.IsNullOrWhiteSpace(documents)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DisplayFX")
            : Path.Combine(documents, "DisplayFX");
        if (File.Exists(Path.Combine(userData, "Data.json"))) return userData;
        try
        {
            Directory.CreateDirectory(installData);
            var probe = Path.Combine(installData, Guid.NewGuid().ToString("N") + ".tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return installData;
        }
        catch (UnauthorizedAccessException) { return userData; }
        catch (IOException) { return userData; }
    }

    public string DataPath { get; }
    public bool HasSavedData => File.Exists(DataPath) || _legacyDataPaths.Any(path => File.Exists(path) && new FileInfo(path).Length > 0);

    public void Write(Computer data)
    {
        lock (_sync)
            WriteAtomically(data);
    }

    public Result<Computer> Load()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(DataPath))
                    return Result.Ok(Read(DataPath));

                Exception? legacyError = null;
                foreach (var legacyPath in _legacyDataPaths.Where(path => File.Exists(path) && new FileInfo(path).Length > 0))
                {
                    Computer migrated;
                    try { migrated = Read(legacyPath); }
                    catch (Exception ex) when (ex is JsonException or InvalidDataException)
                    { legacyError = ex; continue; }
                    WriteAtomically(migrated);
                    return Result.Ok(migrated);
                }
                if (legacyError != null) throw legacyError;

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
        foreach (var key in new[] { "BrightnessSchedules", "IsBrightnessScheduleEnabled", "IsColorDiagnosticsEnabled" })
            document.Property(key, StringComparison.OrdinalIgnoreCase)?.Remove();
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

    private void WriteAtomically(Computer data)
    {
        var json = JsonConvert.SerializeObject(data, new JsonSerializerSettings
        {
            PreserveReferencesHandling = PreserveReferencesHandling.Objects
        });
        if (Encoding.UTF8.GetByteCount(json) > 5 * 1024 * 1024) throw new InvalidDataException("Settings exceed the supported 5 MB size. Shorten notes or remove unused profiles before saving.");
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
                File.Replace(temporaryPath, DataPath, null);
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
