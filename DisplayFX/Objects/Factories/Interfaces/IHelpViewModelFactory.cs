using DisplayFX.Interface.Help;

namespace DisplayFX.Objects.Factories.Interfaces;

public interface IHelpViewModelFactory : IFactory
{
    HelpViewModel Create();
}