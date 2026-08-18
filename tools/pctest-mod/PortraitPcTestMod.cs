using System;
using System.IO;
using System.Reflection;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace Sts2PortraitPcTest;

// Entry point invoked by the game's ModManager. Applies the portrait patch
// group to the desktop game, then drives the screenshot schedule from the
// SceneTree.ProcessFrame event. Plain C# event handlers need no Godot script
// bridge, so this works for an assembly the engine never registered as a
// script assembly (custom Node subclasses from mod DLLs get no _Ready/_Process
// dispatch here).
[ModInitializer(nameof(Initialize))]
public static class PortraitPcTestMod
{
    private const double QuitAtSeconds = 240.0;

    private static SceneTree _tree;
    private static Assembly _sts2Mobile;
    private static MethodInfo _portraitApply;
    private static ulong _startTicksMs;
    private static ulong _lastPortraitApplyMs;
    private static bool _quitRequested;
    private static int _step;
    private static double _stepReadyAt;

    public static void Initialize()
    {
        PcTestLog.Write("mod initializer entered");
        try
        {
            _sts2Mobile = LoadSts2Mobile();
            var patches = _sts2Mobile.GetType("STS2Mobile.Portrait.PortraitPatches");
            var apply = patches?.GetMethod(
                "Apply",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (apply is null)
                throw new InvalidOperationException("PortraitPatches.Apply not found");

            // The game loads mod assemblies in their own AssemblyLoadContext, so
            // this mod's HarmonyLib.Harmony type and the one STS2Mobile binds to
            // can be different runtime types. Build the instance from the
            // parameter type Apply actually declares to stay in one context.
            var harmonyType = apply.GetParameters()[0].ParameterType;
            var harmony = Activator.CreateInstance(harmonyType, "sts2.portrait.pctest");
            BridgePatchLogs();
            apply.Invoke(null, new[] { harmony });
            PcTestLog.Write("portrait patch group applied");

            _portraitApply = _sts2Mobile
                .GetType("STS2Mobile.Portrait.PortraitDisplay")
                ?.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"portrait patch apply FAILED: {ex}");
        }

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            PcTestLog.Write("driver install failed: no SceneTree at mod init");
            return;
        }

        _tree = tree;
        _startTicksMs = Time.GetTicksMsec();
        tree.ProcessFrame += Tick;
        PcTestLog.Write("frame driver installed");
    }

    // PatchHelper.Log feeds BootstrapTrace, which is inert outside the Android
    // bootstrap, so on PC every portrait log would vanish. Splice a GD.Print
    // sink into the LogEmitted event so the rig's godot.log carries them.
    private static void BridgePatchLogs()
    {
        try
        {
            var helper = _sts2Mobile.GetType("STS2Mobile.PatchHelper");
            var field = helper?.GetField(
                "LogEmitted",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (field is null)
            {
                PcTestLog.Write("patch log bridge: LogEmitted field not found");
                return;
            }

            Action<string> sink = message => GD.Print($"[STS2M] {message}");
            var current = (Delegate)field.GetValue(null);
            field.SetValue(null, Delegate.Combine(current, sink));
            PcTestLog.Write("patch log bridge installed");
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"patch log bridge FAILED: {ex.Message}");
        }
    }

    // STS2Mobile must land in the SAME AssemblyLoadContext as this mod so its
    // GodotSharp/0Harmony references resolve to the game's already-initialized
    // instances. A stray Assembly.Load/LoadFrom puts it in another context,
    // which drags in a second GodotSharp whose native function table is empty
    // and crashes with an access violation on first use.
    private static Assembly LoadSts2Mobile()
    {
        var self = typeof(PortraitPcTestMod).Assembly;
        var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(self)
            ?? System.Runtime.Loader.AssemblyLoadContext.Default;
        var local = Path.Combine(Path.GetDirectoryName(self.Location) ?? ".", "STS2Mobile.dll");
        return context.LoadFromAssemblyPath(local);
    }

    private static void Tick()
    {
        try
        {
            var elapsedMs = Time.GetTicksMsec() - _startTicksMs;
            var elapsed = elapsedMs / 1000.0;

            EnforceTestWindow();

            // PC belt for the viewport guard: the guard node's _Process does
            // not dispatch for unregistered assemblies, so re-assert the
            // portrait canvas from here once a second instead.
            if (elapsedMs - _lastPortraitApplyMs >= 1000)
            {
                _lastPortraitApplyMs = elapsedMs;
                _portraitApply?.Invoke(null, null);
            }

            RunScenario(elapsed);

            if (!_quitRequested && elapsed >= QuitAtSeconds)
            {
                _quitRequested = true;
                PcTestLog.Write("driver timeout, quitting game");
                _tree.Quit();
            }
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"tick FAILED: {ex.Message}");
        }
    }

    // Scripted walk: capture, synthesize an in-process click, settle, repeat.
    // Coordinates are canvas units read from the previous round's tree dumps;
    // the canvas is pinned (1180x2596), so they are deterministic per game
    // version. All input is Input.ParseInputEvent inside the game process; the
    // user's OS mouse is never touched.
    private static double _stepStartedAt = -1;

    // A step returns true when it did its work; the walk then settles for
    // Settle seconds. Steps that can legitimately not apply (one-time popups,
    // state-dependent buttons) time out after Timeout seconds and are skipped,
    // so the walk never wedges.
    private sealed record Step(string Name, Func<bool> Run, double Settle, double Timeout = 3.0);

    private static Step Click(string name, double settle, double timeout = 6.0, params string[] fallbackNames)
        => new(
            $"click {name}",
            () =>
            {
                if (ClickControlByName(name))
                    return true;
                foreach (var alt in fallbackNames)
                {
                    if (ClickControlByName(alt))
                        return true;
                }
                return false;
            },
            settle,
            timeout
        );

    private static Step Shot(string label, double settle = 0.3)
        => new(
            $"capture {label}",
            () =>
            {
                if (HideMapScreenOverlay())
                    return false; // capture next frame, after the hide renders
                Capture(label);
                return true;
            },
            settle,
            5.0
        );

    private static Step Cmd(string command, double settle)
        => new($"console {command}", () => { Console(command); return true; }, settle);

    // Minimal bisect walk: straight to the merchant with no capstone screens
    // touched first. Selected via STS2_PCTEST_SCENARIO=merchant.
    private static readonly Step[] MerchantProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        Click("SingleplayerButton", 2.0),
        Click("ConfirmButton", 2.5),
        Click("NoButton", 7.0, 4.0),
        Cmd("room shop", 4.0),
        new(
            "open merchant inventory",
            () =>
            {
                var ctx = MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.GetCurrentScreen();
                PcTestLog.Write($"active screen: {ctx?.GetType().Name ?? "null"}");
                if (FindNodeByName(_tree.Root, "MerchantButton") is MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl mb)
                    PcTestLog.Write($"merchant IsEnabled: {mb.IsEnabled}");
                return !HideMapScreenOverlay() && ClickControlByName("MerchantButton");
            },
            1.0
        ),
        new(
            "wait merchant slide-in",
            () =>
            {
                var inv = FindNodeByName(_tree.Root, "Inventory");
                var slots = inv is null ? null : FindNodeByName(inv, "SlotsContainer");
                return slots is Control { Visible: true } landed && landed.GlobalPosition.Y > 0f;
            },
            1.2,
            12.0
        ),
        Shot("m1-merchant-fresh"),
    };

    private static readonly Step[] NeowProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        Click("SingleplayerButton", 2.0),
        Click("ConfirmButton", 2.5),
        Click("NoButton", 7.0, 4.0),
        Cmd("event NEOW", 4.5),
        Shot("n1-neow"),
    };

    // Main-menu compendium family. The Compendium button only exists on the
    // has-a-run menu page, so the probe starts a run, saves and quits, and
    // returns to the menu first. Tab names are guesses backed by timeouts.
    private static readonly Step[] CompendiumProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        // The menu button is gated on NumberOfRuns > 0, which the wiped
        // sandbox profile never has; call the open handler directly.
        new(
            "open compendium submenu",
            () =>
            {
                var menu = FindNodeByName(_tree.Root, "MainMenu");
                var open = menu?.GetType().GetMethod(
                    "OpenCompendiumSubmenu",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    binder: null,
                    types: new[] { typeof(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NButton) },
                    modifiers: null
                );
                if (open is null)
                    return false;
                open.Invoke(menu, new object[] { null });
                return true;
            },
            2.5,
            8.0
        ),
        Shot("c1-compendium"),
        Click("CardLibraryButton", 2.0, 5.0),
        Shot("c2-card-library"),
        Click("Close", 1.5, 5.0, "BackButton"),
        Click("RelicCollectionButton", 2.0, 5.0),
        Shot("c3-relic-collection"),
        Click("Close", 1.5, 5.0, "BackButton"),
        Click("PotionLabButton", 2.0, 5.0),
        Shot("c4-potion-lab"),
    };

    // Potion-use overlay in combat, then the boss room and its rewards.
    private static readonly Step[] PotionsProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        Click("SingleplayerButton", 2.0),
        Click("ConfirmButton", 2.5),
        Click("NoButton", 7.0, 4.0),
        Cmd("potion ENTROPIC_BREW", 0.8),
        Cmd("room monster", 4.0),
        new(
            "click first potion",
            () =>
            {
                foreach (var holder in FindControlsByType(_tree.Root, "PotionHolder"))
                {
                    if (!holder.IsVisibleInTree() || holder.Size.X < 4f)
                        continue;
                    ClickCanvas(
                        holder.GlobalPosition + holder.Size * holder.Scale * 0.5f,
                        $"potion holder {holder.Name}"
                    );
                    return true;
                }
                return false;
            },
            2.0,
            8.0
        ),
        Shot("p1-potion-prompt"),
        Cmd("win", 6.0),
        Cmd("room boss", 5.0),
        Shot("p2-boss-room"),
        Cmd("win", 7.0),
        Shot("p3-boss-rewards"),
    };

    private static Step[] ActiveScenario =>
        System.Environment.GetEnvironmentVariable("STS2_PCTEST_SCENARIO") switch
        {
            "merchant" => MerchantProbeScenario,
            "neow" => NeowProbeScenario,
            "compendium" => CompendiumProbeScenario,
            "potions" => PotionsProbeScenario,
            "relics" => RelicsProbeScenario,
            "extras" => ExtrasProbeScenario,
            "modding" => ModdingProbeScenario,
            "piles" => PilesProbeScenario,
            "wave2" => Wave2ProbeScenario,
            "wave3" => Wave3ProbeScenario,
            _ => Scenario,
        };

    // Act 3 environment and an elite encounter under the same pins.
    private static readonly Step[] Wave3ProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        Click("SingleplayerButton", 2.0),
        Click("ConfirmButton", 2.5),
        Click("NoButton", 7.0, 4.0),
        Cmd("act 3", 5.0),
        Shot("v1-act3-map"),
        Cmd("room elite", 4.5),
        Shot("v2-act3-elite"),
        Cmd("win", 6.0),
        Cmd("room monster", 4.5),
        Shot("v3-act3-combat"),
    };

    // Second character and second act: the Silent's combat layout, then the
    // act 2 environment (map, combat, rest) under the same pins.
    private static readonly Step[] Wave2ProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        Cmd("unlock all", 1.0),
        Click("SingleplayerButton", 2.0),
        Click("SILENT_button", 1.2),
        Click("SILENT_button", 1.2),
        Click("ConfirmButton", 2.0),
        // unlock all opens the ascension panel plus its FTUE popup on the
        // first confirm; dismiss and confirm again to actually start.
        Click("FtueConfirmButton", 1.5, 5.0),
        Click("ConfirmButton", 2.5),
        Click("NoButton", 7.0, 4.0),
        Cmd("room monster", 4.5),
        Shot("w1-silent-combat"),
        Cmd("win", 6.0),
        Cmd("act 2", 5.0),
        Shot("w2-act2-map"),
        Cmd("room monster", 4.5),
        Shot("w3-act2-combat"),
        Cmd("win", 6.0),
        Cmd("room restsite", 3.5),
        Shot("w4-act2-rest"),
    };

    // Draw/discard pile screens in combat, then the boss relic choice via
    // Neow's Lava Rock (the act boss drops two relics to pick from).
    private static readonly Step[] PilesProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        Click("SingleplayerButton", 2.0),
        Click("ConfirmButton", 2.5),
        Click("NoButton", 7.0, 4.0),
        Cmd("relic add LAVA_ROCK", 0.6),
        Cmd("room monster", 4.0),
        Click("DrawPile", 2.0, 6.0),
        Shot("q1-draw-pile"),
        Click("Close", 1.5, 5.0, "BackButton"),
        Click("DiscardPile", 2.0, 6.0),
        Shot("q2-discard-pile"),
        Click("Close", 1.5, 5.0, "BackButton"),
        Cmd("win", 6.0),
        Cmd("room boss", 5.0),
        Cmd("win", 8.0),
        Shot("q3-boss-relic-choice"),
    };

    // Last unexplored menu screens: the timeline (handler-invoked like the
    // compendium) and the modding screen inside settings.
    private static readonly Step[] ExtrasProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        new(
            "open timeline screen",
            () =>
            {
                var menu = FindNodeByName(_tree.Root, "MainMenu");
                var open = menu?.GetType().GetMethod(
                    "OpenTimelineScreen",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    binder: null,
                    types: new[] { typeof(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NButton) },
                    modifiers: null
                );
                if (open is null)
                    return false;
                open.Invoke(menu, new object[] { null });
                return true;
            },
            3.0,
            8.0
        ),
        Shot("x1-timeline"),
    };

    // Modding screen lives inside settings; separate walk because the
    // timeline has no Close control to return from.
    private static readonly Step[] ModdingProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        Click("SettingsButton", 2.0),
        Click("ModdingButton", 2.0, 6.0),
        Shot("x2-modding"),
    };

    // Many-relic bar stress: unknown ids fail loudly in the log and are
    // simply skipped, so the belt ends up with however many stick.
    private static readonly Step[] RelicsProbeScenario =
    {
        new(
            "wait main menu",
            () => FindNodeByName(_tree.Root, "MainMenu") is Control { Visible: true },
            0.4,
            25.0
        ),
        Click("SingleplayerButton", 2.0),
        Click("ConfirmButton", 2.5),
        Click("NoButton", 7.0, 4.0),
        new(
            "stack relics",
            () =>
            {
                foreach (var id in new[]
                {
                    "AKABEKO", "ANCHOR", "BAG_OF_MARBLES", "BAG_OF_PREPARATION",
                    "BRONZE_SCALES", "VAJRA", "LANTERN", "HAPPY_FLOWER",
                    "PEN_NIB", "ORICHALCUM", "CENTENNIAL_PUZZLE", "OLD_COIN",
                    "SMILING_MASK", "TOUGH_BANDAGES", "MEAT_ON_THE_BONE", "BLOOD_VIAL",
                })
                    Console($"relic add {id}");
                return true;
            },
            2.0
        ),
        Shot("r1-map-manyrelics"),
        Cmd("event BYRDONIS_NEST", 4.0),
        Shot("r2-event-manyrelics"),
        Cmd("room monster", 4.0),
        Shot("r3-combat-manyrelics"),
    };

    private static readonly Step[] Scenario =
    {
        new(
            "wait main menu",
            () =>
            {
                if (FindNodeByName(_tree.Root, "MainMenu") is not Control { Visible: true })
                    return false;
                Capture("01-main-menu");
                return true;
            },
            0.4,
            25.0
        ),
        Click("SingleplayerButton", 2.0),
        Shot("02-character-select"),
        Click("ConfirmButton", 2.5),
        Shot("03-after-confirm"),
        // One-time tutorial popup; absent on profiles that already answered.
        Click("NoButton", 7.0, 4.0),
        Shot("04-run-start", 5.0),
        Shot("05-map"),
        new(
            "relics + reported event",
            () =>
            {
                // Passive-pickup relics only: ASTROLABE opens a transform
                // overlay on pickup and hijacks the rest of the walk.
                Console("relic add AKABEKO");
                Console("relic add ANCHOR");
                Console("relic add BAG_OF_MARBLES");
                Console("relic add BAG_OF_PREPARATION");
                Console("event BYRDONIS_NEST");
                return true;
            },
            3.5
        ),
        Shot("06-event-byrdonis"),
        Cmd("room restsite", 3.5),
        Shot("07-rest-site"),
        new(
            "open smith upgrade select",
            () =>
            {
                // Second campfire card is Smith; the buttons carry no
                // distinct names, so resolve by type and order.
                Control smith = null;
                var seen = 0;
                foreach (var button in FindControlsByType(_tree.Root, "NRestSiteButton"))
                {
                    if (!button.IsVisibleInTree())
                        continue;
                    seen++;
                    if (seen == 2)
                    {
                        smith = button;
                        break;
                    }
                }
                if (smith is null)
                    return false;
                ClickCanvas(smith.GlobalPosition + smith.Size * smith.Scale * 0.5f, "Smith card");
                return true;
            },
            3.0,
            6.0
        ),
        Shot("07b-upgrade-select"),
        Click("Close", 1.5, 6.0, "BackButton"),
        Cmd("room shop", 3.5),
        Shot("08-shop"),
        Cmd("room treasure", 3.5),
        Shot("09-treasure"),
        Cmd("room monster", 6.0),
        Shot("10-combat"),
        Cmd("win", 6.0),
        Shot("11-rewards"),
        Click("Deck", 2.5),
        Shot("12-deck-view"),
        Click("BackButton", 2.0),
        Click("PauseButton", 2.5, 6.0, "Pause"),
        Shot("13-pause"),
        Click("SettingsButton", 2.5, 6.0, "Settings"),
        Shot("14-settings-body"),
        Click("BackButton", 1.5),
        Click("ResumeButton", 1.5, 3.0, "Resume", "BackButton"),
        Cmd("room shop", 3.5),
        new(
            "open merchant inventory",
            () =>
            {
                if (_tree.Paused)
                {
                    PcTestLog.Write("WARN tree still paused before merchant click; unpausing");
                    _tree.Paused = false;
                }
                return !HideMapScreenOverlay() && ClickControlByName("MerchantButton");
            },
            1.0
        ),
        // The click first walks the character to the merchant, then the
        // inventory slides down from above the canvas; a fixed settle is
        // not enough on far spawns, so wait for the mat to actually land.
        new(
            "wait merchant slide-in",
            () =>
            {
                // Resume leaves the tree paused in some rounds (walks and the
                // inventory slide never run); measure it loudly and heal so
                // the rest of the walk still produces evidence.
                if (_tree.Paused)
                {
                    PcTestLog.Write("WARN tree still paused after Resume; unpausing");
                    _tree.Paused = false;
                    return false;
                }
                var inv = FindNodeByName(_tree.Root, "Inventory");
                var slots = inv is null ? null : FindNodeByName(inv, "SlotsContainer");
                return slots is Control { Visible: true } landed && landed.GlobalPosition.Y > 0f;
            },
            1.2,
            12.0
        ),
        Shot("15-shop-inventory"),
        Cmd("event NEOW", 3.5),
        Shot("16-neow"),
        Cmd("die", 6.0),
        Shot("17-death", 0.1),
    };

    private static void RunScenario(double elapsed)
    {
        if (elapsed < _stepReadyAt)
            return;

        if (_step >= ActiveScenario.Length)
        {
            if (!_quitRequested)
            {
                _quitRequested = true;
                PcTestLog.Write("scenario complete, quitting game");
                _tree.Quit();
            }
            return;
        }

        if (_stepStartedAt < 0)
            _stepStartedAt = elapsed;

        var step = ActiveScenario[_step];
        if (elapsed - _stepStartedAt > step.Timeout)
        {
            PcTestLog.Write($"step {_step} ({step.Name}) timed out, skipping");
            Advance(elapsed, 0.1);
            return;
        }

        if (step.Run())
            Advance(elapsed, step.Settle);
    }

    private static void Advance(double elapsed, double settleSeconds)
    {
        _step++;
        _stepReadyAt = elapsed + settleSeconds;
        _stepStartedAt = -1;
    }

    // Click a control's live center so layout shifts never break the walk.
    // Several screens keep same-named controls parked off-canvas (inactive
    // submenus), so prefer a visible match whose center is inside the canvas.
    private static bool ClickControlByName(string name)
    {
        var canvas = (Vector2)_tree.Root.ContentScaleSize;
        Control fallback = null;

        foreach (var control in FindControlsByName(_tree.Root, name))
        {
            if (!control.IsVisibleInTree())
                continue;
            fallback ??= control;
            var center = control.GlobalPosition + control.Size * 0.5f;
            if (center.X >= 0 && center.X <= canvas.X && center.Y >= 0 && center.Y <= canvas.Y)
            {
                ClickCanvas(center, name);
                return true;
            }
        }

        if (fallback is not null)
        {
            ClickCanvas(fallback.GlobalPosition + fallback.Size * 0.5f, $"{name} (off-canvas fallback)");
            return true;
        }

        return false;
    }

    private static MegaCrit.Sts2.Core.DevConsole.DevConsole _console;

    // Clicking a map point closes the map screen; console teleports bypass
    // that, leaving the map drawn over the entered room. Rig-only cleanup so
    // captures show the room the way normal navigation would. Returns true
    // when it hid something this frame: the viewport texture only reflects it
    // on the NEXT rendered frame, so the caller must capture one tick later.
    private static bool HideMapScreenOverlay()
    {
        // Console room/event jumps leave the map screen OPEN over the new
        // room. Setting Visible=false only hides pixels: the screen still
        // owns ActiveScreenContext and keeps CombatManager paused, which
        // disables room controls (the merchant button ignored clicks for
        // exactly this reason). Close it through the game's own teardown.
        // When the map IS the current room, leave it alone.
        if (FindNodeByName(_tree.Root, "MapRoom") is Control { Visible: true })
            return false;
        if (FindNodeByName(_tree.Root, "MapScreen") is not Control map)
            return false;
        var isOpen = map.GetType().GetProperty("IsOpen")?.GetValue(map) as bool? ?? map.Visible;
        if (!isOpen && !map.Visible)
            return false;
        if (isOpen && map.GetType().GetMethod("Close") is { } close)
        {
            close.Invoke(map, new object[] { false });
            PcTestLog.Write("map screen closed via game path for capture");
        }
        if (map.Visible)
            map.Visible = false;
        return true;
    }

    // The game's own dev-console commands (event/room/fight/...) are plain
    // classes; driving them directly needs no console UI and no keybind.
    private static void Console(string command)
    {
        try
        {
            _console ??= new MegaCrit.Sts2.Core.DevConsole.DevConsole(shouldAllowDebugCommands: true);
            var result = _console.ProcessCommand(command);
            PcTestLog.Write($"console '{command}' -> success={result.success} msg={result.msg}");
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"console '{command}' FAILED: {ex.Message}");
        }
    }

    // The entry map node is the bottom-most reachable point; its position is
    // seed-dependent, so resolve it live by script-class name.
    private static bool ClickBottomMapPoint()
    {
        var canvas = (Vector2)_tree.Root.ContentScaleSize;
        Control best = null;
        var bestY = float.MinValue;

        foreach (var control in FindControlsByType(_tree.Root, "MapPoint"))
        {
            if (!control.IsVisibleInTree())
                continue;
            var center = control.GlobalPosition + control.Size * 0.5f;
            if (center.X < 0 || center.X > canvas.X || center.Y < 0 || center.Y > canvas.Y)
                continue;
            if (center.Y > bestY)
            {
                bestY = center.Y;
                best = control;
            }
        }

        if (best is null)
            return false;

        ClickCanvas(best.GlobalPosition + best.Size * 0.5f, $"map point {best.GetType().Name}");
        return true;
    }

    private static System.Collections.Generic.IEnumerable<Control> FindControlsByType(Node root, string typeNameContains)
    {
        if (root is Control control && root.GetType().Name.Contains(typeNameContains))
            yield return control;
        foreach (var child in root.GetChildren())
        {
            foreach (var found in FindControlsByType(child, typeNameContains))
                yield return found;
        }
    }

    private static System.Collections.Generic.IEnumerable<Control> FindControlsByName(Node root, string name)
    {
        if (root is Control control && root.Name == name)
            yield return control;
        foreach (var child in root.GetChildren())
        {
            foreach (var found in FindControlsByName(child, name))
                yield return found;
        }
    }

    private static void ClickCanvas(Vector2 canvasPos, string label)
    {
        var windowSize = (Vector2)DisplayServer.WindowGetSize();
        var canvasSize = (Vector2)_tree.Root.ContentScaleSize;
        var pos = canvasPos * (windowSize / canvasSize);

        var motion = new InputEventMouseMotion { Position = pos, GlobalPosition = pos };
        Input.ParseInputEvent(motion);
        var down = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = pos,
            GlobalPosition = pos,
        };
        var up = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = pos,
            GlobalPosition = pos,
        };
        Input.ParseInputEvent(down);
        Input.ParseInputEvent(up);
        PcTestLog.Write($"clicked {label} at canvas {canvasPos.X:F0},{canvasPos.Y:F0} window {pos.X:F0},{pos.Y:F0}");
    }

    private static Node FindNodeByName(Node root, string name)
    {
        if (root.Name == name)
            return root;
        foreach (var child in root.GetChildren())
        {
            if (FindNodeByName(child, name) is { } found)
                return found;
        }
        return null;
    }

    private static void EnforceTestWindow()
    {
        // The game re-applies its saved desktop display settings after boot,
        // which overrides the command-line resolution and re-centers the
        // window onto the visible desktop. Pin the phone-shaped window and the
        // off-screen position for the whole run so the rig never depends on
        // saved settings and never surfaces a window on the user's desktop.
        if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        if (DisplayServer.WindowGetSize() != PcTestConfig.WindowSize)
            DisplayServer.WindowSetSize(PcTestConfig.WindowSize);
        if (DisplayServer.WindowGetPosition() != PcTestConfig.WindowPosition)
            DisplayServer.WindowSetPosition(PcTestConfig.WindowPosition);
    }

    private static void Capture(string label)
    {
        try
        {
            var image = _tree.Root.GetTexture().GetImage();
            Directory.CreateDirectory(PcTestLog.OutDir);
            image.SavePng(Path.Combine(PcTestLog.OutDir, $"{label}.png"));

            var window = (Vector2)DisplayServer.WindowGetSize();
            var canvas = _tree.Root.ContentScaleSize;
            var scene = _tree.CurrentScene;
            PcTestLog.Write(
                $"captured {label}: window={window.X:F0}x{window.Y:F0} canvas={canvas.X}x{canvas.Y} scene={(scene is null ? "(none)" : scene.Name.ToString())}"
            );
            DumpTree(label);
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"capture {label} FAILED: {ex.Message}");
        }
    }

    // Shallow tree dump: enough to learn screen structure and pick node paths
    // for the next driver iteration, without megabyte logs.
    private static void DumpTree(string label)
    {
        try
        {
            var sb = new StringBuilder();
            Dump(_tree.Root, sb, 0, DumpDepth);
            File.WriteAllText(Path.Combine(PcTestLog.OutDir, $"{label}-tree.txt"), sb.ToString());
        }
        catch (Exception ex)
        {
            PcTestLog.Write($"tree dump {label} FAILED: {ex.Message}");
        }
    }

    private const int DumpDepth = 12;

    private static void Dump(Node node, StringBuilder sb, int depth, int maxDepth)
    {
        sb.Append(new string(' ', depth * 2));
        sb.Append(node.Name).Append(" : ").Append(node.GetType().Name);
        if (node is Control control)
            sb.Append($"  pos={control.GlobalPosition.X:F0},{control.GlobalPosition.Y:F0} size={control.Size.X:F0}x{control.Size.Y:F0} visible={control.Visible}");
        sb.AppendLine();

        if (depth >= maxDepth)
            return;
        // includeInternal: container-spawned rows (event options, dialogue)
        // can be internal children; the default enumeration hides them.
        foreach (var child in node.GetChildren(includeInternal: true))
            Dump(child, sb, depth + 1, maxDepth);
    }
}

internal static class PcTestConfig
{
    internal static readonly Vector2I WindowSize = ReadVector("STS2_PCTEST_WINDOW", 'x', new Vector2I(1298, 2856));
    internal static readonly Vector2I WindowPosition = ReadVector("STS2_PCTEST_POSITION", ',', new Vector2I(10020, 60));

    private static Vector2I ReadVector(string env, char separator, Vector2I fallback)
    {
        var raw = System.Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        var parts = raw.Split(separator);
        return parts.Length == 2
            && int.TryParse(parts[0], out var x)
            && int.TryParse(parts[1], out var y)
            ? new Vector2I(x, y)
            : fallback;
    }
}

// File log the harness can tail; GD.Print also goes to the game's log.
internal static class PcTestLog
{
    internal static string OutDir =>
        System.Environment.GetEnvironmentVariable("STS2_PCTEST_OUT")
        ?? ProjectSettings.GlobalizePath("user://pctest-shots");

    internal static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        GD.Print($"[PcTest] {message}");
        try
        {
            Directory.CreateDirectory(OutDir);
            File.AppendAllText(Path.Combine(OutDir, "pctest-log.txt"), line + System.Environment.NewLine);
        }
        catch
        {
            // Logging must never take the game down.
        }
    }
}
