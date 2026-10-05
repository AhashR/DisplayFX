namespace DisplayFX.Global;

public static class AppVersion
{
    public static string Current => typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "Unknown";
}
