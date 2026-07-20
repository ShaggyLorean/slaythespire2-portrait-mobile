using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

internal sealed class ActionSection : VBoxContainer
{
    internal event Action LaunchPressed;
    internal event Action RetryPressed;
    internal event Action<bool> LocalBackupToggled;
    internal event Action<bool> CloudSyncToggled;
    internal event Action CloudPushPressed;
    internal event Action CloudPullPressed;
    internal event Action CheckForUpdatesPressed;
    internal event Action RedownloadPressed;
    internal event Action DiagnosticsPressed;
    internal event Action ShowLastErrorPressed;
    internal event Action CopyRawLogPressed;
    internal event Action SafeLaunchPressed;
    internal event Action ChangeModePressed;

    private readonly Button _launchButton;
    private readonly Button _safeLaunchButton;
    private readonly Button _retryButton;
    private readonly Button _localBackupToggle;
    private readonly Button _cloudSyncToggle;
    private readonly Button _pushButton;
    private readonly Button _pullButton;
    private readonly Button _updateButton;
    private readonly Button _redownloadButton;
    private readonly Button _diagnosticsButton;
    private readonly Button _showLastErrorButton;
    private readonly Button _copyRawLogButton;
    private readonly Button _changeModeButton;
    private readonly Button _moreButton;
    private readonly Button _troubleshootingButton;
    private readonly HBoxContainer _saveToggleRow;
    private readonly HBoxContainer _pushPullRow;
    private readonly VBoxContainer _advancedGroup;
    private readonly VBoxContainer _diagnosticsGroup;
    private readonly StyleBoxFlat _toggleOffStyle;
    private readonly StyleBoxFlat _toggleOnStyle;

    internal ActionSection(float scale)
    {
        var toggleRadius = (int)(4 * scale);
        var toggleBorderWidth = Math.Max(1, (int)(2 * scale));
        _toggleOffStyle = LauncherStyleBoxes.MakeOutline(
            new Color(0.7f, 0.25f, 0.25f),
            toggleRadius,
            toggleBorderWidth
        );
        _toggleOnStyle = LauncherStyleBoxes.MakeOutline(
            new Color(0.25f, 0.65f, 0.3f),
            toggleRadius,
            toggleBorderWidth
        );

        _retryButton = AddHiddenButton(
            "Try again",
            scale,
            LauncherSectionMetrics.PrimaryButtonFontSize,
            LauncherSectionMetrics.PrimaryButtonHeight,
            () => RetryPressed?.Invoke()
        );
        _launchButton = AddPrimaryHiddenButton(
            "Start game",
            scale,
            () => LaunchPressed?.Invoke()
        );
        if (_launchButton is StyledButton primaryLaunch)
            primaryLaunch.UsePrimaryAccent();

        _saveToggleRow = BuildCompactRow(scale);
        _saveToggleRow.Visible = false;
        AddChild(_saveToggleRow);
        _localBackupToggle = AddHiddenButton(
            _saveToggleRow,
            "Backups: Off",
            scale,
            13,
            48,
            null
        );
        _cloudSyncToggle = AddHiddenButton(
            _saveToggleRow,
            "Auto-sync: Off",
            scale,
            13,
            48,
            null
        );
        ConfigureToggle(
            _localBackupToggle,
            LocalBackupText,
            pressed => LocalBackupToggled?.Invoke(pressed)
        );
        ConfigureToggle(
            _cloudSyncToggle,
            CloudSyncText,
            pressed => CloudSyncToggled?.Invoke(pressed)
        );

        _pushPullRow = BuildCompactRow(scale);
        _pushPullRow.Visible = false;
        _pullButton = AddPushPullButton(
            _pushPullRow,
            "Download saves",
            scale,
            () => CloudPullPressed?.Invoke()
        );
        _pushButton = AddPushPullButton(
            _pushPullRow,
            "Upload saves",
            scale,
            () => CloudPushPressed?.Invoke()
        );
        AddChild(_pushPullRow);

        _moreButton = new StyledButton("More options", scale, fontSize: 14, height: 48);
        _moreButton.Visible = false;
        _moreButton.Pressed += ToggleAdvanced;
        AddChild(_moreButton);

        _advancedGroup = BuildDisclosureGroup(scale);
        AddChild(_advancedGroup);
        _updateButton = AddHiddenButton(_advancedGroup, "Check for updates", scale, 14, 50, () => CheckForUpdatesPressed?.Invoke());
        _redownloadButton = AddHiddenButton(_advancedGroup, "Repair game files", scale, 14, 50, () => RedownloadPressed?.Invoke());
        _safeLaunchButton = AddHiddenButton(_advancedGroup, "Start without cloud saves", scale, 14, 50, () => SafeLaunchPressed?.Invoke());
        _changeModeButton = AddHiddenButton(_advancedGroup, "Change play mode", scale, 14, 50, () => ChangeModePressed?.Invoke());

        _troubleshootingButton = new StyledButton("Troubleshooting", scale, fontSize: 14, height: 48);
        _troubleshootingButton.Visible = false;
        _troubleshootingButton.Pressed += ToggleDiagnostics;
        _advancedGroup.AddChild(_troubleshootingButton);

        _diagnosticsGroup = BuildDisclosureGroup(scale);
        _advancedGroup.AddChild(_diagnosticsGroup);
        _diagnosticsButton = AddHiddenButton(_diagnosticsGroup, "Export diagnostics", scale, 13, 46, () => DiagnosticsPressed?.Invoke());
        _showLastErrorButton = AddHiddenButton(_diagnosticsGroup, "Show last error", scale, 13, 46, () => ShowLastErrorPressed?.Invoke());
        _copyRawLogButton = AddHiddenButton(_diagnosticsGroup, "Copy error log", scale, 13, 46, () => CopyRawLogPressed?.Invoke());
    }

    internal void SetLocalBackupChecked(bool value)
    {
        SetToggleChecked(_localBackupToggle, value, LocalBackupText);
    }

    internal void SetCloudSyncChecked(bool value)
    {
        SetToggleChecked(_cloudSyncToggle, value, CloudSyncText);
    }

    internal void ShowLaunch(string text, bool showCloudSync, bool showUpdate)
    {
        _launchButton.Text = text;
        _launchButton.Visible = true;
        SetCloudControlsVisible(showCloudSync);
        ShowLaunchButtons(showUpdate);
        _retryButton.Visible = false;
    }

    internal void ShowRetry()
    {
        _retryButton.Visible = true;
        SetCloudControlsVisible(false);
        ShowRetryButtons();
    }

    internal void HideAll()
    {
        _retryButton.Visible = false;
        SetCloudControlsVisible(false);
        HideSecondaryButtons();
    }

    internal void SetPushPullDisabled(bool disabled)
    {
        _pushButton.Disabled = disabled;
        _pullButton.Disabled = disabled;
    }

    internal void SetUpdateButtonText(string text) => _updateButton.Text = text;

    internal void SetUpdateButtonDisabled(bool disabled) => _updateButton.Disabled = disabled;

    private void SetCloudControlsVisible(bool visible)
    {
        _saveToggleRow.Visible = visible;
        _localBackupToggle.Visible = visible;
        _cloudSyncToggle.Visible = visible;
        _pushPullRow.Visible = visible;
    }

    private void ShowLaunchButtons(bool showUpdate)
    {
        ShowUpdateButton(showUpdate);
        _redownloadButton.Visible = true;
        SetSupportButtonsVisible(true);
        _safeLaunchButton.Visible = true;
        _changeModeButton.Visible = true;
    }

    private void ShowRetryButtons()
    {
        ShowUpdateButton(false);
        _redownloadButton.Visible = false;
        SetSupportButtonsVisible(true);
        _safeLaunchButton.Visible = false;
        _launchButton.Visible = false;
        _changeModeButton.Visible = true;
    }

    private void HideSecondaryButtons()
    {
        ShowUpdateButton(false);
        _redownloadButton.Visible = false;
        SetSupportButtonsVisible(false);
        _safeLaunchButton.Visible = false;
        _launchButton.Visible = false;
        _changeModeButton.Visible = false;
    }

    private void ShowUpdateButton(bool visible)
    {
        _updateButton.Visible = visible;
        _updateButton.Disabled = false;
        _updateButton.Text = "Check for updates";
    }

    private void SetSupportButtonsVisible(bool visible)
    {
        _moreButton.Visible = visible;
        if (!visible)
            CollapseDisclosures();
        _diagnosticsButton.Visible = visible;
        _showLastErrorButton.Visible = visible;
        _copyRawLogButton.Visible = visible;
        _troubleshootingButton.Visible = visible;
    }

    private Button AddPrimaryHiddenButton(string text, float scale, Action pressed)
        => AddHiddenButton(
            text,
            scale,
            LauncherSectionMetrics.PrimaryButtonFontSize,
            LauncherSectionMetrics.PrimaryButtonHeight,
            pressed
        );

    private Button AddSecondaryHiddenButton(string text, float scale, Action pressed)
        => AddHiddenButton(
            text,
            scale,
            LauncherSectionMetrics.SecondaryButtonFontSize,
            LauncherSectionMetrics.SecondaryButtonHeight,
            pressed
        );

    private Button AddHiddenButton(
        string text,
        float scale,
        int fontSize,
        int height,
        Action pressed
    )
    {
        return AddHiddenButton(this, text, scale, fontSize, height, pressed);
    }

    private static Button AddHiddenButton(
        Container parent,
        string text,
        float scale,
        int fontSize,
        int height,
        Action pressed
    )
    {
        var button = new StyledButton(text, scale, fontSize: fontSize, height: height)
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.Visible = false;
        if (pressed != null)
            button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private static HBoxContainer BuildCompactRow(float scale)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride(
            LauncherViewLayoutMetrics.ThemeSeparation,
            LauncherViewLayoutMetrics.ScaleInt(LauncherSectionMetrics.PushPullRowSeparation, scale)
        );
        return row;
    }

    private static VBoxContainer BuildDisclosureGroup(float scale)
    {
        var group = new VBoxContainer { Visible = false };
        group.AddThemeConstantOverride(
            LauncherViewLayoutMetrics.ThemeSeparation,
            LauncherViewLayoutMetrics.ScaleInt(LauncherSectionMetrics.SectionSeparation, scale)
        );
        return group;
    }

    private void ToggleAdvanced()
    {
        _advancedGroup.Visible = !_advancedGroup.Visible;
        _moreButton.Text = _advancedGroup.Visible ? "Fewer options" : "More options";
        if (!_advancedGroup.Visible)
        {
            _diagnosticsGroup.Visible = false;
            _troubleshootingButton.Text = "Troubleshooting";
        }
    }

    private void ToggleDiagnostics()
    {
        _diagnosticsGroup.Visible = !_diagnosticsGroup.Visible;
        _troubleshootingButton.Text = _diagnosticsGroup.Visible
            ? "Hide troubleshooting"
            : "Troubleshooting";
    }

    private void CollapseDisclosures()
    {
        _advancedGroup.Visible = false;
        _diagnosticsGroup.Visible = false;
        _moreButton.Text = "More options";
        _troubleshootingButton.Text = "Troubleshooting";
    }

    private static Button AddPushPullButton(
        HBoxContainer row,
        string text,
        float scale,
        Action pressed
    )
    {
        var button = new StyledButton(
            text,
            scale,
            LauncherSectionMetrics.SecondaryButtonFontSize,
            LauncherSectionMetrics.SecondaryButtonHeight
        );
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (pressed != null)
            button.Pressed += pressed;
        row.AddChild(button);
        return button;
    }

    private void ConfigureToggle(Button button, Func<bool, string> text, Action<bool> toggled)
    {
        button.ToggleMode = true;
        ApplyToggle(button, false, text);
        button.Toggled += pressed =>
        {
            ApplyToggle(button, pressed, text);
            toggled?.Invoke(pressed);
        };
    }

    private void SetToggleChecked(Button button, bool value, Func<bool, string> text)
    {
        button.ButtonPressed = value;
        ApplyToggle(button, value, text);
    }

    private void ApplyToggle(Button button, bool value, Func<bool, string> text)
    {
        button.Text = text(value);
        var style = value ? _toggleOnStyle : _toggleOffStyle;
        button.AddThemeStyleboxOverride("normal", style);
        button.AddThemeStyleboxOverride("hover", style);
        button.AddThemeStyleboxOverride("pressed", style);
        button.AddThemeStyleboxOverride("disabled", style);
    }

    private static string LocalBackupText(bool value) => value ? "Backups: On" : "Backups: Off";

    private static string CloudSyncText(bool value) => value ? "Auto-sync: On" : "Auto-sync: Off";
}
