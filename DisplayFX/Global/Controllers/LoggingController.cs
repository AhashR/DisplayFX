using System;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace DisplayFX.Global.Controllers;

public static class LoggingController
{
    public static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayFX", "Logs");

    public static void Configure(string? directory = null)
    {
        var configuration = new LoggingConfiguration();
        var file = new FileTarget("diagnostics")
        {
            FileName = Path.Combine(directory ?? LogDirectory, "DisplayFX.log"),
            Layout = "${longdate}|${level:uppercase=true}|${message} ${exception:format=tostring}",
            ArchiveAboveSize = 2 * 1024 * 1024,
            MaxArchiveFiles = 5,
            KeepFileOpen = false
        };
        configuration.AddRule(LogLevel.Info, LogLevel.Fatal, file);
        LogManager.Configuration = configuration;
    }
}
