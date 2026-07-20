using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

internal sealed class OfflineImportSection : VBoxContainer
{
    internal event Action ImportPressed;
    internal event Action BackPressed;

    private readonly Button _importButton;
    private readonly Button _backButton;
    private readonly ProgressBar _progress;
    private readonly Label _progressLabel;

    internal OfflineImportSection(float scale)
    {
        Visible = false;
        AddThemeConstantOverride(
            LauncherViewLayoutMetrics.ThemeSeparation,
            LauncherViewLayoutMetrics.ScaleInt(12, scale)
        );

        var instructions = new StyledLabel(
            "On this phone, create:\n/StS2Portrait/Offline/\n\nCopy SlayTheSpire2.pck and the complete data_sts2_windows_x86_64 folder there from your own PC installation.",
            scale,
            fontSize: 15,
            align: HorizontalAlignment.Left
        );
        instructions.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(instructions);

        _progress = new StyledProgressBar(scale) { Visible = false };
        AddChild(_progress);

        _progressLabel = new StyledLabel("", scale, fontSize: 13) { Visible = false };
        _progressLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_progressLabel);

        _importButton = new StyledButton("Import game files", scale, fontSize: 18, height: 64);
        _importButton.Pressed += () => ImportPressed?.Invoke();
        AddChild(_importButton);

        _backButton = new StyledButton("Back", scale, fontSize: 14, height: 54);
        _backButton.Pressed += () => BackPressed?.Invoke();
        AddChild(_backButton);
    }

    internal void BeginImport()
    {
        _importButton.Disabled = true;
        _backButton.Disabled = true;
        _progress.Visible = true;
        _progress.Value = 0;
        _progressLabel.Visible = true;
        _progressLabel.Text = "Validating local files...";
    }

    internal void ShowProgress(OfflineGameImporter.ImportProgress progress)
    {
        _importButton.Disabled = true;
        _backButton.Disabled = true;
        _progress.Visible = true;
        _progress.Value = progress.Percentage;
        _progressLabel.Visible = true;
        _progressLabel.Text =
            $"{LauncherGameFiles.FormatSize(progress.CopiedBytes)} / "
            + $"{LauncherGameFiles.FormatSize(progress.TotalBytes)} — {progress.CurrentFile}";
    }

    internal void Reset()
    {
        _importButton.Disabled = false;
        _backButton.Disabled = false;
        _progress.Visible = false;
        _progressLabel.Visible = false;
        _progress.Value = 0;
    }
}
