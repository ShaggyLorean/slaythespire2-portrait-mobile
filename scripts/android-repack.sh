#!/usr/bin/env bash
# =============================================================================
# draft-android-repack.sh — StS2 launcher APK'sini PORTRAIT'e repack eder
# =============================================================================
# NE YAPAR:
#   Kullanıcının KENDİ getirdiği launcher APK'sini (Ekyso/StS2-Launcher veya
#   darthalex NoSteam) alır, AndroidManifest.xml'deki ana activity'ye
#   android:screenOrientation="sensorPortrait" ekler/değiştirir, yeniden
#   paketler, hizalar (zipalign) ve imzalar. Çıktı AYRI bir dosyaya yazılır;
#   girdi APK'ye asla dokunulmaz.
#
# NE YAPMAZ (bilerek):
#   - Hiçbir şey İNDİRMEZ. APK, oyun dosyası, sts2.dll — hiçbiri.
#   - Hiçbir oyun asset'i / başkasının APK'sini REHOST ETMEZ.
#   - Girdi APK'yi değiştirmez; her şey geçici bir çalışma dizininde olur.
#
# APK'NİZİ NEREDEN GETİRİRSİNİZ (script indirmez, SİZ getirirsiniz):
#   - Ekyso/StS2-Launcher: kendi indirdiğiniz GitHub release APK'si (v0.2.0)
#     veya Cinderbox Discord'daki güncel alfa. MIT lisanslı launcher.
#   - darthalex2014 NoSteam: kendi edindiğiniz APK (v0.3.19). Oyun dosyalarını
#     (SlayTheSpire2.pck vb.) KENDİ Steam kurulumunuzdan kopyalarsınız.
#   APK'yi bu makineye koyun ve yolunu 1. argüman olarak verin.
#
# ÖNEMLİ GODOT TUZAĞI:
#   Manifest'teki orientation TEK BAŞINA YETMEZ — Godot runtime'da project
#   ayarından yönü landscape'e geri çevirir. Portrait modumuz
#   (Sts2Portrait.dll, PortraitDisplay.cs) çalışma anında
#   DisplayServer.ScreenSetOrientation(Portrait) çağırdığı için manifest +
#   mod DLL'i BİRLİKTE gerçek portrait verir. Yani bu repack'ten sonra
#   Sts2Portrait.dll'i launcher'ın Mods klasörüne koymayı unutmayın:
#     /storage/emulated/0/StS2Launcher/Mods/   (Ekyso)
#
# GEREKEN ARAÇLAR (hepsi yerelde olmalı; script hiçbirini indirmez):
#   apktool, zipalign (Android SDK build-tools), java
#   İmza için: uber-apk-signer (tercih) YA DA apksigner+keytool (fallback)
#
# KULLANIM:
#   ./draft-android-repack.sh <girdi-launcher.apk> [cikti-portrait.apk]
# =============================================================================

set -euo pipefail

# ----------------------------------------------------------------------------
# Yardımcılar
# ----------------------------------------------------------------------------
log()  { printf '\033[1;34m[repack]\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m[uyari]\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31m[hata]\033[0m %s\n'  "$*" >&2; exit 1; }

have() { command -v "$1" >/dev/null 2>&1; }

usage() {
    sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'
    exit 1
}

# ----------------------------------------------------------------------------
# Argümanlar
# ----------------------------------------------------------------------------
[[ $# -ge 1 ]] || usage

IN_APK="$1"
# Çıktı her zaman AYRI dosya — girdinin üzerine asla yazmayız.
OUT_APK="${2:-${IN_APK%.apk}-portrait.apk}"

# --- Girdi doğrulama --------------------------------------------------------
[[ -f "$IN_APK" ]] || die "Girdi APK bulunamadı: $IN_APK
  Kendi launcher APK'nizi (Ekyso veya NoSteam) bu makineye kopyalayın ve
  yolunu argüman olarak verin. Bu script hiçbir şey indirmez."

[[ "$(realpath "$IN_APK")" != "$(realpath -m "$OUT_APK")" ]] || die "Çıktı, girdiyle aynı dosya olamaz (girdi korunur)."

# APK aslında bir zip'tir — kaba bir bütünlük kontrolü (magic: PK\x03\x04).
if [[ "$(head -c 2 "$IN_APK")" != "PK" ]]; then
    die "Bu dosya bir APK/zip gibi görünmüyor: $IN_APK"
fi

# ----------------------------------------------------------------------------
# Araç kontrolleri — eksikse İNDİRMEYİZ, kullanıcıya söyleriz.
# ----------------------------------------------------------------------------
have apktool  || die "apktool bulunamadı. Dağıtım paketinden ya da https://apktool.org üzerinden KENDİNİZ kurun."
have java     || die "java bulunamadı (apktool ve imzalama için gerekli). JRE/JDK kurun."
have zipalign || die "zipalign bulunamadı. Android SDK build-tools kurun ve PATH'e ekleyin.
  (ör. \$ANDROID_HOME/build-tools/<sürüm>/zipalign)"

# İmzalayıcı seçimi: uber-apk-signer (jar veya PATH) tercih, apksigner fallback.
# UBER_APK_SIGNER_JAR env değişkeniyle jar yolu verilebilir.
SIGNER=""
UBER_JAR="${UBER_APK_SIGNER_JAR:-}"
if [[ -n "$UBER_JAR" && -f "$UBER_JAR" ]]; then
    SIGNER="uber-jar"
elif have uber-apk-signer; then
    SIGNER="uber-cmd"
elif have apksigner; then
    SIGNER="apksigner"
    have keytool || die "apksigner fallback için keytool gerekli (JDK ile gelir)."
    warn "uber-apk-signer yok → apksigner + geçici debug keystore ile imzalanacak."
else
    die "İmzalayıcı yok. Şunlardan birini kurun:
  - uber-apk-signer (jar'ı indirin, UBER_APK_SIGNER_JAR=/yol/uber-apk-signer.jar verin)
  - apksigner (Android SDK build-tools içinde)"
fi

# Manifest düzenleme aracı: xmlstarlet (hassas) > sed (kaba). İkisi de yoksa dur.
XML_TOOL=""
if have xmlstarlet; then
    XML_TOOL="xmlstarlet"
elif have sed; then
    XML_TOOL="sed"
    warn "xmlstarlet yok → sed ile kaba düzenleme yapılacak (tüm activity'lere uygulanabilir)."
else
    die "Ne xmlstarlet ne sed var — manifest düzenlenemez."
fi

# ----------------------------------------------------------------------------
# Çalışma dizini (geçici; çıkışta temizlenir)
# ----------------------------------------------------------------------------
WORK="$(mktemp -d "${TMPDIR:-/tmp}/sts2-repack.XXXXXX")"
cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

DEC="$WORK/decoded"       # apktool d çıktısı
REBUILT="$WORK/rebuilt.apk"
ALIGNED="$WORK/aligned.apk"

# ----------------------------------------------------------------------------
# 1) apktool d — APK'yi aç (kaynaklar decode edilir, manifest düz XML olur)
# ----------------------------------------------------------------------------
log "1/5 apktool d: APK açılıyor..."
# -s: classes.dex bayt-bayt korunur (manifest-only patch; smali roundtrip riski yok)
apktool d -s -f "$IN_APK" -o "$DEC" >/dev/null

MANIFEST="$DEC/AndroidManifest.xml"
[[ -f "$MANIFEST" ]] || die "AndroidManifest.xml decode edilemedi — APK bozuk olabilir."

# ----------------------------------------------------------------------------
# 2) Manifest patch — ana activity'ye screenOrientation=sensorPortrait
#    Not: sensorPortrait = dik + baş aşağı portrait'e izin verir, landscape'e asla.
# ----------------------------------------------------------------------------
log "2/5 Manifest patch: android:screenOrientation=\"sensorPortrait\"..."

ORIENT='sensorPortrait'

if [[ "$XML_TOOL" == "xmlstarlet" ]]; then
    # Ana (LAUNCHER) activity'yi hedefle. XPath: LAUNCHER kategorisi içeren
    # intent-filter'a sahip activity düğümü.
    LAUNCH_XPATH='//activity[intent-filter/category/@android:name="android.intent.category.LAUNCHER"]'

    # Önce var olan attribute'u güncelle (varsa)...
    xmlstarlet ed -L \
        -u "$LAUNCH_XPATH/@android:screenOrientation" -v "$ORIENT" \
        "$MANIFEST"
    # ...sonra hiç yoksa ekle (insert; attribute zaten varsa bu adım no-op
    # olsun diye "yoksa" koşuluyla).
    xmlstarlet ed -L \
        -i "${LAUNCH_XPATH}[not(@android:screenOrientation)]" \
        -t attr -n "android:screenOrientation" -v "$ORIENT" \
        "$MANIFEST"
else
    # sed fallback — kaba ama Godot launcher APK'leri tipik olarak tek
    # activity içerdiği için pratikte yeterli.
    if grep -q 'android:screenOrientation=' "$MANIFEST"; then
        # Var olan orientation değer(ler)ini değiştir (landscape → sensorPortrait vb.)
        sed -i 's/android:screenOrientation="[^"]*"/android:screenOrientation="'"$ORIENT"'"/g' "$MANIFEST"
        warn "sed: manifest'teki TÜM screenOrientation değerleri sensorPortrait yapıldı."
    else
        # Hiç yok → her <activity açılış tag'ine ekle (tek activity'li
        # launcher'da güvenli; çoklu activity'de hepsine uygulanır — uyarıyoruz).
        sed -i 's/<activity /<activity android:screenOrientation="'"$ORIENT"'" /g' "$MANIFEST"
        warn "sed: attribute yoktu → TÜM <activity> tag'lerine eklendi."
    fi
fi

# Patch gerçekten yazıldı mı? (sessiz başarısızlığı yakala)
grep -q "android:screenOrientation=\"$ORIENT\"" "$MANIFEST" \
    || die "Manifest patch DOĞRULANAMADI — screenOrientation eklenememiş. Manifest'i elle kontrol edin: $MANIFEST"

log "   Manifest OK: $(grep -c "screenOrientation=\"$ORIENT\"" "$MANIFEST") activity sensorPortrait."

# ----------------------------------------------------------------------------
# 3) apktool b — yeniden paketle
# ----------------------------------------------------------------------------
log "3/5 apktool b: yeniden paketleniyor..."
apktool b "$DEC" -o "$REBUILT" >/dev/null
[[ -f "$REBUILT" ]] || die "apktool b çıktı üretmedi."

# ----------------------------------------------------------------------------
# 4) zipalign — -p ŞART (uncompressed .so ve resources.arsc sayfa hizalama;
#    API 30+ bunu zorunlu kılar, -p olmadan Android 11+ APK'yi REDDEDER).
# ----------------------------------------------------------------------------
log "4/5 zipalign -p -f 4 ..."
zipalign -p -f 4 "$REBUILT" "$ALIGNED"
# Hizalamayı doğrula (zipalign -c: check modu)
zipalign -c -p 4 "$ALIGNED" || die "zipalign doğrulaması başarısız."

# ----------------------------------------------------------------------------
# 5) İmzalama — apktool imzayı düşürür; imzasız APK yüklenemez.
#    NOT: Yeni imza ORİJİNALDEN FARKLI olacağı için, cihazda orijinal launcher
#    kuruluysa önce onu kaldırmanız gerekir (signature mismatch).
# ----------------------------------------------------------------------------
log "5/5 İmzalanıyor ($SIGNER)..."
case "$SIGNER" in
    uber-jar)
        # uber-apk-signer: zipalign kontrolü + debug imza + verify tek adımda.
        java -jar "$UBER_JAR" -a "$ALIGNED" --overwrite >/dev/null
        SIGNED="$ALIGNED"
        ;;
    uber-cmd)
        uber-apk-signer -a "$ALIGNED" --overwrite >/dev/null
        SIGNED="$ALIGNED"
        ;;
    apksigner)
        # Fallback: geçici (atılabilir) debug keystore üret ve onunla imzala.
        # Keystore çalışma dizininde — script sonunda silinir; bu bilinçli:
        # kalıcı kimlik değil, yerel kurulum imzası.
        KS="$WORK/debug.keystore"
        keytool -genkeypair -v -keystore "$KS" -storepass android -keypass android \
            -alias portraitdebug -keyalg RSA -keysize 2048 -validity 10000 \
            -dname "CN=Sts2PortraitDebug" >/dev/null 2>&1
        apksigner sign --ks "$KS" --ks-pass pass:android --key-pass pass:android \
            --ks-key-alias portraitdebug "$ALIGNED"
        apksigner verify "$ALIGNED" || die "apksigner verify başarısız."
        SIGNED="$ALIGNED"
        ;;
esac

# ----------------------------------------------------------------------------
# Çıktıyı AYRI dosyaya kopyala (girdi el değmemiş kalır)
# ----------------------------------------------------------------------------
cp -f "$SIGNED" "$OUT_APK"
log "TAMAM → $OUT_APK"
cat <<EOF

Sonraki adımlar:
  1. Cihazda ORİJİNAL launcher kuruluysa KALDIRIN (imza farklı, üstüne kurulmaz).
  2. $OUT_APK dosyasını telefona kopyalayıp kurun.
  3. Oyun dosyalarınızı KENDİ Steam kurulumunuzdan launcher'ın beklediği yere
     kopyalayın (ör. NoSteam: /storage/emulated/0/StS2/).
  4. Sts2Portrait.dll + manifest json'ı launcher'ın Mods klasörüne koyun.
     Ekyso: /storage/emulated/0/StS2Launcher/Mods/ (Android mod yükleme bazı
     sürümlerde sorunlu — launcher issue #31).
     NoSteam (darthalex): mod desteği/klasörü DOĞRULANMADI — launcher mod
     yüklemiyorsa bu repack tek başına portrait VERMEZ (manifest yetmez,
     modun runtime ScreenSetOrientation çağrısı şart).
EOF
