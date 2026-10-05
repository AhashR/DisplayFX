using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DisplayFX.Bootstrap;
using DisplayFX.Interface.Help;
using DisplayFX.Objects.Entities;

internal static class RestartRegressionChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            count++;
        }

        Check(ParseParent(new[] { "--restart", "123" }, 456) == 123, "A valid restart identifies its original process.");
        Check(ParseParent(Array.Empty<string>(), 456) == null, "Ordinary launches do not wait for a parent.");
        Check(ParseParent(new[] { "--restart", "456" }, 456) == null, "A restart cannot wait for its own process.");
        Check(ParseParent(new[] { "--restart", "-1" }, 456) == null, "Negative parent identifiers are rejected.");
        Check(ParseParent(new[] { "--restart", "0" }, 456) == null, "Zero is not a restart parent.");
        Check(ParseParent(new[] { "--restart", "invalid" }, 456) == null, "Malformed restart identifiers are rejected.");
        Check(ParseParent(new[] { "--restart", "2147483648" }, 456) == null, "Overflowing restart identifiers are rejected.");
        Check(ParseParent(new[] { "--restart", "123", "unexpected" }, 456) == null, "Extra restart arguments are rejected.");
        Check(ParseParent(new[] { "--unknown", "123" }, 456) == null, "Unknown flags do not trigger restart waits.");

        var executable = @"C:\Program Files\DisplayFX\DisplayFX.exe";
        var assembly = @"C:\Program Files\DisplayFX\DisplayFX.dll";
        var direct = CreateCommand(executable, assembly, 123);
        Check(direct.FileName == executable, "The restart uses the actual running executable.");
        Check(!direct.UseShellExecute && direct.CreateNoWindow, "The restart does not open a helper console window.");
        Check(direct.ArgumentList.Count == 2 && direct.ArgumentList[0] == "--restart" && direct.ArgumentList[1] == "123",
            "The executable restart passes the parent identifier without shell quoting.");
        var hosted = CreateCommand(@"C:\Program Files\dotnet\dotnet.exe", assembly, 123);
        Check(hosted.ArgumentList.Count == 3 && hosted.ArgumentList[0] == assembly && hosted.ArgumentList[1] == "--restart" &&
              hosted.ArgumentList[2] == "123", "A dotnet-hosted restart retains the entry assembly path.");

        var appProcesses = new HashSet<int> { 123 };
        Check(IsWindow(123, "Adjust Displays - (GPU)", appProcesses), "The existing app's main window is eligible for activation.");
        Check(IsWindow(123, "DisplayFX", appProcesses), "The existing app's fallback title is eligible for activation.");
        Check(!IsWindow(999, "DisplayFX - Browser tab", appProcesses), "A browser title cannot identify another app's window.");
        Check(!IsWindow(123, "Browser tab containing DisplayFX", appProcesses), "A title mention is insufficient for activation.");
        Check(!IsWindow(uint.MaxValue, "DisplayFX", appProcesses), "Invalid unsigned identifiers cannot match signed process IDs.");

        Check(MatchesIdentity("DisplayFX", 1, @"D:\Installed\DisplayFX.exe", "DisplayFX", 1, executable, false),
            "Launching another app copy restores the existing instance in the same session.");
        Check(!MatchesIdentity("DisplayFX", 1, @"D:\Installed\DisplayFX.exe", "DisplayFX", 1, executable, true),
            "A restart can only wait for its original executable path.");
        Check(MatchesIdentity("DisplayFX", 1, executable.ToUpperInvariant(), "DisplayFX", 1, executable, true),
            "Restart executable paths are matched case-insensitively.");
        Check(!MatchesIdentity("DisplayFX", 2, executable, "DisplayFX", 1, executable, false),
            "Other Windows sessions cannot supply the existing app window.");
        Check(!MatchesIdentity("chrome", 1, executable, "DisplayFX", 1, executable, false),
            "Unrelated process names cannot supply the existing app window.");

        var original = new Computer { IsStartWithWindows = true };
        var defaults = new Computer();
        var events = new List<string>();
        var saved = original;
        var startupEnabled = true;
        var failure = Reset(original, defaults,
            computer => { saved = computer; events.Add(ReferenceEquals(computer, defaults) ? "write-defaults" : "write-original"); },
            enabled => { startupEnabled = enabled; events.Add("startup-" + enabled); },
            () => events.Add("restart"));
        Check(failure == null && ReferenceEquals(saved, defaults) && !startupEnabled,
            "A successful reset saves defaults and removes automatic startup before restarting.");
        Check(string.Join(",", events) == "write-defaults,startup-False,restart", "A reset launches only after settings and registry succeed.");

        events.Clear();
        saved = original;
        startupEnabled = true;
        failure = Reset(original, defaults,
            computer => { saved = computer; events.Add(ReferenceEquals(computer, defaults) ? "write-defaults" : "write-original"); },
            enabled => { events.Add("startup-" + enabled); if (!enabled) throw new InvalidOperationException("Registry failed."); startupEnabled = enabled; },
            () => events.Add("restart"));
        Check(failure is InvalidOperationException && ReferenceEquals(saved, original) && startupEnabled,
            "A registry failure restores the original saved settings and startup preference.");
        Check(string.Join(",", events) == "write-defaults,startup-False,write-original,startup-True",
            "A failed registry change cannot launch the replacement process.");

        events.Clear();
        failure = Reset(original, defaults,
            _ => { events.Add("write-defaults"); throw new InvalidOperationException("Write failed."); },
            _ => events.Add("registry"), () => events.Add("restart"));
        Check(failure is InvalidOperationException && string.Join(",", events) == "write-defaults",
            "A failed settings write does not touch startup registration or restart.");

        saved = original;
        startupEnabled = true;
        failure = Reset(original, defaults, computer => saved = computer, enabled => startupEnabled = enabled,
            () => throw new InvalidOperationException("Launch failed."));
        Check(failure is InvalidOperationException && ReferenceEquals(saved, original) && startupEnabled,
            "A launch failure restores the original settings and startup registration.");

        var registryRollbackAttempted = false;
        failure = Reset(original, defaults,
            computer => { if (ReferenceEquals(computer, original)) throw new InvalidOperationException("Restore failed."); },
            enabled => { if (enabled) registryRollbackAttempted = true; throw new InvalidOperationException("Registry failed."); },
            () => throw new InvalidOperationException("Must not restart."));
        Check(failure is AggregateException aggregate && aggregate.InnerExceptions.Count == 3 && registryRollbackAttempted,
            "Rollback failures remain visible and every rollback is attempted independently.");

        return count;
    }

    private static int? ParseParent(string[] arguments, int currentProcessId) =>
        (int?)Invoke(typeof(Bootstrapper), "GetRestartParentProcessId", arguments, currentProcessId);

    private static ProcessStartInfo CreateCommand(string executable, string assembly, int parentProcessId) =>
        (ProcessStartInfo)Invoke(typeof(Bootstrapper), "CreateRestartStartInfo", executable, assembly, parentProcessId)!;

    private static bool IsWindow(uint processId, string title, ISet<int> appProcesses) =>
        (bool)Invoke(typeof(Bootstrapper), "IsApplicationWindow", processId, title, appProcesses)!;

    private static bool MatchesIdentity(string candidateName, int candidateSession, string candidatePath,
        string currentName, int currentSession, string currentPath, bool requireSamePath) =>
        (bool)Invoke(typeof(Bootstrapper), "MatchesApplicationIdentity", candidateName, candidateSession, candidatePath,
            currentName, currentSession, currentPath, requireSamePath)!;

    private static Exception? Reset(Computer original, Computer defaults, Action<Computer> write, Action<bool> startup, Action restart)
    {
        try
        {
            Invoke(typeof(HelpViewModel), "ResetAndRestart", original, defaults, write, startup, restart);
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException;
        }
    }

    private static object? Invoke(Type type, string name, params object[] arguments)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing restart helper: " + name);
        return method.Invoke(null, arguments);
    }
}
