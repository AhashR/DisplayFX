namespace DisplayFX.Objects.Entities;

public class ProfileSetting
{
    public ProfileSetting Copy() => new(Brightness, Contrast, Gamma, DigitalVibrance);
    public ProfileSetting(double brightness, double contrast, double gamma, double digitalVibrance)
    {
        Brightness = brightness;
        Contrast = contrast;
        Gamma = gamma;
        DigitalVibrance = digitalVibrance;
    }

    public double Brightness { get; set; }
    public double Contrast { get; set; }
    public double Gamma { get; set; }
    public double DigitalVibrance { get; set; }
}
