using System;
using System.Linq;
using System.Text;

namespace DisplayFX.Global.Controllers;

public static class MonitorIdentityController
{
    public static string? FromEdid(byte[]? edid)
    {
        if (edid == null || edid.Length < 128 || !edid.Take(8).SequenceEqual(new byte[] { 0, 255, 255, 255, 255, 255, 255, 0 }) ||
            edid.Take(128).Sum(value => (int)value) % 256 != 0) return null;
        var serial = BitConverter.ToUInt32(edid, 12);
        var identity = Convert.ToHexString(edid, 8, 4);
        if (serial != 0 && serial != uint.MaxValue) return identity + ":" + serial.ToString("X8");
        for (var offset = 54; offset + 18 <= 126; offset += 18)
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 2] == 0 && edid[offset + 3] == 255)
            {
                var text = Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\n', '\r', ' ');
                if (text.Length > 0 && !text.Any(char.IsControl) && text.Any(character => character != '0')) return identity + ":" + text.ToUpperInvariant();
            }
        return null; // Model numbers alone cannot distinguish two identical panels.
    }
}
