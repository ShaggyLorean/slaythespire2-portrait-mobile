using System;
using Godot;

namespace Sts2Portrait;

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

                var cls = key[..dot];
                var field = (Type.GetType("Sts2Portrait.Patches." + cls)
                             ?? Type.GetType("Sts2Portrait." + cls))?.GetField(key[(dot + 1)..]);
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
