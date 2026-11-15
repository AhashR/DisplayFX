namespace DisplayFX.Objects.Entities;

public class ProfileSetting
{
    public double RedGain { get; set; } = 1;
    public double GreenGain { get; set; } = 1;
    public double BlueGain { get; set; } = 1;
    public int TemperatureKelvin { get; set; } = 6500;
    public ProfileSetting Copy() => new(Brightness, Contrast, Gamma, DigitalVibrance)
    { RedGain = RedGain, GreenGain = GreenGain, BlueGain = BlueGain, TemperatureKelvin = TemperatureKelvin };
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
