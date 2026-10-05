using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DisplayFX.Global.Controllers;
using DisplayFX.Objects.Entities;
using NLog;
using WindowsDisplayAPI;

internal static class BrightnessRegressionChecks
{
    private static int _checkCount;

    public static int Run()
    {
        _checkCount = 0;
        CheckBrightnessRanges();
        CheckNativeTransportSelection();
        var destroyParameter = typeof(MonitorBrightnessController)
            .GetMethod("DestroyPhysicalMonitors", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[1];
        Equal(true, destroyParameter.IsIn && !destroyParameter.IsOut,
            "Physical monitor cleanup must marshal existing handles into the native API");
        CheckFailedPersistenceDoesNotConsumeTheChange();
        return _checkCount;
    }

    private static void CheckBrightnessRanges()
    {
        Equal(0, Normalize(20, 10, 220), "Brightness below the hardware minimum clamps to zero");
        Equal(100, Normalize(20, 300, 220), "Brightness above the hardware maximum clamps to 100");
        Equal(50, Normalize(20, 120, 220), "A nonzero hardware minimum is normalized");
        Equal(20u, Scale(0, 20, 220), "Zero percent maps to the hardware minimum");
        Equal(120u, Scale(50, 20, 220), "Half brightness maps into the hardware range");
        Equal(220u, Scale(100, 20, 220), "Full brightness maps to the hardware maximum");
        Equal(20u, Scale(-10, 20, 220), "Negative percentages clamp to the hardware minimum");
        Equal(220u, Scale(150, 20, 220), "Excess percentages clamp to the hardware maximum");
        Equal(75, Normalize(75, 0), "A missing VCP maximum falls back to percentages");
        Equal(75u, Scale(75, 0), "A missing VCP maximum can still be written as a percentage");
        Equal(75, Normalize(20, 75, 20), "A degenerate hardware range falls back to percentages");
        Equal(75u, Scale(75, 20, 20), "A degenerate hardware range can still be written as a percentage");
        Equal(2147483648u, Scale(50, uint.MaxValue), "Large VCP ranges do not overflow signed integers");

        for (var brightness = 0; brightness <= 100; brightness++)
        {
            Equal(brightness, Normalize(20, Scale(brightness, 20, 220), 220),
                "Brightness round-trips through a nonstandard hardware range");
            Equal(brightness, Normalize(Scale(brightness, 255), 255),
                "Brightness round-trips through an 8-bit VCP range");
        }
    }

    private static void CheckFailedPersistenceDoesNotConsumeTheChange()
    {
        var temporaryDirectory = Path.Combine(Directory.GetCurrentDirectory(), "obj", "regression-data",
            "brightness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var blockedDirectory = Path.Combine(temporaryDirectory, "not-a-directory");
            File.WriteAllText(blockedDirectory, "sentinel");
            var dataController = new DataController(blockedDirectory, Path.Combine(temporaryDirectory, "missing-legacy.json"));
            var logger = LogManager.GetLogger("BrightnessRegressionChecks");
            var persistence = new BrightnessPersistenceController(dataController, new DisplayCache(),
                new MonitorBrightnessController(logger), logger);
            var computer = new Computer();
            var monitor = new DisplayFX.Objects.Entities.Monitor("display-test", "Regression display",
                new System.Drawing.Size(1920, 1080), 60) { LastBrightness = 42 };
            computer.Monitors.Add(monitor);
            persistence.Attach(computer);
            var display = new FakeDisplay("DISPLAY-TEST");
            var brightnessController = new MonitorBrightnessController(logger);
            Equal(false, brightnessController.SetBrightness(display, 75), "Detached displays cannot report a successful write");
            Equal(false, brightnessController.SupportsBrightness(display), "Detached displays have no brightness controls");
            Equal<int?>(null, brightnessController.GetBrightness(display), "Detached displays have no readable brightness");

            persistence.Remember(display, 75);
            Equal(42, monitor.LastBrightness!.Value, "Failed persistence preserves the previously saved brightness");
            Equal("sentinel", File.ReadAllText(blockedDirectory), "Failed persistence leaves existing files intact");

            File.Delete(blockedDirectory);
            persistence.Remember(display, 75);
            Equal(75, monitor.LastBrightness!.Value, "The same requested brightness can be retried after a failure");
            Equal(75, dataController.Load().Value.Monitors[0].LastBrightness!.Value,
                "The retried brightness reaches storage");

            dataController.Write(new Computer());
            persistence.Detach();
            persistence.Remember(display, 90);
            Equal(75, monitor.LastBrightness!.Value, "Detached persistence does not modify the previous computer");
            Equal(0, dataController.Load().Value.Monitors.Count,
                "Closing brightness controls during reset cannot overwrite the saved defaults");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }

    private static void CheckNativeTransportSelection()
    {
        var hardware = new NativeFixture();
        var raw = new FakeDisplay("raw");
        var high = new FakeDisplay("high");
        Equal<int?>(50, hardware.GetBrightness(raw), "Raw brightness is normalized to its reported range");
        var probes = hardware.Reads;
        Equal(true, hardware.SetBrightness(raw, 80), "A raw-only monitor accepts a matching write");
        Equal(204u, hardware.LastRawValue, "Raw writes use the cached nonstandard maximum");
        Equal(0, hardware.HighWrites, "A raw-only read must not be followed by a blind high-level write");
        Equal(probes, hardware.Reads, "Healthy writes must not re-query the hardware range");
        Equal<int?>(50, hardware.GetBrightness(high), "High-level brightness uses its nonzero minimum");
        Equal(true, hardware.SetBrightness(high, 25), "High-level displays retain their working transport");
        Equal(70u, hardware.LastHighValue, "High-level writes use the cached minimum and maximum");
        hardware.HighWriteFails = true;
        Equal(true, hardware.SetBrightness(high, 75), "A failed preferred write falls back to the other supported transport");
        Equal(191u, hardware.LastRawValue, "Fallback writes are rescaled to the fallback transport range");
        probes = hardware.Reads;
        var highWrites = hardware.HighWrites;
        Equal(true, hardware.SetBrightness(high, 10), "The working fallback remains usable");
        Equal(highWrites, hardware.HighWrites, "A successful fallback becomes the preferred write transport");
        Equal(probes, hardware.Reads, "Fallback success avoids repeated probing on later writes");
        hardware.MaximumConcurrentWrites = 0;
        Task.WaitAll(Task.Run(() => hardware.SetBrightness(raw, 30)), Task.Run(() => hardware.SetBrightness(raw, 90)));
        Equal(1, hardware.MaximumConcurrentWrites, "Concurrent requests for one monitor are serialized");
        Equal(hardware.Acquired, hardware.Released, "All physical monitor handles are released after reads and writes");
        hardware.RawWriteFails = true;
        hardware.HighWriteFails = false;
        Equal(true, hardware.SetBrightness(raw, 35), "Write-only high-level firmware remains usable when raw writes fail");
        Equal(35u, hardware.LastHighValue, "An unreadable alternative uses a bounded percentage range");
        var reads = hardware.Reads;
        hardware.NotifyDisplayPower(false);
        Equal<int?>(null, hardware.GetBrightness(raw), "Sleeping displays must not be read as a fake brightness value");
        Equal(false, hardware.SetBrightness(raw, 50), "Sleeping displays must not accept accidental brightness writes");
        Equal(reads, hardware.Reads, "Display sleep skips slow native brightness queries");
        hardware.NotifyDisplayPower(true);
        Equal<int?>(50, hardware.GetBrightness(high), "Wake invalidates the cached transport and probes the display again");
        Equal<bool?>(false, DisplayPowerNotifications.ParseState(DisplayPowerNotifications.DisplayState, 4, 0), "Display-off notification is parsed");
        Equal<bool?>(true, DisplayPowerNotifications.ParseState(DisplayPowerNotifications.DisplayState, 4, 1), "Display-on notification is parsed");
        Equal<bool?>(true, DisplayPowerNotifications.ParseState(DisplayPowerNotifications.DisplayState, 4, 2), "A dimmed display remains awake");
        Equal<bool?>(null, DisplayPowerNotifications.ParseState(Guid.NewGuid(), 4, 1), "Unrelated power settings are ignored");
        Equal<bool?>(null, DisplayPowerNotifications.ParseState(DisplayPowerNotifications.DisplayState, 0, 1), "Incorrect power-notification data lengths are ignored");
    }

    private sealed class NativeFixture : MonitorBrightnessController
    {
        public NativeFixture() : base(LogManager.GetLogger("NativeBrightnessFixture")) { }
        public int Reads, HighWrites, Acquired, Released, MaximumConcurrentWrites;
        public uint LastRawValue, LastHighValue;
        public bool HighWriteFails, RawWriteFails;
        private int _concurrentWrites;
        protected override IntPtr GetMonitorHandle(Display display) => new(display.DevicePath == "raw" ? 1 : 2);
        protected override PhysicalMonitor[] GetPhysicalMonitors(IntPtr handle)
        {
            Acquired++;
            return new[] { new PhysicalMonitor { Handle = handle, Description = "Fixture" } };
        }
        protected override void ReleasePhysicalMonitors(PhysicalMonitor[] monitors) => Released += monitors.Length;
        protected override bool ReadHighLevel(IntPtr handle, out uint min, out uint current, out uint max)
        {
            Reads++;
            min = 20; current = 120; max = 220;
            return handle.ToInt32() == 2;
        }
        protected override bool ReadVcp(IntPtr handle, out uint current, out uint max)
        { Reads++; current = 128; max = 255; return true; }
        protected override bool WriteHighLevel(IntPtr handle, uint value)
        { HighWrites++; LastHighValue = value; return !HighWriteFails; }
        protected override bool WriteVcp(IntPtr handle, uint value)
        {
            var active = Interlocked.Increment(ref _concurrentWrites);
            MaximumConcurrentWrites = Math.Max(MaximumConcurrentWrites, active);
            Thread.Sleep(20);
            LastRawValue = value;
            Interlocked.Decrement(ref _concurrentWrites);
            return !RawWriteFails;
        }
    }

    private static int Normalize(uint current, uint maximum) =>
        Invoke<int>("NormalizeBrightness", new[] { typeof(uint), typeof(uint) }, current, maximum);

    private static int Normalize(uint minimum, uint current, uint maximum) =>
        Invoke<int>("NormalizeBrightness", new[] { typeof(uint), typeof(uint), typeof(uint) }, minimum, current, maximum);

    private static uint Scale(int brightness, uint maximum) =>
        Invoke<uint>("ScaleBrightness", new[] { typeof(int), typeof(uint) }, brightness, maximum);

    private static uint Scale(int brightness, uint minimum, uint maximum) =>
        Invoke<uint>("ScaleBrightness", new[] { typeof(int), typeof(uint), typeof(uint) }, brightness, minimum, maximum);

    private static T Invoke<T>(string name, Type[] parameterTypes, params object[] arguments)
    {
        var method = typeof(MonitorBrightnessController).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic,
            null, parameterTypes, null) ?? throw new InvalidOperationException("Missing brightness conversion: " + name);
        return (T)method.Invoke(null, arguments)!;
    }

    private static void Equal<T>(T expected, T actual, string reason)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{reason}: expected {expected}, got {actual}.");
        _checkCount++;
    }

    private sealed class FakeDisplay : Display
    {
        public FakeDisplay(string path) : base(new FakeDisplayDevice(path)) { }
        public override bool IsAvailable => false;
    }

    private sealed class FakeDisplayDevice : DisplayDevice
    {
        public FakeDisplayDevice(string path) : base(path, "Regression display", "Regression key") { }
    }
}
