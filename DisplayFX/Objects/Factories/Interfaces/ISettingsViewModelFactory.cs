using System;
using System.Threading.Tasks;
using System.Windows;
using DisplayFX.Interface.Settings;
using DisplayFX.Objects.Entities;

namespace DisplayFX.Objects.Factories.Interfaces;

public interface ISettingsViewModelFactory : IFactory
{
    SettingsViewModel Create(Computer computer, Func<Window?, string?> import, Func<Window?, string?> export, Func<Task<string>> checkUpdates, Func<bool> canImport, Func<bool> canExport, Func<string> profileFileContext);
}
