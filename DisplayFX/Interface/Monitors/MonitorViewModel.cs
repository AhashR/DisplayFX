using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Linq;
using Caliburn.Micro;
using DisplayFX.Interface.Profiles;
using DisplayFX.Objects.Entities;
using WindowsDisplayAPI;

namespace DisplayFX.Interface.Monitors;

public class MonitorViewModel : Screen
{
    private bool _isSelected;
    private ObservableCollection<ProfileViewModel> _profiles;
    private string _customNameDraft;
    private string? _labelError;

    public MonitorViewModel(Monitor monitor, Display display)
    {
        Monitor = monitor;
        Display = display;
        _customNameDraft = monitor.CustomName ?? string.Empty;

        _profiles = new ObservableCollection<ProfileViewModel>();
        Guid = Guid.NewGuid();
    }

    public string Name => Monitor.DisplayName;
    public string HardwareName => Monitor.Name;
    public Size Resolution => Monitor.Resolution;
    public int Frequency => Monitor.Frequency;
    public string ResolutionText => $"{Resolution.Width} × {Resolution.Height} @ {Frequency}Hz";
    public string ScreenName => Display?.DisplayScreen?.ScreenName ?? Display?.ScreenName ?? string.Empty;

    public Monitor Monitor { get; }
    public Display Display { get; private set; }
    public Action<bool, Guid>? IsSelectedChanged { get; set; }
    public Action<MonitorViewModel>? CustomNameChanged { get; set; }
    public Guid Guid { get; }

    public string CustomNameDraft
    {
        get => _customNameDraft;
        set
        {
            if (value == _customNameDraft) return;
            _customNameDraft = value ?? string.Empty;
            LabelError = null;
            NotifyOfPropertyChange();
        }
    }

    public string? LabelError
    {
        get => _labelError;
        private set
        {
            _labelError = value;
            NotifyOfPropertyChange();
        }
    }

    public void ApplyCustomName()
    {
        var originalName = Monitor.CustomName;
        var customName = CustomNameDraft.Trim();
        if (customName.Length > 80 || CustomNameDraft.Any(char.IsControl))
        {
            LabelError = "Use up to 80 characters without control characters.";
            return;
        }
        Monitor.CustomName = customName.Length == 0 ? null : customName;
        try
        {
            CustomNameChanged?.Invoke(this);
            LabelError = null;
            CustomNameDraft = Monitor.CustomName ?? string.Empty;
            NotifyOfPropertyChange(nameof(Name));
        }
        catch (Exception)
        {
            Monitor.CustomName = originalName;
            LabelError = "Could not save this name. Try again.";
        }
    }

    public void ClearCustomName()
    {
        CustomNameDraft = string.Empty;
        ApplyCustomName();
    }

    public void RefreshDisplay(Display display)
    {
        Display = display;
        NotifyOfPropertyChange(nameof(Display));
        NotifyOfPropertyChange(nameof(Name));
        NotifyOfPropertyChange(nameof(HardwareName));
        NotifyOfPropertyChange(nameof(Resolution));
        NotifyOfPropertyChange(nameof(Frequency));
        NotifyOfPropertyChange(nameof(ResolutionText));
        NotifyOfPropertyChange(nameof(ScreenName));
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value == _isSelected) return;
            _isSelected = value;
            NotifyOfPropertyChange();
            IsSelectedChanged?.Invoke(value, Guid);
        }
    }

    public ObservableCollection<ProfileViewModel> Profiles
    {
        get => _profiles;
        set
        {
            if (Equals(value, _profiles)) return;
            _profiles = value;
            NotifyOfPropertyChange();
        }
    }
}
