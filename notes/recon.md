# Faz 0 Recon Notları

## Oyun kopyası
- Yol: `/home/whispersgone/Downloads/Slay-the-Spire-2-AnkerGames/Slay the Spire 2/`
- Sürüm: **v0.107.1** (commit 59260271, 2026-06-18) — Major Update 2, Workshop destekli sürüm
- DİKKAT: klasörde crack dosyaları var (`OnlineFix64.dll`, `OnlineFix.ini`, `winmm.dll`, `SteamOverlay64.dll`) — Android'e KOPYALANMAYACAK

## project.godot (pck'den GDRE ile çıkarıldı → notes/pck-project/)
- Tasarım çözünürlüğü: **1920×1080**
- Stretch: **mode=`canvas_items`, aspect=`expand`** ← KRİTİK: dikey pencerede letterbox yok,
  canvas dikeyde büyür (9:16'da ~1920×3413 sanal canvas). Portrait mod = yerleşim işi, render işi değil.
- `config/features`: "4.5", "C#", "Mobile"
- Ana sahne: `res://scenes/game.tscn`
- Autoload: SentryInit, FmodManager (GDScript autoload'ları — oyun logic'i C#'ta)
- Physics: Dummy (2D/3D fizik motoru yok)
- user dir: `SlayTheSpire2` (custom_user_dir)
- Windows renderer: d3d12 default (`rendering_device/driver.windows`)

## data_sts2_windows_x86_64/
- `sts2.dll` — tüm oyun mantığı; `GodotSharp.dll`; **`0Harmony.dll` OYUNLA GELİYOR** (resmi mod
  loader Harmony kullanıyor — modun Harmony bundle etmesi gerekmeyebilir); MonoMod.* da var
- .NET 9 BCL komple gemide (self-contained)

## Ekyso patch'lerinden öğrenilen sınıf/nodepath'ler (vendor/StS2-Launcher)
- `MegaCrit.Sts2.Core.Nodes.NGame` (ana giriş), `NGame.InitializePlatform` (Steam/Sentry init —
  Ekyso bunu prefix-skip ediyor)
- `MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu` — `_Ready`, `OnWindowChange(bool isAspectRatioAuto)`,
  node'lar: `%MainMenuTextButtons`, `%MainMenuBg` (BgContainer 2560×1200), `%Logo`
- `MegaCrit.Sts2.Core.Nodes.CommonUi.NGlobalUi` — `OnWindowChange`, `_window` field
- `MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsScreen`, `NResolutionDropdown`,
  `NResolutionDropdownItem` (`%GraphicsSettings`, `WindowedResolution` node'ları)
- `MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.AspectRatioSetting`
  (`MegaCrit.Sts2.Core.Settings.AspectRatioSetting.Auto` enum'u var — oyunda aspect ratio ayarı ZATEN mevcut)
- Ekyso UI scale mantığı: `Window.ContentScaleAspect = Expand` + `ContentScaleSize = (1680/scale, 1080/scale)`
  → aynı mekanizma portrait için kullanılabilir (ContentScaleSize'ı dikey orana göre ayarla)

## Araçlar
- .NET 9 SDK: `~/.dotnet` (PATH'e ekle: `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$HOME/.dotnet/tools:$PATH`)
- ilspycmd 9.1.0.7988 (çalıştırırken `DOTNET_ROLL_FORWARD=LatestMajor` gerekli)
- gdsdecomp v2.6.0: `~/sts2-portrait/tools/gdre_tools.x86_64` (--headless CLI)
- Decompile çıktısı: `notes/sts2-src/` (repoya GİRMEZ — .gitignore'da)

## PC'de çalıştırma (ÇÖZÜLDÜ)
- Plain system wine ÇALIŞMIYOR: OnlineFix crack `RegOpenKeyExW failed` (Steam registry yok) → süreç ölüyor.
- **GE-Proton11-1 ile SORUNSUZ çalışıyor.** Script: `scripts/run-proton.sh`
  - İzole prefix: `~/sts2-portrait/proton-prefix/` (git-ignore)
  - `STEAM_COMPAT_CLIENT_INSTALL_PATH=~/.steam/steam`, `STEAM_COMPAT_DATA_PATH=<prefix>`
  - `WINEDLLOVERRIDES=winmm=n,b` (OnlineFix proxy), `PROTON_LOG=1`
  - Pencere 3sn'de açıldı, Vulkan (winevulkan) ile 2560x1440 render, ana menüye ulaştı.
  - crashpad "Connection reset by peer" logu ZARARSIZ (crash reporter soketi).
- Ekran görüntüsü: `import -window <WID>` (ImageMagick) çalışıyor; WID = `xdotool search --name "Slay the Spire"`.
- Mevcut Proton'lar: GE-Proton11-1, cachyos-11.0-20260702-slr, Proton Experimental/Hotfix.

## Disk
- /home'da ~7.5 GB boş — pck'yi komple recover ETME, seçici --include kullan
- Proton prefix ~ birkaç yüz MB
