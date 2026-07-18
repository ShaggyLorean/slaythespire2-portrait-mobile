using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Sts2Portrait.Diagnostics;

public static class Bridge
{
    private static bool _started;
    private static string _dir = "";
    private static string _cmdPath = "";
    private static double _accum;
    private static double _time;
    private const double PollInterval = 0.15;
    private static readonly List<(double at, Action act)> _pending = new();

    public static void Start(SceneTree tree)
    {
        if (_started) return;
        _started = true;
        _dir = ProjectSettings.GlobalizePath("user://bridge");
        _cmdPath = System.IO.Path.Combine(_dir, "cmd");
        try { System.IO.Directory.CreateDirectory(_dir); } catch { }
        tree.ProcessFrame += Tick;
        WriteDone("start", $"dir={_dir}");
        PortraitMod.Log($"Bridge started -> {_dir}");
    }

    private static void Tick()
    {
        _time += 0.016;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (_time >= _pending[i].at)
            {
                var act = _pending[i].act;
                _pending.RemoveAt(i);
                try { act(); } catch (Exception e) { PortraitMod.Log($"pending: {e.Message}"); }
            }
        }

        _accum += 0.016;
        if (_accum < PollInterval) return;
        _accum = 0;
        Poll();
    }

    private static void Poll()
    {
        try
        {
            if (!System.IO.File.Exists(_cmdPath)) return;
            var text = System.IO.File.ReadAllText(_cmdPath).Trim();
            System.IO.File.Delete(_cmdPath);
            foreach (var line in text.Split('\n'))
            {
                var c = line.Trim();
                if (c.Length > 0) Execute(c);
            }
        }
        catch (Exception e) { PortraitMod.Log($"poll: {e.Message}"); }
    }

    private static SceneTree Tree => (SceneTree)Engine.GetMainLoop();
    private static Window Root => Tree.Root;

    private static void Execute(string cmd)
    {
        var p = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var op = p[0].ToLowerInvariant();
        try
        {
            switch (op)
            {
                case "shot": Shot(p.Length > 1 ? p[1] : "shot"); break;
                case "dumproot": NodeInspector.DumpTree(Root, "BRIDGE ROOT"); WriteDone(cmd, "ok"); break;
                case "dumpscreen": DumpScreen(); break;
                case "click": Click(F(p, 1), F(p, 2)); WriteDone(cmd, "ok"); break;
                case "move": Move(F(p, 1), F(p, 2)); WriteDone(cmd, "ok"); break;
                case "drag": Drag(F(p, 1), F(p, 2), F(p, 3), F(p, 4)); WriteDone(cmd, "ok"); break;
                case "key": KeyPress(p[1]); WriteDone(cmd, "ok"); break;
                case "tap": Tap(F(p, 1), F(p, 2)); WriteDone(cmd, "ok"); break;
                case "tdrag": TouchDrag(F(p, 1), F(p, 2), F(p, 3), F(p, 4)); WriteDone(cmd, "ok"); break;
                case "warp": Input.WarpMouse(new Vector2(F(p, 1), F(p, 2))); WriteDone(cmd, "ok"); break;
                case "rdrag": RealDrag(F(p, 1), F(p, 2), F(p, 3), F(p, 4)); WriteDone(cmd, "ok"); break;
                case "dev": DevCmd(cmd.Substring(3).Trim()); break;
                default: WriteDone(cmd, "unknown"); break;
            }
        }
        catch (Exception e) { WriteDone(cmd, "ERR: " + e.Message); }
    }

    private static float F(string[] p, int i) => float.Parse(p[i], CultureInfo.InvariantCulture);

    private static void DevCmd(string command)
    {
        try
        {
            var console = new MegaCrit.Sts2.Core.DevConsole.DevConsole(true);
            var result = console.ProcessCommand(command);
            WriteDone("dev " + command, result.ToString());
            PortraitMod.Log($"dev cmd '{command}' -> {result}");
        }
        catch (Exception e) { WriteDone("dev " + command, "ERR: " + e.Message); }
    }

    private static void Shot(string name)
    {
        var img = Root.GetTexture().GetImage();
        var path = System.IO.Path.Combine(_dir, name + ".png");
        var err = img.SavePng(path);
        WriteDone($"shot {name}", err == Error.Ok ? $"{img.GetWidth()}x{img.GetHeight()}" : $"ERR {err}");
    }

    private static void Move(float x, float y) =>
        Input.ParseInputEvent(new InputEventMouseMotion { Position = new Vector2(x, y), GlobalPosition = new Vector2(x, y) });

    private static void Click(float x, float y)
    {
        var pos = new Vector2(x, y);
        Move(x, y);
        Press(pos, true);
        _pending.Add((_time + 0.08, () => Press(pos, false)));
    }

    private static void Press(Vector2 pos, bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = pressed,
            Position = pos,
            GlobalPosition = pos,
            ButtonMask = pressed ? MouseButtonMask.Left : 0,
        });

    private static void Drag(float x1, float y1, float x2, float y2)
    {
        var a = new Vector2(x1, y1);
        var b = new Vector2(x2, y2);
        Move(x1, y1);
        Press(a, true);
        for (int i = 1; i <= 6; i++)
        {
            var mid = a.Lerp(b, i / 6f);
            _pending.Add((_time + 0.04 * i, () =>
                Input.ParseInputEvent(new InputEventMouseMotion { Position = mid, GlobalPosition = mid, ButtonMask = MouseButtonMask.Left })));
        }
        _pending.Add((_time + 0.04 * 7, () => Press(b, false)));
    }

    private static void Tap(float x, float y)
    {
        var pos = new Vector2(x, y);
        TouchPress(pos, true);
        _pending.Add((_time + 0.09, () => TouchPress(pos, false)));
    }

    private static void TouchPress(Vector2 pos, bool pressed) =>
        Input.ParseInputEvent(new InputEventScreenTouch { Index = 0, Pressed = pressed, Position = pos });

    private static void TouchDrag(float x1, float y1, float x2, float y2)
    {
        var a = new Vector2(x1, y1);
        var b = new Vector2(x2, y2);
        TouchPress(a, true);
        var prev = a;
        for (int i = 1; i <= 8; i++)
        {
            var mid = a.Lerp(b, i / 8f);
            var from = prev;
            _pending.Add((_time + 0.04 * i, () =>
                Input.ParseInputEvent(new InputEventScreenDrag { Index = 0, Position = mid, Relative = mid - from })));
            prev = mid;
        }
        _pending.Add((_time + 0.04 * 9, () => TouchPress(b, false)));
    }

    private static void RealDrag(float x1, float y1, float x2, float y2)
    {
        var a = new Vector2(x1, y1);
        var b = new Vector2(x2, y2);
        Input.WarpMouse(a);
        Move(x1, y1);
        Press(a, true);
        for (int i = 1; i <= 10; i++)
        {
            var mid = a.Lerp(b, i / 10f);
            _pending.Add((_time + 0.05 * i, () =>
            {
                Input.WarpMouse(mid);
                Input.ParseInputEvent(new InputEventMouseMotion { Position = mid, GlobalPosition = mid, ButtonMask = MouseButtonMask.Left });
            }));
        }
        _pending.Add((_time + 0.05 * 12, () => Press(b, false)));
    }

    private static void KeyPress(string keyName)
    {
        if (!Enum.TryParse<Key>(keyName, true, out var key)) { WriteDone("key " + keyName, "bad key"); return; }
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        _pending.Add((_time + 0.06, () =>
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false })));
    }

    private static void DumpScreen()
    {
        Node? best = null;
        foreach (var c in Root.GetChildren()) best = c;
        if (best is not null) NodeInspector.DumpTree(best, $"SCREEN: {best.Name}");
        WriteDone("dumpscreen", best is null ? "none" : best.Name);
    }

    private static void WriteDone(string cmd, string result)
    {
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(_dir, "done"), $"{cmd}\n{result}\n"); } catch { }
    }
}
