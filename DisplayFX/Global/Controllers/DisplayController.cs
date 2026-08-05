using System;
using NLog;
using DisplayFX.Objects;
using DisplayFX.Objects.Entities;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

public class DisplayController
{
    private readonly ILogger _logger;
    private readonly DisplayWindowManager _windowManager;

    public DisplayController(ILogger logger, DisplayWindowManager windowManager)
    {
        _logger = logger;
        _windowManager = windowManager;
    }

    public void UpdateColorSettings(Display display, ProfileSetting profileSetting,
        NvAPIWrapper.Display.Display? nvidiaMonitor)
    {
        try
        {
            display.GammaRamp =
                new DisplayGammaRamp(profileSetting.Brightness, profileSetting.Contrast, profileSetting.Gamma);
            if (nvidiaMonitor is not null)
                nvidiaMonitor.DigitalVibranceControl.NormalizedLevel = profileSetting.DigitalVibrance - .3;
        }
        catch (Exception e)
        {
            var message = "Failed to update color settings.";

            _logger.Error(message);
            _logger.Error(e);

            _windowManager.ShowMessageBox(message);
        }
    }
}