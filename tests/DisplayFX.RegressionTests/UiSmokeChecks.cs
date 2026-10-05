using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Caliburn.Micro;
using DisplayFX.Global.Controllers;
using DisplayFX.Interface.Monitors;
using DisplayFX.Interface.Profiles;
using DisplayFX.Interface.Settings;
using DisplayFX.Interface.Shell;
using DisplayFX.Interface.BrightnessFlyout;
using DisplayFX.Objects.Entities;
using DisplayFX.Objects.Factories;
using Monitor = DisplayFX.Objects.Entities.Monitor;

/// <summary>Renders detached views with fixture data. Never initializes Bootstrapper or shows a window.</summary>
internal static class UiSmokeChecks
{
    public static int Run(string? outputDirectory)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var uri in new[]
        {
            "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Themes/dark.blue.xaml",
            "pack://application:,,,/DisplayFX;component/Resources/ResourceDictionaries/Resources.xaml"
        }) app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri) });
        AssemblySource.Instance.Add(typeof(ShellViewModel).Assembly);
        var platform = PlatformProvider.Current;
        var getViews = IoC.GetAllInstances;
        var getInstance = IoC.GetInstance;
        var buildUp = IoC.BuildUp;
        IoC.GetAllInstances = type => new[] { Activator.CreateInstance(type)! };
        IoC.GetInstance = (type, _) => Activator.CreateInstance(type)!;
        IoC.BuildUp = _ => { };
        PlatformProvider.Current = new DefaultPlatformProvider();
        var count = 0;
        var temporary = Path.Combine(AppContext.BaseDirectory, "ui-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            var computer = new Computer { IsMinimizeToTray = true, IsRememberBrightnessOnStart = true,
                BrightnessSchedules = new() { new() { Time = "08:00", Brightness = 80 }, new() { Time = "20:30", Brightness = 30 } } };
            var monitor = new Monitor("fixture", "Fixture monitor", new System.Drawing.Size(2560, 1440), 144) { CustomName = "Desk monitor" };
            var profile = new Profile(monitor, "Default", new ProfileSetting(0.5, 0.5, 1, 0.5), true, true);
            monitor.Profiles.Add(profile);
            computer.Monitors.Add(monitor);
            var settingsModel = new SettingsViewModel(computer, new RegistryController(), new DataController(temporary, temporary + ".json"));
            var settingsView = new SettingsView { DataContext = settingsModel };
            count += Render((FrameworkElement)settingsView.Content, 698, outputDirectory, "settings");
            if (settingsView.WindowStyle != WindowStyle.SingleBorderWindow || Descendants<ScrollViewer>((FrameworkElement)settingsView.Content).Any(scroll => scroll.Content is Panel))
                throw new InvalidOperationException("Settings must be a draggable window with all options visible without scrolling.");
            if (((FrameworkElement)settingsView.Content).ActualHeight > 550)
                throw new InvalidOperationException("Settings must fit within a compact screen height.");
            count++;
            for (var time = 0; time < 12; time++) settingsModel.BrightnessSchedules.Add(new() { Time = $"{time:00}:15", Brightness = 40 });
            count += Render((FrameworkElement)settingsView.Content, 698, outputDirectory, "settings-many-times");
            if (settingsModel.VisibleSchedules.Count() != SettingsViewModel.SchedulePageSize || ((FrameworkElement)settingsView.Content).ActualHeight > 550)
                throw new InvalidOperationException("Many daily times must remain accessible without growing or scrolling the settings window.");
            count++;

            var monitorModel = new MonitorViewModel(monitor, null!);
            var profileModel = new ProfileViewModel(profile, monitorModel, new ProfileSettingViewModelFactory(new EventAggregator()));
            monitorModel.Profiles.Add(profileModel);
            monitorModel.IsSelected = true;
            profileModel.IsSelected = true;
            var shellModel = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
            void Set(string field, object value) => typeof(ShellViewModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shellModel, value);
            Set("_computer", computer);
            Set("_displayName", $"DisplayFX {DisplayFX.Global.AppVersion.Current}");
            Set("_monitors", new ObservableCollection<MonitorViewModel> { monitorModel });
            Set("_selectedMonitor", monitorModel);
            Set("_selectedProfile", profileModel);
            var logger = NLog.LogManager.GetLogger("UiSmoke");
            var hardware = new MonitorBrightnessController(logger);
            var cache = new DisplayCache();
            Set("_brightnessAutomationController", new BrightnessAutomationController(cache, hardware,
                new BrightnessPersistenceController(new DataController(temporary, temporary + ".json"), cache, hardware, logger), logger));
            Set("_operationStatus", "");

            // Load the shell markup without ShellView's tray and live-app constructor.
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (repo != null && !File.Exists(Path.Combine(repo.FullName, "DisplayFX.sln"))) repo = repo.Parent;
            if (repo == null) throw new InvalidOperationException("Cannot find shell markup for UI verification.");
            var document = XDocument.Load(Path.Combine(repo.FullName, "DisplayFX", "Interface", "Shell", "ShellView.xaml"));
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            document.Root!.Attribute(xaml + "Class")!.Remove();
            foreach (var element in document.Descendants().Where(element => element.Name.NamespaceName.StartsWith("clr-namespace:") &&
                         !element.Name.NamespaceName.Contains(";assembly=")))
                element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=DisplayFX");
            foreach (var attribute in document.Root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration &&
                         attribute.Value.StartsWith("clr-namespace:") && !attribute.Value.Contains(";assembly=")))
                attribute.Value += ";assembly=DisplayFX";
            foreach (var attribute in document.Descendants().Attributes("Click").ToList()) attribute.Remove();
            var shell = (Window)XamlReader.Parse(document.ToString());
            shell.DataContext = shellModel;
            ((Grid)shell.Content).Background = app.Resources["WindowBackgroundBrush"] as Brush;
            count += Render((FrameworkElement)shell.Content, 600, outputDirectory, "main-window");
            var colourSlider = Descendants<Slider>((FrameworkElement)shell.Content).First();
            Drag(colourSlider);
            if (!profileModel.ProfileSettings!.IsDirty || profile.ProfileSetting.Brightness != 0.5)
                throw new InvalidOperationException("Dragging the colour slider must update only the editable draft.");
            count++;
            count += WindowMovementChecks.Run(shell, settingsView);
            shell.Close();

            var flyoutDocument = XDocument.Load(Path.Combine(repo.FullName, "DisplayFX", "Interface", "BrightnessFlyout", "BrightnessFlyoutView.xaml"));
            flyoutDocument.Root!.Attribute(xaml + "Class")!.Remove();
            foreach (var attribute in flyoutDocument.Descendants().Attributes("Click").ToList()) attribute.Remove();
            var flyout = (Window)XamlReader.Parse(flyoutDocument.ToString());
            var fixtureHardware = new FixtureBrightness();
            using var firstRow = new BrightnessMonitorViewModel(new FixtureDisplay("first"), fixtureHardware, name: "MSI MAG251RX");
            using var secondRow = new BrightnessMonitorViewModel(new FixtureDisplay("second"), fixtureHardware, name: "DELL 2009W");
            flyout.DataContext = new FlyoutFixture { Monitors = new[] { firstRow, secondRow } };
            count += Render((FrameworkElement)flyout.Content, 320, outputDirectory, "brightness-flyout");
            Drag(Descendants<Slider>((FrameworkElement)flyout.Content).First());
            BrightnessFeatureRegressionChecks.Pump(() => firstRow.FlushPendingBrightnessAsync());
            if (fixtureHardware.LastWrite != firstRow.Brightness || firstRow.Brightness <= 35)
                throw new InvalidOperationException("The new slider template must write the dragged brightness value.");
            count++;
            flyout.Close();
            var closingModel = (BrightnessFlyoutViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BrightnessFlyoutViewModel));
            typeof(BrightnessFlyoutViewModel).GetField("<Monitors>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(closingModel, new ObservableCollection<BrightnessMonitorViewModel> { firstRow, secondRow });
            var closingView = new BrightnessFlyoutView { DataContext = closingModel };
            firstRow.Brightness = 90;
            BrightnessFeatureRegressionChecks.Pump(async () =>
            {
                closingView.Close();
                await closingView.CloseCompletion;
            });
            if (fixtureHardware.LastWrite != 90) throw new InvalidOperationException("Closing the actual flyout must flush its pending value before disposal.");
            count++;
        }
        finally
        {
            PlatformProvider.Current = platform;
            IoC.GetAllInstances = getViews;
            IoC.GetInstance = getInstance;
            IoC.BuildUp = buildUp;
            app.Shutdown();
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
        return count;
    }

    private static int Render(FrameworkElement view, double width, string? directory, string name)
    {
        // Caliburn creates nested profile/monitor views during the first layout pass.
        for (var pass = 0; pass < 3; pass++)
        {
            view.Measure(new Size(width, double.PositiveInfinity));
            view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height));
            view.UpdateLayout();
        }
        if (view.ActualHeight <= 0 || view.ActualHeight > 1000 || view.ActualWidth > width + 1)
            throw new InvalidOperationException($"Unexpected layout bounds for {name}: {view.ActualWidth} x {view.ActualHeight}.");
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(view.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, name + ".png"));
            encoder.Save(stream);
        }
        return 1;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Drag(Slider slider)
    {
        var thumb = Descendants<Thumb>(slider).First();
        thumb.RaiseEvent(new DragDeltaEventArgs(40, 0) { RoutedEvent = Thumb.DragDeltaEvent });
    }

    private sealed class FlyoutFixture
    {
        public BrightnessMonitorViewModel[] Monitors { get; set; } = Array.Empty<BrightnessMonitorViewModel>();
        public bool IsLinked { get; set; }
        public string LinkStatus => "";
    }

    private sealed class FixtureBrightness : MonitorBrightnessController
    {
        public FixtureBrightness() : base(NLog.LogManager.GetLogger("UiFixture")) { }
        public override int? GetBrightness(WindowsDisplayAPI.Display display) => display.DevicePath == "first" ? 35 : 70;
        public override bool SupportsBrightness(WindowsDisplayAPI.Display display) => true;
        public int LastWrite;
        public override bool SetBrightness(WindowsDisplayAPI.Display display, int brightness)
        { LastWrite = brightness; return true; }
    }
    private sealed class FixtureDisplay : WindowsDisplayAPI.Display
    {
        public FixtureDisplay(string path) : base(new FixtureDevice(path)) { }
    }
    private sealed class FixtureDevice : WindowsDisplayAPI.DisplayDevice
    {
        public FixtureDevice(string path) : base(path, "Fixture", "Fixture") { }
    }
}
