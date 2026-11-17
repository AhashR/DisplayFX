using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Net.Http;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using DisplayFX.Global;
using DisplayFX.Interface.Profiles;
using DisplayFX.Global.Controllers;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Interface.Shell;

public partial class ShellViewModel
{
    private string? ImportSingleProfile(Window? owner)
    {
        var target = SelectedMonitor;
        if (target == null || !CanAddProfile) return null;
        var dialog = new OpenFileDialog { Filter = "DisplayFX profile (*.json)|*.json", Title = "Import profile" };
        if ((owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) != true) return null;
        try
        {
            ImportProfileFile(target, dialog.FileName);
            return OperationStatus = "Profile imported.";
        }
        catch (Exception ex) { ReportFailure("Could not import profile.", ex); return OperationStatus; }
    }

    private string? ExportSelectedProfile(Window? owner)
    {
        var profile = SelectedProfile;
        if (profile == null) return null;
        var dialog = new SaveFileDialog { Title = "Export profile", Filter = "DisplayFX profile (*.json)|*.json",
            FileName = "DisplayFX-profile.json", DefaultExt = ".json" };
        if ((owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) != true) return null;
        try { ProfileTransferController.ExportProfile(profile.Profile, dialog.FileName); return OperationStatus = "Profile exported."; }
        catch (Exception ex) { ReportFailure("Could not export profile.", ex); return OperationStatus; }
    }

    internal void RemoveExistingProfileShortcut(ProfileViewModel model)
    {
        // Commit only the shortcut removal, leaving any other edits in the current draft.
        var oldKey = model.Profile.HotkeyKey;
        var oldModifiers = model.Profile.HotkeyModifiers;
        model.Profile.HotkeyKey = null;
        model.Profile.HotkeyModifiers = null;
        try
        {
            Write();
            model.ProfileSettings?.ForgetRemovedShortcut();
            RefreshAllHotkeys();
            OperationStatus = "Profile shortcut removed.";
        }
        catch (Exception ex)
        {
            model.Profile.HotkeyKey = oldKey;
            model.Profile.HotkeyModifiers = oldModifiers;
            ReportFailure("Could not remove profile shortcut.", ex);
        }
    }

    internal void SetProfileIcon(ProfileViewModel model, string icon)
    {
        if (!ProfileIcons.Choices.Any(choice => choice.Id == icon) || model.Profile.Icon == icon) return;
        var previous = model.Profile.Icon;
        SaveProfileMetadata(model, () => model.Profile.Icon = icon, () => model.Profile.Icon = previous);
    }

    private void SaveProfileMetadata(ProfileViewModel model, Action apply, Action revert)
    {
        var modified = model.Profile.ModifiedAt;
        apply();
        model.Profile.ModifiedAt = DateTimeOffset.UtcNow;
        try { PersistFeatureChanges(); OperationStatus = "Profile details saved."; }
        catch (Exception ex) { revert(); model.Profile.ModifiedAt = modified; ReportFailure("Could not save profile details.", ex); }
    }

    private async Task<string> CheckForUpdates()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DisplayFX/" + AppVersion.Current);
            var releases = JArray.Parse(await client.GetStringAsync("https://api.github.com/repos/AhashR/DisplayFX/releases?per_page=20"));
            var release = releases.OfType<JObject>().FirstOrDefault(item => item.Value<bool?>("draft") != true && item.Value<bool?>("prerelease") != true);
            var latest = release?.Value<string>("tag_name");
            var status = latest == null ? "No published release was found." : $"Installed: {AppVersion.Current}. Latest published release: {latest}.";
            return status;
        }
        catch (Exception ex) { ReportFailure("Could not check for updates.", ex); return OperationStatus; }
    }

    internal void PersistFeatureChanges()
    {
        ProfileTransferController.ValidateStoredComputer(Computer);
        Write();
        foreach (var monitor in Monitors)
            foreach (var profile in monitor.Profiles) profile.RefreshMetadata();
        foreach (var property in new[] { nameof(VisibleProfiles), nameof(VisibleMonitors), nameof(ApplyActionLabel), nameof(RevertActionLabel), nameof(SaveActionLabel) }) NotifyOfPropertyChange(property);
        _appSwitching.Refresh();
        UpdateTrayTooltip();
    }
}
