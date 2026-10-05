namespace DisplayFX.Global.Controllers;

public sealed record BrightnessStatus(string Message, bool IsError, int? Brightness = null);
