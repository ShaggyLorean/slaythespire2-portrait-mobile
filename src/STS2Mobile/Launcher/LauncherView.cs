using System;
using System.IO;
using Godot;
using STS2Mobile.Launcher.Components;
using STS2Mobile.Launcher.Sections;
using STS2Mobile.Patches;
using STS2Mobile.Portrait;

namespace STS2Mobile.Launcher;

// Owns launcher view references and UI behavior.
internal sealed class LauncherView
{
    internal LoginSection Login { get; }
    internal CodeSection Code { get; }
    internal DownloadSection Download { get; }
    internal ActionSection Actions { get; }
    internal ModeSelectionSection ModeSelection { get; }
    internal OfflineImportSection OfflineImport { get; }
    private LogView Log { get; }

    private readonly Control _parent;
    private readonly StyledPanel _panel;
    private readonly ScrollContainer _scroll;
    private readonly float _panelBaseY;
    private readonly float _scale;
    private readonly StyledLabel _statusLabel;

    internal LauncherView(Control parent, float scale)
    {
        var dismissKeyboard = new Control.GuiInputEventHandler(DismissKeyboard);
        var (panel, scroll, rootColumns) = BuildShell(parent, scale, dismissKeyboard);
        _parent = parent;
        _panel = panel;
        _scroll = scroll;
        _panelBaseY = panel.Position.Y;
        _scale = scale;
        (_statusLabel, ModeSelection, OfflineImport, Login, Code, Download, Actions) = BuildPrimaryColumn(
            scale,
            rootColumns
        );
        Log = BuildLogColumn(scale, rootColumns, dismissKeyboard);
    }

    internal void SetStatus(string text) => _statusLabel.Text = text;

    internal void AppendLog(string msg) => Log.AppendLog(msg);

    internal void AppendColoredLog(string msg, Godot.Color color) => Log.AppendColoredLog(msg, color);

    internal void HideAllSections()
    {
        _parent.GetViewport()?.GuiReleaseFocus();
        ResetScrollToTop();
        Login.Visible = false;
        Code.Visible = false;
        Download.Visible = false;
        ModeSelection.Visible = false;
        OfflineImport.Visible = false;
        Actions.HideAll();
    }

    private void ResetScrollToTop()
    {
        _scroll.ScrollVertical = 0;
        // Visibility changes are laid out at the end of the frame. Reset once
        // more after layout so a previously focused button cannot restore the
        // old section's scroll offset.
        Callable.From(() => _scroll.ScrollVertical = 0).CallDeferred();
    }

    internal void ShowModeSelection()
    {
        HideAllSections();
        SetStatus("Choose how you want to play");
        ModeSelection.Visible = true;
    }

    internal void ShowOfflineImport()
    {
        HideAllSections();
        SetStatus("Offline setup");
        OfflineImport.Reset();
        OfflineImport.Visible = true;
    }

    internal void UpdateKeyboardOffset()
    {
        // Android's adjustResize already exposes the area above the IME, and
        // ScrollContainer keeps the focused field visible. Applying a second
        // keyboard-height offset here pushed the username field completely off
        // screen on tall devices.
        _panel.Position = new Vector2(_panel.Position.X, _panelBaseY);
    }

    internal void ShowConfirmation(string message, Action onConfirmed)
    {
        var dialog = new StyledDialog(message, _scale);
        dialog.Confirmed += onConfirmed;
        _parent.AddChild(dialog);
    }

    private void DismissKeyboard(InputEvent ev)
    {
        if (ev is InputEventMouseButton { Pressed: true } or InputEventScreenTouch { Pressed: true })
            _parent.GetViewport()?.GuiReleaseFocus();
    }

    private static (StyledPanel Panel, ScrollContainer Scroll, VBoxContainer RootColumns) BuildShell(
        Control parent,
        float scale,
        Control.GuiInputEventHandler dismissKeyboard
    )
    {
        parent.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // The launcher is the whole screen on Android. Treating it like a
        // centered desktop dialog created large, unusable gutters around every
        // edge on tall phones.
        var safeTop = PortraitDisplay.SafeTop();
        var safeBottom = PortraitDisplay.SafeBottom();
        var panel = new StyledPanel(
            scale,
            fullBleed: true,
            safeTop: safeTop,
            safeBottom: safeBottom
        );
        PatchHelper.Log($"[Launcher] Safe content insets: top={safeTop:F0}, bottom={safeBottom:F0}");
        panel.UpdateSizeFromViewport(
            parent.GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080)
        );
        panel.Panel.GuiInput += dismissKeyboard;
        parent.AddChild(panel);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        panel.Content.AddChild(scroll);

        var rootColumns = new VBoxContainer();
        rootColumns.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rootColumns.AddThemeConstantOverride(
            LauncherViewLayoutMetrics.ThemeSeparation,
            LauncherViewLayoutMetrics.ScaleInt(LauncherViewLayoutMetrics.RootColumnSeparation, scale)
        );
        scroll.AddChild(rootColumns);

        return (panel, scroll, rootColumns);
    }

    private static (
        StyledLabel StatusLabel,
        ModeSelectionSection ModeSelection,
        OfflineImportSection OfflineImport,
        LoginSection Login,
        CodeSection Code,
        DownloadSection Download,
        ActionSection Actions
    ) BuildPrimaryColumn(float scale, VBoxContainer root)
    {
        var left = new VBoxContainer();
        left.CustomMinimumSize = new Vector2(
            LauncherViewLayoutMetrics.ScaleInt(LauncherViewLayoutMetrics.PrimaryColumnMinWidth, scale),
            0
        );
        left.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        left.AddThemeConstantOverride(
            LauncherViewLayoutMetrics.ThemeSeparation,
            LauncherViewLayoutMetrics.ScaleInt(LauncherViewLayoutMetrics.PrimaryColumnSeparation, scale)
        );
        root.AddChild(left);

        // The standalone launcher is visible before the downloaded game PCK is
        // mounted, so game textures are not loadable here. Keep the title native
        // and avoid noisy resource-loader errors during every cold start.
        var title = new StyledLabel("Slay the Spire 2", scale, fontSize: 31);
        title.AddThemeFontOverride("font", LauncherComponentTheme.DisplayFont);
        title.AddThemeColorOverride(
            LauncherViewLayoutMetrics.ThemeFontColor,
            LauncherComponentTheme.Gold
        );
        left.AddChild(title);
        left.AddChild(new HSeparator());

        var statusLabel = new StyledLabel("Initializing...", scale);
        statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        statusLabel.AddThemeColorOverride(
            LauncherViewLayoutMetrics.ThemeFontColor,
            LauncherComponentTheme.Ivory
        );
        left.AddChild(statusLabel);

        var modeSelection = new ModeSelectionSection(scale);
        left.AddChild(modeSelection);

        var offlineImport = new OfflineImportSection(scale);
        left.AddChild(offlineImport);

        var login = new LoginSection(scale);
        left.AddChild(login);

        var code = new CodeSection(scale);
        left.AddChild(code);

        var download = new DownloadSection(scale);
        left.AddChild(download);

        var actions = new ActionSection(scale);
        left.AddChild(actions);

        return (statusLabel, modeSelection, offlineImport, login, code, download, actions);
    }

    private static LogView BuildLogColumn(
        float scale,
        VBoxContainer root,
        Control.GuiInputEventHandler dismissKeyboard
    )
    {
        var right = new VBoxContainer();
        right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        right.CustomMinimumSize = new Vector2(0, LauncherViewLayoutMetrics.ScaleInt(92, scale));
        root.AddChild(right);

        var detailsButton = new StyledButton("Show details", scale, fontSize: 13, height: 42);
        right.AddChild(detailsButton);

        var details = new VBoxContainer { Visible = false };
        right.AddChild(details);

        var log = new LogView(scale);
        log.GuiInput += dismissKeyboard;
        details.AddChild(log);
        details.AddChild(new FmodAttributionSection(scale));
        detailsButton.Pressed += () =>
        {
            details.Visible = !details.Visible;
            detailsButton.Text = details.Visible ? "Hide details" : "Show details";
        };
        return log;
    }

    private sealed class LogView : RichTextLabel
    {
        internal LogView(float scale)
        {
            CustomMinimumSize = new Vector2(
                0,
                LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.LogHeight)
            );
            ScrollFollowing = true;
            BbcodeEnabled = true;
            AddThemeFontSizeOverride(
                LauncherComponentTheme.NormalFontSize,
                LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.LogFontSize)
            );
            AddThemeColorOverride(LauncherComponentTheme.DefaultColor, LauncherComponentTheme.LogText);
            AddThemeStyleboxOverride(LauncherComponentTheme.StateNormal, BuildStyle(scale));
        }

        internal void AppendLog(string msg) => AddText(msg + "\n");

        internal void AppendColoredLog(string msg, Color color)
        {
            PushColor(color);
            AddText(msg + "\n");
            Pop();
        }

        private static StyleBoxFlat BuildStyle(float scale)
        {
            var background = new StyleBoxFlat();
            background.BgColor = LauncherComponentTheme.LogBackground;
            background.SetCornerRadiusAll(
                LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.LogRadius)
            );
            background.ContentMarginLeft = LauncherComponentTheme.ScaleInt(
                scale,
                LauncherComponentTheme.LogMarginHorizontal
            );
            background.ContentMarginRight = LauncherComponentTheme.ScaleInt(
                scale,
                LauncherComponentTheme.LogMarginHorizontal
            );
            background.ContentMarginTop = LauncherComponentTheme.ScaleInt(
                scale,
                LauncherComponentTheme.LogMarginVertical
            );
            background.ContentMarginBottom = LauncherComponentTheme.ScaleInt(
                scale,
                LauncherComponentTheme.LogMarginVertical
            );
            return background;
        }
    }

    private sealed class StyledDialog : ColorRect
    {
        internal event Action Confirmed;
        private event Action Cancelled;

        internal StyledDialog(string message, float scale)
        {
            SetAnchorsPreset(Control.LayoutPreset.FullRect);
            Color = LauncherComponentTheme.DialogOverlay;

            var center = BuildCenter();
            var dialogBox = BuildDialogBox(scale);
            var vbox = BuildContentBox(scale);
            dialogBox.AddChild(vbox);

            vbox.AddChild(BuildMessage(message, scale));
            vbox.AddChild(BuildButtons(scale));

            center.AddChild(dialogBox);
            AddChild(center);
        }

        private static CenterContainer BuildCenter()
        {
            var center = new CenterContainer();
            center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            return center;
        }

        private static PanelContainer BuildDialogBox(float scale)
        {
            var dialogBox = new PanelContainer();
            var boxStyle = new StyleBoxFlat();
            boxStyle.BgColor = LauncherComponentTheme.DialogPanelBackground;
            boxStyle.SetCornerRadiusAll(
                LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.DialogPanelRadius)
            );
            boxStyle.SetContentMarginAll(
                LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.DialogPanelMargin)
            );
            dialogBox.AddThemeStyleboxOverride(LauncherComponentTheme.Panel, boxStyle);
            return dialogBox;
        }

        private static VBoxContainer BuildContentBox(float scale)
        {
            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride(
                LauncherComponentTheme.ThemeSeparation,
                LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.DialogContentSeparation)
            );
            return vbox;
        }

        private static Label BuildMessage(string message, float scale)
        {
            var label = new StyledLabel(
                message,
                scale,
                fontSize: LauncherComponentTheme.DialogMessageFontSize
            );
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.CustomMinimumSize = new Vector2(
                LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.DialogMessageWidth),
                0
            );
            label.HorizontalAlignment = HorizontalAlignment.Center;
            return label;
        }

        private HBoxContainer BuildButtons(float scale)
        {
            var buttonRow = new HBoxContainer();
            buttonRow.AddThemeConstantOverride(
                LauncherComponentTheme.ThemeSeparation,
                LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.DialogButtonSeparation)
            );
            buttonRow.Alignment = BoxContainer.AlignmentMode.Center;

            buttonRow.AddChild(BuildButton("Cancel", scale, () => Cancelled?.Invoke()));
            buttonRow.AddChild(BuildButton("OK", scale, () => Confirmed?.Invoke()));

            return buttonRow;
        }

        private Button BuildButton(string text, float scale, Action callback)
        {
            var button = new StyledButton(
                text,
                scale,
                LauncherComponentTheme.DialogButtonFontSize,
                LauncherComponentTheme.DialogButtonHeight
            );
            button.CustomMinimumSize = new Vector2(
                LauncherComponentTheme.ScaleInt(scale, LauncherComponentTheme.DialogButtonWidth),
                button.CustomMinimumSize.Y
            );
            button.Pressed += () =>
            {
                QueueFree();
                callback?.Invoke();
            };
            return button;
        }
    }

    private sealed class FmodAttributionSection : VBoxContainer
    {
        private const string CreditText = "Made using FMOD Studio by Firelight Technologies Pty Ltd.";
        private const int CreditFontSize = 8;
        private const string LogoFileName = "fmod_logo.png";
        private const int LogoHeight = 30;
        private const int LogoWidth = 120;
        private static readonly Color CreditColor = new(0.5f, 0.5f, 0.55f);

        internal FmodAttributionSection(float scale)
        {
            var logo = LoadLogo(scale);
            if (logo != null)
                AddChild(logo);

            var credit = new StyledLabel(
                CreditText,
                scale,
                fontSize: CreditFontSize
            );
            credit.AddThemeColorOverride(
                LauncherViewLayoutMetrics.ThemeFontColor,
                CreditColor
            );
            AddChild(credit);
        }

        private static TextureRect LoadLogo(float scale)
        {
            try
            {
                var logoPath = Path.Combine(OS.GetDataDir(), LogoFileName);
                if (!File.Exists(logoPath))
                {
                    PatchHelper.Log($"FMOD logo not found at {logoPath}");
                    return null;
                }

                var bytes = File.ReadAllBytes(logoPath);
                var image = new Image();
                image.LoadPngFromBuffer(bytes);
                var texture = ImageTexture.CreateFromImage(image);

                return new TextureRect
                {
                    Texture = texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(
                        (int)(LogoWidth * scale),
                        (int)(LogoHeight * scale)
                    ),
                };
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"Failed to load FMOD logo: {ex.Message}");
                return null;
            }
        }
    }
}
