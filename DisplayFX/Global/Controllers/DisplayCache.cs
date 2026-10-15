using System.Collections.Generic;
using System.Linq;
using WindowsDisplayAPI;

namespace DisplayFX.Global.Controllers;

public class DisplayCache
{
    private IReadOnlyList<Display>? _cachedDisplays;

    public virtual IReadOnlyList<Display> GetDisplays()
    {
        return _cachedDisplays ??= Display.GetDisplays().ToList();
    }

    public virtual void Refresh()
    {
        _cachedDisplays = Display.GetDisplays().ToList();
    }
}
