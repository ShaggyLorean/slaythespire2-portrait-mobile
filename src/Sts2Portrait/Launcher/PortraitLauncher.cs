using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;

namespace Sts2Portrait.Launcher;

// Our launcher screen. The mobile launcher's install, session and launch logic
// (LauncherModel) is reused through reflection; its landscape view is never built.
// Everything model-facing is probed at runtime, so a base with more capabilities
// (Steam sign in, cloud saves) lights up extra actions instead of needing a rewrite.
public static class PortraitLauncher
{
    private static Type _uiType, _modelType, _appPaths, _warmupType;

    public static void TryInstall(Harmony harmony)
    {
        _uiType = AccessTools.TypeByName("STS2Mobile.Launcher.LauncherUI");
        _modelType = AccessTools.TypeByName("STS2Mobile.Launcher.LauncherModel");
        _appPaths = AccessTools.TypeByName("STS2Mobile.AppPaths");
        _warmupType = AccessTools.TypeByName("STS2Mobile.Launcher.ShaderWarmupScreen");
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
    private static Control _root;
    private static Button _action;
    private static Label _status;
    private static ProgressBar _progress;
    private static Control _overlay;
    private static bool _inGameMode;
    private static bool _ready;
    private static bool _needsWarmup;
    private static int _fitFrames;

    private static void Build(Control root)
    {
        try { DisplayServer.ScreenSetOrientation(DisplayServer.ScreenOrientation.Portrait); } catch { }
        PortraitDisplay.ApplyPortrait();

        _root = root;
        root.ZIndex = 100;
        FitRoot();
        var c = PortraitConfig.CanvasSize;

        _inGameMode = (bool)(Traverse.Create(root).Field("_inGameMode").GetValue() ?? true);
        _model = Activator.CreateInstance(_modelType, OS.GetDataDir());
        Traverse.Create(_model).Property("InGameMode").SetValue(_inGameMode);
        Traverse.Create(root).Field("_model").SetValue(_model);

        root.AddChild(PortraitTheme.Background());

        // Title block, upper third.
        var title = new VBoxContainer();
        title.AddThemeConstantOverride("separation", 10);
        title.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        title.OffsetTop = c.Y * 0.14f;
        title.OffsetLeft = 60; title.OffsetRight = -60;
        title.AddChild(PortraitTheme.Heading("Slay the Spire II", 96));
        title.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        title.AddChild(PortraitTheme.Rule(c.X * 0.34f));
        title.AddChild(PortraitTheme.BodyText("portrait", 38, PortraitTheme.Muted));
        root.AddChild(title);

        // Action block, lower middle.
        var act = new VBoxContainer();
        act.AddThemeConstantOverride("separation", 30);
        act.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        act.OffsetTop = c.Y * 0.47f;
        act.OffsetLeft = c.X * 0.13f; act.OffsetRight = -c.X * 0.13f;

        _status = PortraitTheme.BodyText("", 32);
        act.AddChild(_status);

        _progress = new ProgressBar { CustomMinimumSize = new Vector2(0, 18), Visible = false, ShowPercentage = false };
        PortraitTheme.StyleProgress(_progress);
        act.AddChild(_progress);

        _action = new Button { CustomMinimumSize = new Vector2(0, 118) };
        _action.AddThemeFontSizeOverride("font_size", 44);
        PortraitTheme.StyleButton(_action);
        _action.Pressed += OnAction;
        act.AddChild(_action);

        var minor = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        minor.AddThemeConstantOverride("separation", 60);
        if (HasSteam())
        {
            var steam = SmallButton("steam sign in");
            steam.Pressed += ShowSteamOverlay;
            minor.AddChild(steam);
        }
        var logs = SmallButton("logs");
        logs.Pressed += ShowLogsOverlay;
        minor.AddChild(logs);
        act.AddChild(minor);
        root.AddChild(act);

        var footer = PortraitTheme.BodyText("unofficial portrait build", 24, PortraitTheme.Muted);
        footer.HorizontalAlignment = HorizontalAlignment.Left;
        footer.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        footer.OffsetLeft = 48; footer.OffsetTop = -76; footer.OffsetRight = 700;
        root.AddChild(footer);

        root.GetTree().ProcessFrame += Pump;
        _fitFrames = 30;
        root.GetTree().ProcessFrame += FitLoop;
        WireModelEvents();
        Refresh();
    }

    private static Button SmallButton(string text)
    {
        var b = new Button { Text = text };
        b.AddThemeFontSizeOverride("font_size", 30);
        PortraitTheme.StyleButton(b, primary: false);
        return b;
    }

    private static bool HasSteam() =>
        AccessTools.Method(_modelType, "LoginAsync") is not null &&
        AccessTools.Method(_modelType, "Connect") is not null;

    private static void FitRoot()
    {
        if (_root is null) return;
        _root.Position = Vector2.Zero;
        _root.Size = PortraitConfig.CanvasSize;
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
        _needsWarmup = false;
        try
        {
            var m = _warmupType is null ? null : AccessTools.Method(_warmupType, "NeedsWarmup");
            if (_ready && m is not null) _needsWarmup = (bool)m.Invoke(null, null);
        }
        catch { }

        _progress.Visible = false;
        if (!_ready)
        {
            _status.Text = "Copy SlayTheSpire2.pck and data_sts2_windows_x86_64\nfrom your PC into /StS2LauncherMM/ on this phone.";
            _action.Text = "INSTALL";
        }
        else if (_needsWarmup)
        {
            _status.Text = "First run only: shaders have to be built once.\nTakes a few minutes, then you land back here.";
            _action.Text = "BUILD SHADERS";
        }
        else
        {
            _status.Text = "Ready.";
            _action.Text = _inGameMode ? "PLAY" : "RESTART";
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
                _status.Text = "Allow file access on the next screen, then tap INSTALL again.";
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

    // ---- logs overlay ----

    private static void ShowLogsOverlay()
    {
        CloseOverlay();
        var c = PortraitConfig.CanvasSize;
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PortraitTheme.Box(PortraitTheme.PanelFill));
        panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        panel.OffsetLeft = 40; panel.OffsetTop = c.Y * 0.08f; panel.OffsetRight = -40; panel.OffsetBottom = -c.Y * 0.08f;

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 18);
        panel.AddChild(col);

        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        bar.AddThemeConstantOverride("separation", 40);
        string logText = ReadLogTail();
        var copy = SmallButton("copy");
        copy.Pressed += () => { try { DisplayServer.ClipboardSet(logText); } catch { } };
        var close = SmallButton("close");
        close.Pressed += CloseOverlay;
        bar.AddChild(copy);
        bar.AddChild(close);
        col.AddChild(bar);

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var body = PortraitTheme.MonoText(logText, 22);
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(body);
        col.AddChild(scroll);

        _overlay = panel;
        _root.AddChild(panel);
    }

    private static string ReadLogTail()
    {
        try
        {
            var dir = "/storage/emulated/0/StS2LauncherMM/Logs";
            var file = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            if (file is null) return "No log file yet.";
            var lines = File.ReadAllLines(file);
            return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 400)));
        }
        catch (Exception e) { return "Could not read logs: " + e.Message; }
    }

    // ---- steam overlay (scaffold; wired by reflection, lights up when the base supports it) ----

    private static LineEdit _user, _pass, _code;
    private static Label _steamStatus;

    private static void ShowSteamOverlay()
    {
        CloseOverlay();
        var c = PortraitConfig.CanvasSize;
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PortraitTheme.Box(PortraitTheme.PanelFill));
        panel.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        panel.OffsetLeft = 60; panel.OffsetRight = -60; panel.OffsetTop = c.Y * 0.2f;

        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 24);
        panel.AddChild(col);

        col.AddChild(PortraitTheme.Heading("Steam sign in", 44));
        _steamStatus = PortraitTheme.BodyText("Downloads the game with your own account.\nExperimental.", 26, PortraitTheme.Muted);
        col.AddChild(_steamStatus);

        _user = Field("username", secret: false);
        _pass = Field("password", secret: true);
        _code = Field("guard code (if asked)", secret: false);
        col.AddChild(_user);
        col.AddChild(_pass);
        col.AddChild(_code);

        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bar.AddThemeConstantOverride("separation", 50);
        var login = new Button { Text = "SIGN IN", CustomMinimumSize = new Vector2(300, 88) };
        login.AddThemeFontSizeOverride("font_size", 34);
        PortraitTheme.StyleButton(login);
        login.Pressed += DoSteamLogin;
        var back = SmallButton("close");
        back.Pressed += CloseOverlay;
        bar.AddChild(login);
        bar.AddChild(back);
        col.AddChild(bar);

        _overlay = panel;
        _root.AddChild(panel);
    }

    private static LineEdit Field(string hint, bool secret)
    {
        var e = new LineEdit { PlaceholderText = hint, Secret = secret, CustomMinimumSize = new Vector2(0, 84) };
        e.AddThemeFontSizeOverride("font_size", 32);
        e.AddThemeStyleboxOverride("normal", PortraitTheme.Box(PortraitTheme.ButtonFill, border: false));
        e.AddThemeStyleboxOverride("focus", PortraitTheme.Box(PortraitTheme.ButtonFill));
        return e;
    }

    private static async void DoSteamLogin()
    {
        try
        {
            _steamStatus.Text = "Connecting...";
            if (!string.IsNullOrEmpty(_code.Text))
            {
                AccessTools.Method(_modelType, "SubmitCode")?.Invoke(_model, new object[] { _code.Text });
                return;
            }
            AccessTools.Method(_modelType, "Connect")?.Invoke(_model, null);
            var task = AccessTools.Method(_modelType, "LoginAsync")
                ?.Invoke(_model, new object[] { _user.Text, _pass.Text });
            if (task is System.Threading.Tasks.Task t) await t;
            Defer(() => { if (_steamStatus is not null) _steamStatus.Text = "Sent. Watch the logs for the result."; });
        }
        catch (Exception e)
        {
            var msg = e.InnerException?.Message ?? e.Message;
            Defer(() => { if (_steamStatus is not null) _steamStatus.Text = "Sign in failed: " + msg; });
        }
    }

    private static void CloseOverlay()
    {
        if (_overlay is not null && GodotObject.IsInstanceValid(_overlay)) _overlay.QueueFree();
        _overlay = null;
    }

    // ---- model events ----

    private static void WireModelEvents()
    {
        Sub("DownloadCompleted", (Action)(() => Defer(() =>
        {
            _action.Disabled = false; Refresh();
        })));
        Sub("DownloadFailed", (Action<string>)(msg => Defer(() =>
        {
            _progress.Visible = false; _action.Disabled = false;
            _status.Text = "Install failed: " + msg;
        })));
        Sub("CodeNeeded", (Action<bool>)(_ => Defer(() =>
        {
            if (_steamStatus is not null) _steamStatus.Text = "Steam Guard code needed. Enter it and sign in again.";
        })));
        Sub("LogReceived", (Action<string>)(line => Defer(() =>
        {
            if (_steamStatus is not null && GodotObject.IsInstanceValid(_steamStatus)) _steamStatus.Text = line;
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
