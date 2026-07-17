# sts2-portrait — Durum

Portrait (dikey) Slay the Spire 2 modu. balatro-portrait-mobile felsefesi StS2'ye taşınıyor.
PC-first geliştirme (GE-Proton), sonra Android launcher'a (Ekyso/NoSteam) taşınacak.

## ✅ Faz 0 — Recon & tooling (TAMAM)
- Oyun v0.107.1, Godot 4.5.1 + C#/.NET 9, tek assembly `sts2.dll` (ILSpy → `notes/sts2-src/`).
- Tasarım res 1920×1080, stretch canvas_items/expand → portrait = yerleşim işi.
- Resmi mod loader: `<id>.dll` + `<id>.json` manifest, `[ModInitializer]`. `0Harmony.dll` oyunla geliyor.
- **PC çalıştırma:** plain wine crack'te ölüyor; **GE-Proton11-1 sorunsuz** (`scripts/run-proton.sh`, izole prefix).

## ✅ Dev altyapı (TAMAM — projenin hızlandırıcısı)
İki bağımsız kanal, ikisi de X/odak/pencere GEREKTİRMEZ:
1. **Bridge** (`src/.../Diagnostics/Bridge.cs`): TAMAMEN STATİK, `SceneTree.ProcessFrame` sinyaline bağlı.
   - Screenshot: `GetViewport().GetTexture().SavePng()` — oyunun içinden, pencere gizli/başka workspace olsa bile.
   - Input: `Input.ParseInputEvent()` (click/drag/key) — gerçek event, Godot kabul eder.
   - Node dökümü: `NodeInspector.DumpTree` (DOM inspector karşılığı).
   - Kanal: `user://bridge/` (cmd/done/*.png). Yardımcı: `scripts/lib.sh` → `bcmd`, `bshot`.
   - **KRİTİK ÖĞRENME:** mod DLL'inden gelen özel Node tiplerinin `_Ready/_Process/_Input`
     callback'leri Godot tarafından çağrılMAZ (dinamik assembly). Bu yüzden node değil, `ProcessFrame` sinyali.
2. **MCP** (`~/projects/STS2MCP`, kunology'nin modu): state sorgu + programatik navigasyon/oyun.
   - **DÜZELTME:** Wine `HttpListener`'ı (http.sys) desteklemiyor → `SimpleHttp.cs` ile `TcpListener` shim'i yazıldı.
     Ayrıca 0.107.1 API kaymaları düzeltildi (IsPlayPhase, ICombatState cast, MerchantRoom.GetLocalInventory).
   - Port 15526, host'tan `curl`/MCP tool ile erişilir. `get_game_state`, `menu_select`, `map_choose_node`,
     `combat_play_card` vb. çalışıyor.

## ✅ Portrait ekranlar (PC'de doğrulandı, screenshot'lı)
| Ekran | Durum | Not |
|---|---|---|
| Ana menü | ✅ Cilalı | bg cover, logo ortalı+ölçekli, butonlar ortalı (MenuLayoutPatches) |
| Karakter seçim | ✅ Cilalı | bilgi paneli taşması düzeltildi (SelectCharacter postfix, panel x=40) |
| Run modu seçim | ✅ İyi | Standard/Daily/Custom, 3 kart ortalı |
| Deste görünümü | ✅ İyi | NCardGrid 2 sütun |
| Pause menü | ✅ İyi | Resume/Settings/Give Up/Save&Quit ortalı |
| Ayarlar | ✅ İyi | General/Graphics/Sound/Input sekmeleri, tüm seçenekler okunur |
| Event (Neow) | ✅ İyi | art üstte, diyalog+seçenekler altta okunur, işlevsel |
| Harita | ✅ Cilalı | legend taşması düzeltildi (MapLegendX getter clamp) |
| Savaş | ✅ Cilalı | bg dikeyi kaplıyor (scale 1.7), el yukarı alındı (170px), tüm UI erişilebilir (CombatLayoutPatches) |
| Ödül (Loot) | ✅ İyi | merkezli modal, kutudan iyi |
| Kart ödül | ✅ İyi | 3 kart yan yana, okunur |
| Dükkan | ✅ İşlevsel | kartlar/relic/potion fiyatlı; sağ kenar kartları hafif kırpık |
| Deste görünümü | ✅ İyi | NCardGrid 2 sütuna adapte (kutudan) |

**Tam oyun döngüsü portrait'te oynanabiliyor:** menü → karakter → harita → savaş → ödül → dükkan → deste.

## 🔜 Kalan
- Dükkan sağ-kenar kartları hafif kırpık
- Görülmeyen ekranlar: event, dinlenme (rest), hazine (treasure), kart seçim overlay'leri (muhtemelen kutudan iyi)
- Savaş: yaratıklar biraz küçük (opsiyonel büyütme), üst HUD sağ tarafı kalabalık
- Dokunmatik cila (uzun basış önizleme, dikey sürükleyerek hedefleme)

## Faz 2 — Android (henüz başlamadı)
- NoSteam v0.3.19 + Ekyso APK repack (manifest → portrait), STS2Mobile.dll gerekirse 0.107.1'e derle.
- Mod → `/storage/emulated/0/StS2Launcher/Mods/`. Crack dosyaları ASLA kopyalanmaz.

## Geliştirme döngüsü
```
source scripts/lib.sh
sts2_kill; sts2_build && sts2_install; <launch>   # veya dev-cycle.sh
bcmd "dumpscreen"      # aktif ekran ağacı → godot.log
bshot <ad>             # oyun-içi screenshot → scratchpad/<ad>-s.png
# MCP: get_game_state, menu_select, map_choose_node, combat_play_card ...
```
Mod patch'leri: `src/Sts2Portrait/Patches/` (Display, Menu, Combat, Map, Diagnostics).
Merkezi ayarlar: `PortraitConfig.cs` + her patch sınıfının başındaki sabitler (deneyle ayarlı).
