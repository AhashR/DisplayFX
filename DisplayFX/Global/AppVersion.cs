namespace DisplayFX.Global;

public static class AppVersion
{
    public static string Current => System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AppVersion).Assembly)?.InformationalVersion.Split('+')[0]
        ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "Unknown";
}
