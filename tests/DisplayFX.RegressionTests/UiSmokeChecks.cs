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
            var computer = new Computer { IsMinimizeToTray = true, IsRememberBrightnessOnStart = true };
            var monitor = new Monitor("fixture", "Fixture monitor", new System.Drawing.Size(2560, 1440), 144) { CustomName = "Desk monitor" };
            var profile = new Profile(monitor, "Default", new ProfileSetting(0.5, 0.5, 1, 0.5), true, true);
            monitor.Profiles.Add(profile);
            computer.Monitors.Add(monitor);
            var settingsData = new DataController(temporary, temporary + ".json");
            var importCount = 0;
            var exportCount = 0;
            var settingsModel = new SettingsViewModel(computer, new RegistryController(), settingsData,
                _ => { importCount++; return "Profile imported."; }, _ => { exportCount++; return "Profile exported."; },
                canImport: () => true, canExport: () => true, profileFileContext: () => "Desk monitor · Default");
            var settingsView = new SettingsView { DataContext = settingsModel };
            count += Render((FrameworkElement)settingsView.Content, 534, outputDirectory, "settings");
            var settingsContent = (FrameworkElement)settingsView.Content;
            var settingsScroll = (ScrollViewer)settingsView.FindName("SettingsScroll");
            if (!ReferenceEquals(settingsView.Content, settingsScroll) ||
                Descendants<Button>(settingsContent).Any(button => button.Content is string label &&
                    label is "General" or "Profile files" or "About"))
                throw new InvalidOperationException("Settings must use the full-width scroll area without sidebar navigation.");
            count++;
            var switches = Descendants<CheckBox>(settingsContent).Select(control => control.Content as string);
            if (settingsView.WindowStyle != WindowStyle.SingleBorderWindow ||
                !switches.SequenceEqual(new[] { "Launch with Windows", "Start minimised", "Close to tray" }) ||
                settingsView.FindName("InstalledVersion") is not TextBlock version || version.Text != settingsModel.VersionText ||
                settingsView.FindName("Done") != null)
                throw new InvalidOperationException("Settings must use native chrome and show only the retained preferences and the app version.");
            var brokenSettings = new SettingsViewModel(computer, new RegistryController(), settingsData,
                _ => throw new IOException("Unavailable"), checkUpdates: () => Task.FromException<string>(new IOException("Offline")));
            brokenSettings.ImportProfileCommand.Execute(null);
            if (!brokenSettings.SettingsStatus.Contains("Unavailable"))
                throw new InvalidOperationException("File dialog errors must be reported in Settings.");
            BrightnessFeatureRegressionChecks.Pump(brokenSettings.CheckForUpdates);
            if (!brokenSettings.SettingsStatus.Contains("Offline") || !brokenSettings.CanCheckForUpdates)
                throw new InvalidOperationException("A failed update check must allow retry.");
            count += 2;
            var import = (Button)settingsView.FindName("ImportProfile");
            var export = (Button)settingsView.FindName("ExportProfile");
            if (!Equals(import.Content, "Import profile") || !Equals(export.Content, "Export profile"))
                throw new InvalidOperationException("Profile files must live in Settings and have concise labels.");
            import.Command.Execute(null);
            export.Command.Execute(null);
            if (importCount != 1 || exportCount != 1 || settingsModel.SettingsStatus != "Profile exported.")
                throw new InvalidOperationException("Settings transfer buttons must invoke their actions and report the result.");
            settingsContent.Height = 130;
            count += Render(settingsContent, 600, null, "compact-settings");
            settingsContent.ClearValue(FrameworkElement.HeightProperty);
            if (settingsScroll.ExtentHeight <= settingsScroll.ViewportHeight ||
                settingsScroll.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
                throw new InvalidOperationException("Settings must scroll vertically when the window is constrained.");
            count += 4;

            var monitorModel = new MonitorViewModel(monitor, null!);
            var profileModel = new ProfileViewModel(profile, monitorModel, new ProfileSettingViewModelFactory(new EventAggregator()));
            monitorModel.Profiles.Add(profileModel);
            monitorModel.IsSelected = true;
            profileModel.IsSelected = true;
            var shellModel = (ShellViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ShellViewModel));
            void Set(string field, object value) => typeof(ShellViewModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shellModel, value);
            Set("_computer", computer);
            Set("_monitors", new ObservableCollection<MonitorViewModel> { monitorModel });
            Set("_selectedMonitor", monitorModel);
            Set("_selectedProfile", profileModel);
            Set("_operationStatus", "");

            var profileActions = profileModel.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header as string).ToArray();
            if (!profileActions.SequenceEqual(new[] { "Choose icon", "Move up", "Move down", "Duplicate" }))
                throw new InvalidOperationException("Default profiles must offer only their retained context menu actions.");
            var custom = new Profile(monitor, "Game", new ProfileSetting(0.5, 0.5, 1, 0.5), false)
            {
                IsFavorite = true,
                HotkeyKey = System.Windows.Input.Key.F8
            };
            monitor.ReferenceProfileId = custom.Id;
            var customModel = new ProfileViewModel(custom, monitorModel, new ProfileSettingViewModelFactory(new EventAggregator()));
            if (!customModel.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header as string).SequenceEqual(
                    new[] { "Choose icon", "Remove existing profile shortcut", "Move up", "Move down", "Duplicate", "Rename...", "Remove" }))
                throw new InvalidOperationException("Legacy favorites and references must not reintroduce removed menu items.");
            monitor.ReferenceProfileId = null;
            count += 2;

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
            foreach (var attribute in document.Descendants().Attributes().Where(attribute => attribute.Name.LocalName is "PreviewMouseLeftButtonDown" or "PreviewMouseLeftButtonUp" or "LostMouseCapture" or "PreviewKeyDown" or "PreviewKeyUp").ToList()) attribute.Remove();
            var shell = (Window)XamlReader.Parse(document.ToString());
            shell.DataContext = shellModel;
            ((Grid)shell.Content).Background = app.Resources["WindowBackgroundBrush"] as Brush;
            count += Render((FrameworkElement)shell.Content, 600, outputDirectory, "main-window");
            var status = "NVIDIA Digital Vibrance is unavailable. Refresh displays after the driver update, or restart DisplayFX, then apply the profile again.";
            shellModel.IsNotifying = true;
            typeof(ShellViewModel).GetProperty("OperationStatus")!.SetValue(shellModel, status);
            count += Render((FrameworkElement)shell.Content, 600, outputDirectory, "main-window-status");
            var mainContent = (FrameworkElement)shell.Content;
            var rail = (Border)shell.FindName("NavigationRail");
            var editor = (StackPanel)shell.FindName("EditorPane");
            var monitorNavigation = (ItemsControl)shell.FindName("DisplayNavigation");
            var profileNavigation = (ItemsControl)shell.FindName("ProfileNavigation");
            if (rail == null || editor == null || monitorNavigation?.Items.Count != 1 || profileNavigation?.Items.Count != 1 ||
                Grid.GetColumn(rail) != 0 ||
                editor.TranslatePoint(new Point(), mainContent).X <= rail.ActualWidth ||
                ((TextBlock)shell.FindName("SelectedDisplayName")).Text != monitorModel.Name ||
                ((TextBlock)shell.FindName("SelectedProfileHeading")).Text != profileModel.Name)
                throw new InvalidOperationException("The left sidebar must contain displays and profiles, with the active context in the right pane.");
            count++;
            if (Descendants<TextBlock>(mainContent).Any(text => text.Text == status) ||
                !Descendants<TextBlock>(mainContent).Any(text => Equals(text.ToolTip, status) && text.Visibility == Visibility.Visible && text.ActualHeight > 0))
                throw new InvalidOperationException("The full status belongs in the status icon's tooltip, not in a separate line.");
            if (shell.FindName("RestoreOriginal") != null || shell.FindName("ImportProfile") != null || Descendants<Expander>(mainContent).Any() || Descendants<ProgressBar>(mainContent).Any() ||
                Descendants<Slider>(mainContent).Count() != 4 ||
                new[] { "Revert", "Apply", "Update" }.Any(name => shell.FindName(name) is not Button))
                throw new InvalidOperationException("The simplified editor must retain all four sliders and the three profile actions.");
            count += 2;
            var defaultEditor = Descendants<DisplayFX.Interface.ProfileSettings.ProfileSettingView>(mainContent).Single();
            if (Descendants<TextBlock>(defaultEditor).Any(t => t.Text == "Linked app" && t.ActualHeight > 0 && (t.Parent is DockPanel panel && panel.Parent is Grid linkGrid && linkGrid.Parent is Border card && card.Visibility == Visibility.Visible)) ||
                !Descendants<TextBlock>(defaultEditor).Any(t => t.Text == "Colour settings") ||
                !Descendants<TextBlock>(defaultEditor).Any(t => t.Text == "Brightness") || shell.Title != "DisplayFX")
                throw new InvalidOperationException("The default profile must hide linking, use British wording and keep the title unversioned.");
            var customEditor = new DisplayFX.Interface.ProfileSettings.ProfileSettingView { DataContext = customModel.ProfileSettings };
            count += Render(customEditor, 410, null, "custom-profile");
            if (!Descendants<TextBlock>(customEditor).Any(t => t.Text == "Linked app" && t.ActualHeight > 0 && (t.Parent is DockPanel panel && panel.Parent is Grid linkGrid && linkGrid.Parent is Border card && card.Visibility == Visibility.Visible)))
                throw new InvalidOperationException("Custom profiles must retain app linking.");
            count += 2;
            var colourSlider = Descendants<Slider>((FrameworkElement)shell.Content).First();
            Drag(colourSlider);
            if (!profileModel.ProfileSettings!.IsDirty || profile.ProfileSetting.Brightness != 0.5)
                throw new InvalidOperationException("Dragging the colour slider must update only the editable draft.");
            count++;
            count += WindowMovementChecks.Run(shell, settingsView);
            if (shell.FindName("OpenWorkspace") != null || shell.FindName("MainActions") != null ||
                Descendants<TextBox>(mainContent).Any(text => text.Name == "HotkeyRecorderButton"))
                throw new InvalidOperationException("The main editor must replace Workspace and omit the profile shortcut recorder.");
            count++;
            var channels = Descendants<TextBox>(mainContent).Select(text => text.Tag as string).Where(tag => tag != null).ToArray();
            if (!channels.SequenceEqual(new[] { "Brightness", "Contrast", "DigitalVibrance", "Gamma" }))
                throw new InvalidOperationException("Each displayed color value must have an inline editor.");
            count += 2;
            shell.Close();

            var corner = typeof(BrightnessFlyoutView).GetMethod("GetCornerPosition", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var (area, size, gap, expected) in new[]
            {
                (new System.Drawing.Rectangle(0, 0, 1920, 1040), new System.Drawing.Size(320, 232), 12, new System.Drawing.Point(1588, 796)),
                (new System.Drawing.Rectangle(-1280, -240, 1280, 984), new System.Drawing.Size(320, 232), 12, new System.Drawing.Point(-332, 500)),
                (new System.Drawing.Rectangle(1920, 48, 2560, 1392), new System.Drawing.Size(480, 348), 18, new System.Drawing.Point(3982, 1074)),
                (new System.Drawing.Rectangle(20, 40, 220, 200), new System.Drawing.Size(320, 232), 12, new System.Drawing.Point(20, 40))
            })
            {
                if (!Equals(corner.Invoke(null, new object[] { area, size, gap }), expected))
                    throw new InvalidOperationException("The brightness flyout must stay in the selected display's bottom-right work-area corner.");
                count++;
            }

            var flyoutDocument = XDocument.Load(Path.Combine(repo.FullName, "DisplayFX", "Interface", "BrightnessFlyout", "BrightnessFlyoutView.xaml"));
            flyoutDocument.Root!.Attribute(xaml + "Class")!.Remove();
            foreach (var attribute in flyoutDocument.Descendants().Attributes("Click").ToList()) attribute.Remove();
            var flyout = (Window)XamlReader.Parse(flyoutDocument.ToString());
            var fixtureHardware = new FixtureBrightness();
            using var firstRow = new BrightnessMonitorViewModel(new FixtureDisplay("first"), fixtureHardware, name: "MSI MAG251RX");
            using var secondRow = new BrightnessMonitorViewModel(new FixtureDisplay("second"), fixtureHardware, name: "DELL 2009W");
            flyout.DataContext = new FlyoutFixture { Monitors = new[] { firstRow, secondRow } };
            count += Render((FrameworkElement)flyout.Content, 320, outputDirectory, "brightness-flyout");
            var flyoutContent = (FrameworkElement)flyout.Content;
            var flyoutSliders = Descendants<Slider>(flyoutContent).ToArray();
            var footerButtons = Descendants<Button>(flyoutContent).ToArray();
            if (flyout.FindName("BrightnessHeading") != null || Descendants<TextBlock>(flyoutContent).Any(text => text.Text == "Brightness") ||
                flyoutSliders.Length != 2 || flyoutSliders.Any(slider => slider.ActualHeight < 26 || slider.ActualWidth < 240) ||
                footerButtons.Length != 1 || footerButtons[0].Name != "OpenDisplayFX" || flyout.FindName("LinkBrightness") != null ||
                flyoutContent.ActualHeight is < 124 or > 167)
                throw new InvalidOperationException($"The tray popup must omit its heading and keep just the Open DisplayFX action, with full-size sliders and a compact height. Actual height: {flyoutContent.ActualHeight}.");
            count++;
            Drag(Descendants<Slider>((FrameworkElement)flyout.Content).First());
            BrightnessFeatureRegressionChecks.Pump(() => firstRow.FlushPendingBrightnessAsync());
            if (fixtureHardware.LastWrite != firstRow.Brightness || firstRow.Brightness <= 35)
                throw new InvalidOperationException("The new slider template must write the dragged brightness value.");
            count++;
            flyout.Close();
            var closingModel = (BrightnessFlyoutViewModel)RuntimeHelpers.GetUninitializedObject(typeof(BrightnessFlyoutViewModel));
            typeof(BrightnessFlyoutViewModel).GetField("<Monitors>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(closingModel, new ObservableCollection<BrightnessMonitorViewModel> { firstRow, secondRow });
            typeof(BrightnessFlyoutViewModel).GetField("<Initialization>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(closingModel, Task.CompletedTask);
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
