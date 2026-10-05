using System;
using System.Reflection;
using System.Runtime.InteropServices;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories;
using WindowsDisplayAPI;
using Monitor = DisplayFX.Objects.Entities.Monitor;

internal static class LibraryRegressionChecks
{
    internal static int Run()
    {
        var library = typeof(Display).Assembly;
        var modeType = library.GetType("WindowsDisplayAPI.Native.DeviceContext.Structures.DeviceMode", true)!;
        Check(Marshal.SizeOf(modeType) == 156, "DEVMODEA must include all 156 native bytes.");
        Check(Marshal.OffsetOf(modeType, "FormName").ToInt32() == 70,
            "DEVMODEA form name must begin at byte 70 without overlapping dmLogPixels.");
        Check(Marshal.OffsetOf(modeType, "LogicalInchPixels").ToInt32() == 102,
            "DEVMODEA dmLogPixels offset must match Windows.");
        Check(Marshal.OffsetOf(modeType, "BitsPerPixel").ToInt32() == 104,
            "DEVMODEA dmBitsPerPel offset must match Windows.");
        Check(Marshal.OffsetOf(modeType, "PanningHeight").ToInt32() == 152,
            "DEVMODEA trailing fields must match Windows.");

        var fieldsType = library.GetType("WindowsDisplayAPI.Native.DeviceContext.DeviceModeFields", true)!;
        var mode = Activator.CreateInstance(modeType, Enum.ToObject(fieldsType, 0))!;
        Check((ushort)modeType.GetField("Size")!.GetValue(mode)! == 156,
            "Constructed display modes must advertise the full native size.");

        var nativeMode = Marshal.AllocHGlobal(156);
        try
        {
            var bytes = new byte[156];
            bytes[70] = (byte)'F';
            bytes[71] = (byte)'X';
            BitConverter.GetBytes((ushort)144).CopyTo(bytes, 102);
            BitConverter.GetBytes((uint)32).CopyTo(bytes, 104);
            BitConverter.GetBytes((uint)3840).CopyTo(bytes, 108);
            BitConverter.GetBytes((uint)2160).CopyTo(bytes, 112);
            BitConverter.GetBytes((uint)144).CopyTo(bytes, 120);
            Marshal.Copy(bytes, 0, nativeMode, bytes.Length);
            var readMode = Marshal.PtrToStructure(nativeMode, modeType)!;
            Check((string)modeType.GetField("FormName", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(readMode)! == "FX", "Native form name must marshal without an offset shift.");
            Check((ushort)modeType.GetField("LogicalInchPixels")!.GetValue(readMode)! == 144,
                "Native logical DPI must marshal without string overlap.");
            Check((uint)modeType.GetField("PixelsWidth")!.GetValue(readMode)! == 3840 &&
                  (uint)modeType.GetField("PixelsHeight")!.GetValue(readMode)! == 2160 &&
                  (uint)modeType.GetField("DisplayFrequency")!.GetValue(readMode)! == 144,
                "Native resolution and frequency must round-trip correctly.");
        }
        finally
        {
            Marshal.FreeHGlobal(nativeMode);
        }

        var apiType = library.GetType("WindowsDisplayAPI.Native.DeviceContextApi", true)!;
        var rectangleParameter = apiType.GetMethod("MonitorFromRect", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetParameters()[0];
        Check(rectangleParameter.ParameterType.IsByRef,
            "MonitorFromRect must receive a pointer to RECT rather than a struct value.");

        var checks = 10 + MonitorMergeChecks();
        Console.WriteLine($"Passed {checks} display-library and monitor configuration regression checks.");
        return checks;
    }

    private static int MonitorMergeChecks()
    {
        static Monitor CreateMonitor(string path)
        {
            var monitor = new Monitor(path, path, new System.Drawing.Size(1920, 1080), 60);
            monitor.Profiles.Add(new Profile(monitor, "Default", new ProfileSetting(0.5, 0.5, 1, 0.5), true, true));
            return monitor;
        }

        var existing = CreateMonitor("saved-display");
        existing.LastBrightness = 37;
        existing.Profiles.Add(new Profile(existing, "My saved profile", new ProfileSetting(0.6, 0.4, 1.2, 0.5), false));
        var disconnected = CreateMonitor("disconnected-display");
        var saved = new Computer
        {
            IsStartWithWindows = true,
            IsApplySettingsOnStart = true,
            IsRememberBrightnessOnStart = true,
            IsStartMinimized = true,
            IsMinimizeToTray = true
        };
        saved.Monitors.Add(existing);
        saved.Monitors.Add(disconnected);

        var newMonitor = CreateMonitor("new-display");
        var detected = new Computer();
        detected.Monitors.Add(CreateMonitor("SAVED-DISPLAY"));
        detected.Monitors.Add(newMonitor);
        detected.Monitors.Add(CreateMonitor("NEW-DISPLAY"));

        var merge = typeof(ComputerFactory).GetMethod("MergeConnectedMonitors", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check((bool)merge.Invoke(null, new object[] { saved, detected })!,
            "A newly connected monitor must report a configuration change.");
        Check(saved.Monitors.Count == 3 && ReferenceEquals(saved.Monitors[2], newMonitor),
            "Only new monitor paths may be added, including duplicate paths in a detection result.");
        Check(ReferenceEquals(saved.Monitors[0], existing) && existing.Profiles.Count == 2 && existing.LastBrightness == 37,
            "Existing monitor profiles and remembered brightness must survive a case-insensitive match.");
        Check(ReferenceEquals(saved.Monitors[1], disconnected),
            "Disconnected monitor configuration must be retained for reconnection.");
        Check(saved.IsStartWithWindows && saved.IsApplySettingsOnStart && saved.IsRememberBrightnessOnStart &&
              saved.IsStartMinimized && saved.IsMinimizeToTray,
            "Detecting new hardware must preserve all existing application options.");
        Check(ReferenceEquals(newMonitor.Profiles[0].Monitor, newMonitor),
            "New monitor profiles must retain their parent references.");
        Check(!(bool)merge.Invoke(null, new object[] { saved, detected })! && saved.Monitors.Count == 3,
            "Merging the same detected monitors again must not add duplicates or request another save.");
        return 7;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
