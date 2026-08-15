namespace DisplayFX.Objects.Entities;

public class ProfileSetting
{
    public ProfileSetting(double brightness, double contrast, double gamma,
        double digitalVibrance, bool useHardwareBrightness = false, int hardwareBrightness = 100)
    {
        Brightness = brightness;
        Contrast = contrast;
        Gamma = gamma;
        DigitalVibrance = digitalVibrance;
        UseHardwareBrightness = useHardwareBrightness;
        HardwareBrightness = hardwareBrightness;
    }

    public double Brightness { get; set; }
    public double Contrast { get; set; }
    public double Gamma { get; set; }
    public double DigitalVibrance { get; set; }

    /// <summary>
    ///     When true, applying the profile also sets the monitor's physical
    ///     backlight brightness (DDC/CI), like Twinkle Tray.
    /// </summary>
    public bool UseHardwareBrightness { get; set; }

    /// <summary>
    ///     The target backlight brightness in percent (0-100).
    /// </summary>
    public int HardwareBrightness { get; set; }
}