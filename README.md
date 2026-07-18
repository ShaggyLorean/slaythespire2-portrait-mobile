# Slay the Spire 2 Portrait Mobile

A portrait mod that turns Slay the Spire 2 into a phone game.

The whole interface is re-laid for vertical screens: a two row HUD that stays clear
of punch hole cameras, a full screen shop, bigger cards, buttons that stay on
screen, and a launcher that installs your own game files and then boots straight
into the game.

**Jump to:**
[Install](#install) ·
[First run](#first-run) ·
[Logs](#logs) ·
[Building the APK yourself](#building-the-apk-yourself) ·
[How it works](#how-it-works) ·
[Planned](#planned) ·
[Troubleshooting](#troubleshooting) ·
[Credits](#credits)

## What you need

- Your own copy of Slay the Spire 2 on PC. Nothing from the game ships in this
  repo or in the APK.
- An arm64 Android phone. Tested on a OnePlus 13 running Android 16.

## Install

1. Download the newest `SlayTheSpire2-Portrait.apk` from Releases and install it.
2. On your PC, open your Slay the Spire 2 install folder. Copy
   `SlayTheSpire2.pck` and the whole `data_sts2_windows_x86_64` folder into
   `/StS2LauncherMM/` in the phone storage root.
3. Open the app. Allow file access when it asks, then tap INSTALL.
4. Tap RESTART when the copy finishes.

## First run

The first start has to build shaders once. The button says BUILD SHADERS, the
build takes a few minutes and drops you back at the launcher. After that the
button says PLAY and goes straight into the game.

## Logs

The launcher has a logs button that opens the current log with a copy button.
If something breaks, that text is exactly what an issue needs.

## Building the APK yourself

The APK is assembled by `build.py` from two things you provide: a community StS2
mobile launcher APK (the engine runtime) and your own game folder.

```sh
git clone https://github.com/ShaggyLorean/slaythespire2-portrait-mobile.git
cd slaythespire2-portrait-mobile
tools/fetch-tools.sh
python3 build.py --launcher-apk /path/to/StS2Launcher.apk --game-dir "/path/to/Slay the Spire 2"
```

Needs Python 3, Java 11 or newer, and the .NET 9 SDK. The result lands in
`dist/SlayTheSpire2-Portrait.apk`.

## How it works

The game is Godot 4 with C#. The mod itself is a set of Harmony patches over the
game's own UI: a portrait content scale, re-layouts for combat, shop, map and the
top bar, and touch input fixes. Sizes are derived from the screen, not hardcoded,
so different phones end up with the same relative layout.

On Android the app wraps a community launcher runtime (see credits), replaces its
interface with the launcher you see, and injects the portrait patches at boot.
Early access updates can and will break patches from time to time. When that
happens, grab the updated release.

## Planned

- Steam sign in from the launcher, so the phone can download the game and sync
  saves by itself. The button is already in there, treat it as experimental.
- Achievements and cloud saves through that sign in.
- Pinch zoom on the battlefield for smaller phones.

## Troubleshooting

- Black screen after a game update: the patches need a rebuild for the new game
  version, check Releases.
- Stuck on the install screen: make sure the pck and the data folder are directly
  inside `/StS2LauncherMM/`, not in a subfolder.
- Layout looks wrong on your phone: open an issue with a screenshot and the text
  from the logs button.

## Credits

- [Ekyso/StS2-Launcher](https://github.com/Ekyso/StS2-Launcher). The Android
  runtime this app is built on (MIT): the engine build, the .NET bootstrap and
  the install logic all come from that project.
- [darthalex2014/StS2-Launcher](https://github.com/darthalex2014/StS2-Launcher).
  The offline install flow used as the base APK.
- Mega Crit made Slay the Spire 2. This project is not affiliated with them,
  contains none of their assets, and is useless without your own copy of the
  game. Go buy it.
