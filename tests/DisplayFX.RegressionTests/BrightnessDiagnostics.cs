using DisplayFX.Global.Controllers;
using WindowsDisplayAPI;

/// <summary>Optional, explicitly selected read-only hardware diagnostics. Never writes monitor settings.</summary>
internal static class BrightnessDiagnostics
{
    public static int Run()
    {
        var controller = new DiagnosticController();
        foreach (var display in Display.GetDisplays())
        {
            Console.WriteLine($"Display: {display.ScreenName}");
            var value = controller.GetBrightness(display);
            Console.WriteLine($"Brightness: {value?.ToString() ?? "unreadable"}%");
        }
        return 0;
    }

    private sealed class DiagnosticController : MonitorBrightnessController
    {
        public DiagnosticController() : base(NLog.LogManager.GetLogger("BrightnessDiagnostics")) { }
        protected override bool ReadHighLevel(IntPtr handle, out uint min, out uint current, out uint max)
        {
            var result = base.ReadHighLevel(handle, out min, out current, out max);
            Console.WriteLine(result ? $"  High-level: {current}, range {min}–{max}" : "  High-level read unavailable");
            return result;
        }
        protected override bool ReadVcp(IntPtr handle, out uint current, out uint max)
        {
            var result = base.ReadVcp(handle, out current, out max);
            Console.WriteLine(result ? $"  VCP: {current}, range 0–{max}" : "  VCP read unavailable");
            return result;
        }
    }
}
