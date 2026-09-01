using System;
using Godot;

namespace STS2Mobile.Portrait;

// The game's clickable controls accept a press only while they believe the
// pointer is over them, and that belief is updated exclusively by mouse
// motion. A finger produces no motion before it lands, so the first tap on
// any button only moved the invisible emulated cursor there and looked dead,
// and after lifting the finger the cursor stayed parked on the control,
// leaving hovered or pressed visuals stuck. Both device complaints in one:
// "presses do not register" and "holds stay stuck".
//
// This layer watches raw touches at the window and repairs the pointer
// model around them: before a touch press it walks the emulated cursor onto
// the touch point, so hover exists by the time the press arrives, and after
// the touch ends it parks the cursor outside the canvas, so nothing keeps
// hover after the finger is gone. Between those moments the engine's own
// touch-to-mouse emulation runs untouched, which keeps drags and the
// long-press mechanics working.
internal static class PortraitTouchInput
{
    private static readonly Vector2 ParkPosition = new(-4096f, -4096f);
    private static bool _installed;
    private static Window _window;

    internal static void Install()
    {
        if (_installed || !OperatingSystem.IsAndroid())
            return;

        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root is null)
        {
            PatchHelper.Log("[Touch] Scene tree not ready; pointer bridge skipped");
            return;
        }

        _window = tree.Root;
        _window.WindowInput += OnWindowInput;
        _installed = true;
        PatchHelper.Log("[Touch] Pointer bridge installed");
        PortraitFrameBudget.StartMonitor(tree);
        PortraitTouchPass.StartMonitor(tree);
        PortraitDisplay.StartAspectGuard(tree);
    }

    // Pinch-to-zoom on the map: the game has no touch zoom, and the portrait
    // map is a tall dense column where node icons sit near the touch floor.
    // Two fingers down while NMapScreen is visible open a pinch session
    // scaled off the starting distance; either finger lifting ends it.
    private static Vector2? _finger0;
    private static Vector2? _finger1;
    private static bool _pinching;
    private static float _pinchStartDistance = 1f;
    private static float _pinchStartZoom = 1f;
    private static Vector2 _pinchMid;

    private static void OnWindowInput(InputEvent inputEvent)
    {
        switch (inputEvent)
        {
            case InputEventScreenTouch touch:
                if (touch.Index == 0)
                {
                    _finger0 = touch.Pressed ? touch.Position : null;
                    if (touch.Pressed)
                        MovePointer(touch.Position);
                    else
                        Callable.From(ParkPointer).CallDeferred();
                }
                else if (touch.Index == 1)
                {
                    _finger1 = touch.Pressed ? touch.Position : null;
                }
                UpdatePinchState();
                break;
            case InputEventScreenDrag drag:
                if (drag.Index == 0)
                    _finger0 = drag.Position;
                else if (drag.Index == 1)
                    _finger1 = drag.Position;
                if (_pinching && _finger0 is { } a && _finger1 is { } b)
                {
                    var distance = Math.Max(a.DistanceTo(b), 1f);
                    PortraitMap.SetZoom(_pinchStartZoom * distance / _pinchStartDistance);
                    // Two-finger drift pans; a single finger stays the game's
                    // own gesture (the quill draws with it).
                    var mid = (a + b) * 0.5f;
                    PortraitMap.PanX(mid.X - _pinchMid.X);
                    _pinchMid = mid;
                }
                break;
        }
    }

    private static void UpdatePinchState()
    {
        if (_finger0 is { } a && _finger1 is { } b)
        {
            if (_pinching || PortraitMap.VisibleMap() is null)
                return;
            _pinching = true;
            _pinchStartDistance = Math.Max(a.DistanceTo(b), 1f);
            _pinchStartZoom = PortraitMap.Zoom;
            _pinchMid = (a + b) * 0.5f;
            PortraitMap.BeginPinch(_pinchMid);
            return;
        }
        _pinching = false;
    }

    private static void MovePointer(Vector2 position)
    {
        if (!GodotObject.IsInstanceValid(_window))
            return;

        var motion = new InputEventMouseMotion
        {
            Position = position,
            GlobalPosition = position,
        };
        _window.PushInput(motion);
    }

    // Deferred so the release lands on the control first, at the position the
    // finger actually lifted from.
    private static void ParkPointer() => MovePointer(ParkPosition);
}
