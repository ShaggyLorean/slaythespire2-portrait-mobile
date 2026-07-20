using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Godot;
using STS2Mobile.Patches;
using STS2Mobile.Portrait;

namespace STS2Mobile.Launcher;

// Thin wrapper Control that initializes the MVC launcher components and
// processes a main-thread action queue so SteamKit callbacks can update the UI.
internal sealed class LauncherUI : Control
{
    // Launcher dimensions are authored as logical mobile units. Using the old
    // 1080px desktop reference made 14-22px text and 56-72px buttons physically
    // tiny on high-density phones. A ~440-unit short edge produces familiar
    // phone-sized type and touch targets while the portrait game canvas remains
    // independent from the launcher layout.
    private const float ReferenceShortEdge = 440f;
    private const int LauncherZIndex = 100;
    private static readonly Vector2 DefaultViewportSize = new(1920, 1080);

    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private LauncherModel _model;
    private LauncherView _view;
    private LauncherController _controller;
    private bool _inGameMode;
    private bool _buildAttempted;
    private readonly TaskCompletionSource<bool> _launcherReady = new();

    internal void Initialize()
    {
        ZIndex = LauncherZIndex;
        SetAnchorsPreset(LayoutPreset.FullRect);

        var tree = GetTree();
        tree.AutoAcceptQuit = false;
        tree.ProcessFrame += OnProcessFrame;
        TreeExiting += OnExitTree;

        TryBuildUI();
    }

    private void TryBuildUI()
    {
        if (_buildAttempted)
            return;

        // Android can report the old landscape surface for a few frames while
        // honoring the portrait request. Building against that transient size
        // permanently leaves the launcher tiny and letterboxed, so wait until
        // PortraitDisplay has installed the portrait canvas.
        if (OperatingSystem.IsAndroid() && !PortraitDisplay.Apply())
            return;

        _buildAttempted = true;

        try
        {
            PortraitDisplay.Apply();
            var viewportSize = GetViewportSize();
            Size = viewportSize;
            var scale = Math.Clamp(
                Math.Min(viewportSize.X, viewportSize.Y) / ReferenceShortEdge,
                2.0f,
                2.8f
            );
            _model = new LauncherModel(OS.GetDataDir());
            _model.InGameMode = _inGameMode;
            _view = new LauncherView(this, scale);
            _controller = new LauncherController(
                _model,
                _view,
                EnqueueMainThreadAction
            );

            PatchHelper.Log($"LauncherUI initialized. Viewport={viewportSize}");
            _controller.Start();
            _launcherReady.TrySetResult(true);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"BuildUI FAILED: {ex}");
            _launcherReady.TrySetException(ex);
            return;
        }
    }

    internal void SetGameMode(bool inGameMode) => _inGameMode = inGameMode;

    internal async Task WaitForLaunch()
    {
        await _launcherReady.Task;
        await _model.WaitForLaunch();
    }

    private void OnProcessFrame()
    {
        TryBuildUI();
        DrainMainThreadActions();
        _view?.UpdateKeyboardOffset();
    }

    private void EnqueueMainThreadAction(Action action) => _mainThreadActions.Enqueue(action);

    private void DrainMainThreadActions()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"UI update error: {ex.Message}");
            }
        }
    }

    private void OnExitTree()
    {
        var tree = GetTree();
        tree.ProcessFrame -= OnProcessFrame;
        tree.AutoAcceptQuit = true;
        _model?.Dispose();
    }

    private Vector2 GetViewportSize()
        => GetViewport()?.GetVisibleRect().Size ?? DefaultViewportSize;
}
