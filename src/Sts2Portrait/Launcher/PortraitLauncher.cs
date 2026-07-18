using System;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace Sts2Portrait.Launcher;

// Our launcher screen, in portrait. The mobile launcher's install and launch logic
// (LauncherModel) is reused through reflection; its landscape view is never built.
// Wired by a Harmony prefix on LauncherUI.Initialize, resolved by name, so on PC the
// type does not exist and nothing happens. If anything in here throws we fall back to
// the stock launcher rather than leaving the user on a dead screen.
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
    private static bool _ready;

    private static void Build(Control root)
    {
        try { DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait); } catch { }
        PortraitDisplay.ApplyPortrait();

        // The launcher node hangs under a plain Node, so anchors do nothing on the root
        // itself: it has to be sized by hand or everything collapses to a corner. Use the
        // content-scale canvas (which ApplyPortrait just set to portrait), not the viewport
        // rect, which is still landscape this frame. Re-fit for a few frames as it settles.
        _root = root;
        root.ZIndex = 100;
        FitRoot();
        var vp = PortraitConfig.CanvasSize;

        _inGameMode = (bool)(Traverse.Create(root).Field("_inGameMode").GetValue() ?? true);

        _model = Activator.CreateInstance(_modelType, OS.GetDataDir());
        Traverse.Create(_model).Property("InGameMode").SetValue(_inGameMode);
        Traverse.Create(root).Field("_model").SetValue(_model);

        root.AddChild(PortraitTheme.Background());

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(center);

        float colW = Mathf.Min(vp.X * 0.82f, 860f);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(colW, 0) };
        col.AddThemeConstantOverride("separation", 38);
        center.AddChild(col);

        col.AddChild(PortraitTheme.Heading("SLAY THE SPIRE", 88));
        col.AddChild(PortraitTheme.Heading("II", 120));
        col.AddChild(PortraitTheme.Divider(colW * 0.45f));
        col.AddChild(PortraitTheme.Heading("PORTRAIT", 40, PortraitTheme.Teal));

        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 26) });

        _status = PortraitTheme.BodyText("");
        col.AddChild(_status);

        _progress = new ProgressBar { CustomMinimumSize = new Vector2(0, 24), Visible = false, ShowPercentage = false };
        PortraitTheme.StyleProgress(_progress);
        col.AddChild(_progress);

        _action = new Button { CustomMinimumSize = new Vector2(0, 128) };
        _action.AddThemeFontSizeOverride("font_size", 46);
        PortraitTheme.StyleButton(_action);
        _action.Pressed += OnAction;
        col.AddChild(_action);

        var footer = PortraitTheme.BodyText("unofficial portrait build", 26);
        footer.AddThemeColorOverride("font_color", PortraitTheme.Muted);
        col.AddChild(footer);

        root.GetTree().ProcessFrame += Pump;
        _fitFrames = 30;
        root.GetTree().ProcessFrame += FitLoop;
        WireModelEvents();
        Refresh();
    }

    private static Control _root;
    private static int _fitFrames;

    private static void FitRoot()
    {
        if (_root is null) return;
        var c = PortraitConfig.CanvasSize;
        _root.Position = Vector2.Zero;
        _root.Size = c;
    }

    private static void FitLoop()
    {
        FitRoot();
        if (--_fitFrames <= 0 && _root?.GetTree() is SceneTree t)
            t.ProcessFrame -= FitLoop;
    }

    private static void Refresh()
    {
        _ready = (bool)AccessTools.Method(_modelType, "GameFilesReady").Invoke(null, null);
        if (_ready)
        {
            _status.Text = "Ready.";
            _action.Text = _inGameMode ? "PLAY" : "RESTART";
            _progress.Visible = false;
        }
        else
        {
            _status.Text = "Copy SlayTheSpire2.pck and data_sts2_windows_x86_64\nfrom your PC into /StS2LauncherMM/ on this phone.";
            _action.Text = "INSTALL";
        }
    }

    private static void OnAction()
    {
        try
        {
            if (_ready)
            {
                AccessTools.Method(_modelType, "Launch").Invoke(_model, null);
                return;
            }
            bool hasPerm = _appPaths is not null &&
                           (bool)AccessTools.Method(_appPaths, "HasStoragePermission").Invoke(null, null);
            if (!hasPerm)
            {
                _status.Text = "Allow file access on the next screen, then come back and tap INSTALL again.";
                AccessTools.Method(_appPaths, "RequestStoragePermission")?.Invoke(null, null);
                return;
            }
            _progress.Visible = true;
            try { _progress.Indeterminate = true; } catch { }
            _status.Text = "Copying game files, hang on.";
            _action.Disabled = true;
            AccessTools.Method(_modelType, "StartDownloadAsync").Invoke(_model, new object[] { null });
        }
        catch (Exception e) { _status.Text = "Error: " + e.Message; _action.Disabled = false; }
    }

    private static void WireModelEvents()
    {
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
}
