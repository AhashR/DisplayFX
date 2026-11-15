using DisplayFX.Global.Controllers;
using NLog;
using WindowsDisplayAPI;

internal static class HardwareCapabilityDiagnostics
{
    public static int Run()
    {
        var controller = new MonitorBrightnessController(LogManager.GetLogger("HardwareCapabilities"), new WmiInternalPanelBrightness());
        try
        {
            NvAPIWrapper.Display.Display[] nvidia;
            try { NvAPIWrapper.NVIDIA.Initialize(); nvidia = NvAPIWrapper.Display.Display.GetDisplays(); }
            catch { nvidia = Array.Empty<NvAPIWrapper.Display.Display>(); }
            foreach (var display in Display.GetDisplays())
            {
                Console.WriteLine("Display: " + display.ScreenName);
                Console.WriteLine("  Advanced color: " + (DisplayCapabilitiesController.IsAdvancedColorEnabled(display)?.ToString() ?? "unknown"));
                Console.WriteLine("  Brightness: " + (controller.GetBrightness(display)?.ToString() ?? "unavailable"));
                var gpu = nvidia.FirstOrDefault(item => item.Name == display.ScreenName);
                Console.WriteLine("  NVIDIA vibrance read: " + (gpu?.DigitalVibranceControl.NormalizedLevel.ToString() ?? "unavailable"));
                foreach (var feature in controller.ReadVcpFeatures(display))
                    Console.WriteLine($"  VCP 0x{feature.Code:X2}: {feature.Name}; current={feature.Current}; max={feature.Maximum}; allowed={feature.AllowedValuesText}");
            }
            Console.WriteLine("Read-only diagnostics complete. No display settings were written.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
