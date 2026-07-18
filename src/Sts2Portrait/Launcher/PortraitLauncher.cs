using System;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace Sts2Portrait.Launcher;

// Our own launcher UI, in portrait. We reuse the mobile launcher's LauncherModel (the file
// install / launch / session logic) but throw away its landscape LauncherView entirely and
// present our own vertical design. Wired by replacing LauncherUI.Initialize via reflection, so
// there is no compile-time dependency on the launcher (on PC the type isn't found and this is a
// no-op). If anything here fails we return true and let the stock launcher run — never worse.
public static class PortraitLauncher
{
    private static Type _uiType, _modelType, _appPaths;

    public static void TryInstall(Harmony harmony)
    {
        _uiType = AccessTools.TypeByName("STS2Mobile.Launcher.LauncherUI");
        _modelType = AccessTools.TypeByName("STS2Mobile.Launcher.LauncherModel");
        _appPaths = AccessTools.TypeByName("STS2Mobile.AppPaths");
        if (_uiType is null || _modelType is null) return;
        var init = AccessTools.Method(_uiType, "Initialize");
        if (init is null) return;
        harmony.Patch(init, prefix: new HarmonyMethod(typeof(PortraitLauncher), nameof(InitializePrefix)));
        PortraitMod.Log("portrait launcher installed");
    }

    // Prefix: build our portrait launcher instead of theirs. Return false to skip the original.
    public static bool InitializePrefix(Control __instance)
    {
        try
        {
            Build(__instance);
            return false;
        }
        catch (Exception e)
        {
            PortraitMod.Log("portrait launcher failed, using stock: " + e.Message);
            return true;
        }
    }

    private static object _model;
    private static Button _action;
    private static Label _status;
    private static ProgressBar _progress;
    private static bool _inGameMode;

    private static void Build(Control root)
    {
        // Portrait window.
        try { DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait); } catch { }
        PortraitDisplay.ApplyPortrait();

        var vp = root.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1080, 2160);
        root.ZIndex = 100;
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        _inGameMode = (bool)(Traverse.Create(root).Field("_inGameMode").GetValue() ?? true);

        // Build the launcher model (their install/launch logic) and hand it back to LauncherUI so
        // its WaitForLaunch() keeps working.
        _model = Activator.CreateInstance(_modelType, OS.GetDataDir());
        Traverse.Create(_model).Property("InGameMode").SetValue(_inGameMode);
        Traverse.Create(root).Field("_model").SetValue(_model);

        // ---- our vertical UI ----
        var bg = new ColorRect { Color = new Color("120a0a") };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(bg);

        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 34);
        col.OffsetLeft = 70; col.OffsetRight = -70;
        root.AddChild(col);

        col.AddChild(Spacer(40));
        col.AddChild(Title("Slay the Spire", 84, new Color("d8b24a")));
        col.AddChild(Title("II  ·  PORTRAIT", 48, new Color("7fb0b8")));
        col.AddChild(Spacer(30));

        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _status.AddThemeFontSizeOverride("font_size", 34);
        _status.AddThemeColorOverride("font_color", new Color("c9c2bd"));
        col.AddChild(_status);

        _progress = new ProgressBar { CustomMinimumSize = new Vector2(0, 26), Visible = false, ShowPercentage = false };
        col.AddChild(_progress);

        _action = new Button { CustomMinimumSize = new Vector2(0, 120), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _action.AddThemeFontSizeOverride("font_size", 44);
        StyleButton(_action);
        _action.Pressed += OnAction;
        col.AddChild(_action);

        col.AddChild(Spacer(60));

        // main-thread pump for model events (they fire on worker threads)
        root.GetTree().ProcessFrame += Pump;
        WireModelEvents();
        Refresh();
    }

    private static bool _ready;

    private static void Refresh()
    {
        _ready = (bool)AccessTools.Method(_modelType, "GameFilesReady").Invoke(null, null);
        if (_ready)
        {
            _status.Text = "Ready to play.";
            _action.Text = _inGameMode ? "PLAY" : "RESTART";
            _progress.Visible = false;
        }
        else
        {
            _status.Text = "Game files not found.\nPut SlayTheSpire2.pck + data_sts2_windows_x86_64 in\n/StS2LauncherMM/, then install.";
            _action.Text = "INSTALL FROM STORAGE";
        }
    }

    private static void OnAction()
    {
        try
        {
            if (_ready)
            {
                AccessTools.Method(_modelType, "Launch").Invoke(_model, null);   // completes WaitForLaunch -> game boots
                return;
            }
            bool hasPerm = _appPaths is not null &&
                           (bool)AccessTools.Method(_appPaths, "HasStoragePermission").Invoke(null, null);
            if (!hasPerm)
            {
                _status.Text = "Grant 'All files access' so we can read /StS2LauncherMM/, then tap again.";
                AccessTools.Method(_appPaths, "RequestStoragePermission")?.Invoke(null, null);
                return;
            }
            _progress.Visible = true;
            try { _progress.Indeterminate = true; } catch { }
            _status.Text = "Copying game files…";
            _action.Disabled = true;
            AccessTools.Method(_modelType, "StartDownloadAsync").Invoke(_model, new object[] { null });
        }
        catch (Exception e) { _status.Text = "Error: " + e.Message; _action.Disabled = false; }
    }

    private static void WireModelEvents()
    {
        // Only the events whose delegate types we can bind without referencing launcher-internal
        // types: DownloadCompleted (Action) and DownloadFailed (Action<string>). Progress is shown
        // as text rather than binding the typed Action<DownloadProgress> event.
        Sub("DownloadCompleted", (Action)(() => Defer(() =>
        {
            _progress.Visible = false; _action.Disabled = false; Refresh();
            _status.Text = _inGameMode ? "Installed. Tap PLAY." : "Installed. Tap RESTART.";
        })));
        Sub("DownloadFailed", (Action<string>)(msg => Defer(() =>
        {
            _progress.Visible = false; _action.Disabled = false;
            _status.Text = "Install failed: " + msg;
        })));
    }

    private static void Sub(string ev, Delegate handler)
    {
        try
        {
            var e = _modelType.GetEvent(ev, BindingFlags.Public | BindingFlags.Instance);
            if (e is null) return;
            var d = Delegate.CreateDelegate(e.EventHandlerType, handler.Target, handler.Method, false);
            if (d is not null) e.AddEventHandler(_model, d);
            else PortraitMod.Log($"launcher event {ev}: type mismatch {e.EventHandlerType}");
        }
        catch (Exception ex) { PortraitMod.Log($"launcher event {ev}: {ex.Message}"); }
    }

    private static readonly System.Collections.Generic.Queue<Action> _q = new();
    private static void Defer(Action a) { lock (_q) _q.Enqueue(a); }
    private static void Pump()
    {
        while (true)
        {
            Action a;
            lock (_q) { if (_q.Count == 0) break; a = _q.Dequeue(); }
            try { a(); } catch { }
        }
    }

    // ---- small style helpers ----
    private static Control Spacer(float h) => new Control { CustomMinimumSize = new Vector2(0, h) };

    private static Label Title(string text, int size, Color c)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", c);
        return l;
    }

    private static void StyleButton(Button b)
    {
        var normal = new StyleBoxFlat { BgColor = new Color("3a2415"), CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14, CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, BorderColor = new Color("d8b24a"), BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3 };
        var hover = (StyleBoxFlat)normal.Duplicate(); hover.BgColor = new Color("4a2f1a");
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("disabled", normal);
        b.AddThemeColorOverride("font_color", new Color("f0d98a"));
    }
}
