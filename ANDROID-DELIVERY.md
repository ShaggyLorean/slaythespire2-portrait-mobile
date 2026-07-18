# Android Teslimat Stratejisi

Portrait StS2 modunu Android telefonlara nasıl ulaştırırız? (araştırma: wf_af253aa9)

## Temel gerçekler
- **Resmi StS2 Android uygulaması YOK** (EA sadece PC). Oyun v0.107.1, Godot 4.5.1 + C#/.NET 9.
- Oyun *yönetilen* mantığı `sts2.dll` (IL, platform-bağımsız); *native* katman (Godot fork + FMOD +
  Spine) Windows x86_64 → Android'de sadece launcher'ın kendi ARM-native runtime'ıyla çalışır.
- **Doğrudan Godot .NET Android export mümkün değil**: oyunun Godot *projesi* elimizde yok (sadece
  `.pck` + `sts2.dll`), sadece *sarmalama* yapabiliriz — launcher'ların yaptığı da bu.
  (.NET 9 blocker'ı ise abartılmış: mobile Mono runtime hâlâ geliyor; sorun linux-bionic NativeAOT.)

## İki launcher
- **Ekyso/StS2-Launcher** (MIT): custom Godot+Mono ARM runtime + `STS2Mobile.dll` (16 Harmony patch).
  Public GitHub release **v0.2.0** (game 0.98.x hedefli); güncel alfalar sadece Cinderbox Discord'da.
  Steam login (SteamKit2) ile depot indirir + cloud sync. Mods klasörü: `/storage/emulated/0/StS2Launcher/Mods/`
  (ama Android mod yükleme **flaky**, issue #31). Sık sorun: her oyun güncellemesi patch'leri kırıyor → siyah ekran.
  Full APK sıfırdan derlenemez (Godot fork + FMOD + Spine SDK'ları repoda yok); ama `STS2Mobile.dll` derlenebilir.
- **darthalex2014/StS2-Launcher** (NoSteam): sadece hazır APK, **v0.3.19** (game 0.105.1). Kullanıcı kendi
  `SlayTheSpire2.pck` + `data_sts2_windows_x86_64`'ünü `/storage/emulated/0/StS2/`'ye kopyalar → offline çalışır.

## Steam login ile indirme?
- Ekyso'nun modeli: SteamKit2 (resmi olmayan Steam client) ile 2FA login → depot indirir. **Ownership meşru
  ama mekanizma gri alan** (SteamKit3rd-party client, ToS'a aykırı; kullanıcı kimlik bilgisini 3.parti uygulamaya verir).
- **Öneri: bunu KENDİMİZ yapmayalım.** Sanctioned bir Valve API'si yok. Temiz yol: kullanıcı kendi Steam-kurulu
  oyun dosyalarını telefona kopyalar (darthalex modeli). Kimlik bilgisi yok, DRM yok, meşru (kullanıcı oyuna sahip).

## APK repack (portrait manifest)
```bash
apktool d launcher.apk -o work
# work/AndroidManifest.xml ana activity'de: android:screenOrientation="sensorPortrait"
apktool b work -o out.apk
zipalign -p -f 4 out.apk aligned.apk      # -p ŞART (resources.arsc/.so hizalama, API30+)
java -jar uber-apk-signer.jar -a aligned.apk   # zipalign+sign+verify
```
**KRİTİK Godot tuzağı:** manifest-only orientation ÇALIŞMAZ — Godot runtime'da project ayarından yönü
landscape'e geri set eder. **Çözüm bizde zaten var:** modumuz `DisplayServer.ScreenSetOrientation(Portrait)`
çağırıyor (PortraitDisplay.cs, Android'de aktif). Yani manifest + modun runtime çağrısı birlikte → gerçek portrait.
Not: Ekyso launcher'ında `.pck` external storage'dan yüklendiği için APK repack HAFİF (sadece manifest).

## Önerilen dağıtım modeli (balatro-portrait-mobile ile aynı şekil)
Mod = SADECE patch kodu (oyun asset'i yok). İki katman:
1. **Birincil: resmi loader mod DLL'i** (`Sts2Portrait.dll` + `.json`). PC'de `mods/`, Android'de
   `/storage/emulated/0/StS2Launcher/Mods/`. Tek artefakt, PC + Android. Portrait layout + orientation flip
   birlikte → Android'de gerçek portrait (landscape frame içinde portrait UI değil). **`sts2.dll`'e pin +
   her oyun sürümüne yeniden derle** (Harmony patch'leri sürüm-hassas — kaçınılmaz).
2. **İkincil: repack script'i** (build.py muadili) — kullanıcının kendi launcher APK'sini portrait manifest'e
   patch'ler + modu paketler. Loader'ın Android mod yüklemesi bozuksa yedek.
- **Steam Workshop: Android'e ULAŞMAZ** (launcher'lar depot/oyun dosyası çeker, Workshop item değil). PC için opsiyonel.
- **Repoya asla girmez:** oyun asset'i, sts2.dll, başkasının APK rehost'u. Kullanıcı kendi oyununu getirir.

## En büyük risk: SÜRÜM HAMLESİ
StS2 EA güncellemeleri (0.108+) Harmony patch katmanını tekrar tekrar kırıyor; launcher'lar da oyunun
gerisinde (0.98-0.105 vs 0.108). Modumuz: (a) tam `sts2.dll`'e karşı sürüm-başına derlenebilir DLL,
(b) runtime orientation flip ile eşli. Dağıtım: MIT repo, sadece patch kodu, kullanıcı kendi oyununu getirir.

## Kullanıcının sorduğu sorulara net cevap
- **"Steam login ile indirtir miyiz?"** Teknik olarak Ekyso yapıyor (SteamKit2) ama ToS-gri + kimlik riski;
  ÖNERMİYORUM. Kullanıcı kendi dosyalarını kopyalasın.
- **"Tamamiyle Android'e portlayabilir miyiz?"** Doğrudan hayır (Godot projesi/native SDK'lar yok). Launcher-sarmalama
  tek yol — ki mevcut launcher'lar bunu zaten yapıyor; biz portrait modunu onların Mods klasörüne veriyoruz.
- **"APK yapabilir miyiz?"** Evet — kullanıcının launcher APK'sini portrait manifest'e repack (script'le), ya da
  daha temizi: mod DLL'ini Mods klasörüne koy (repack gerektirmez, loader çalışıyorsa).

## DOĞRULANMIŞ: OnePlus 13'te çalışan offline portrait (2026-07-18)
Gerçek cihazda uçtan uca çalıştı — menü, karakter seçimi tam portrait, ekranı kaplıyor, siyah bant yok.
- **Kurulum:** darthalex NoSteam launcher APK (v0.3.19) + kullanıcının kendi oyun dosyaları
  (`SlayTheSpire2.pck` + `data_sts2_windows_x86_64`) `/storage/emulated/0/StS2LauncherMM/`'ye kopyalanır,
  launcher "Install Local Files" ile `files/game/`'e açar.
- **Kritik keşif:** Launcher'ın oyun mod-loader redirect'i yeni sürümlerde KIRIK (`"mods"` string'i
  `ModManager.Initialize`'ın async state-machine'ine taşınmış; transpiler dış metni hedefliyor → dış mod
  hiç yüklenmiyor). Çözüm: Mono.Cecil ile `STS2Mobile.ModEntry.Apply`'a `PortraitMod.Init()` çağrısı enjekte
  et (launcher'ın kendi native-invoked entry'si) + DLL'imizi publish dizinine koy. `scripts/android-deploy.sh`.
- **Content-scale savaşı:** `NGame.OnWindowChange` `AspectRatioSetting`'e göre `ContentScaleSize`'ı
  (1680×1260 vb.) set ediyor; bizim portrait set'imiz `size_changed` tetikleyince geri alınıyordu.
  Fix: `NGame.OnWindowChange` + `NGlobalUi.OnWindowChange` prefix-skip → kendi portrait canvas'ımız
  (telefon aspect'inden dinamik, 1440×3168 → contentScaleSize 1173×2580) kazanır.
- **Çakışan mobil-UI:** launcher'ın `UiScalePatches`'i devre dışı (bizim tam portrait UI'mız var).
- **Kalan:** standalone "Slay the Spire 2" uygulaması (rebrand APK: isim/ikon/boot-to-game). Şu an
  launcher üzerinden çalışıyor; paketleme fazı sıradaki.
