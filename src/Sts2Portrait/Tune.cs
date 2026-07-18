using System;
using Godot;

namespace Sts2Portrait;

/// <summary>
/// Canlı ayar: user://bridge/tune.cfg içindeki "Sınıf.Alan=değer" satırlarını
/// (ör. "ShopLayout.CellH=530") Sts2Portrait.Patches altındaki statik layout
/// alanlarına yansıtır. Dosya yoksa no-op — release'te dosya olmaz.
/// Görsel iterasyonu rebuild'siz yapabilmek için.
/// </summary>
public static class Tune
{
    private static DateTime _stamp;

    public static void Reload()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath("user://bridge/tune.cfg");
            if (!System.IO.File.Exists(path)) return;
            var mt = System.IO.File.GetLastWriteTimeUtc(path);
            if (mt == _stamp) return;
            _stamp = mt;

            foreach (var raw in System.IO.File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var eq = line.IndexOf('=');
                if (eq < 1) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                var dot = key.IndexOf('.');
                if (dot < 1) continue;

                var field = Type.GetType("Sts2Portrait.Patches." + key[..dot])?.GetField(key[(dot + 1)..]);
                if (field is null) { PortraitMod.Log($"tune: bilinmeyen alan {key}"); continue; }

                if (field.FieldType == typeof(float) &&
                    float.TryParse(val, System.Globalization.CultureInfo.InvariantCulture, out var f))
                    field.SetValue(null, f);
                else if (field.FieldType == typeof(int) && int.TryParse(val, out var i))
                    field.SetValue(null, i);
                else { PortraitMod.Log($"tune: parse edilemedi {key}={val}"); continue; }
                PortraitMod.Log($"tune: {key}={val}");
            }
        }
        catch (Exception e) { PortraitMod.Log("tune ERR: " + e.Message); }
    }
}
