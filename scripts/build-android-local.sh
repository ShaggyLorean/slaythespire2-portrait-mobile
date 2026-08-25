#!/usr/bin/env bash
#
# Linux port of scripts/build-android-local.ps1.
#
# Produces a signed, verified local-package APK from a Linux host (developed on
# CachyOS/Arch). The PowerShell original stays authoritative for Windows; this
# script mirrors its step order and its checks:
#
#   1. resolve Android SDK / JDK / gradle / keystore
#   2. dotnet publish src/STS2Mobile
#   3. stage managed dependencies into android/assets/dotnet_bcl
#   4. stage the Mono native runtime .so files into android/libs/release/<abi>
#   5. build + run tools/SteamKitAndroidPatch over the staged assemblies
#   6. rebuild the FMOD JNI bridge with NDK clang
#   7. gradle -p android assembleMonoRelease
#   8. verify the APK, then archive it to artifacts/android with .sha256/.json
#
# Linux-specific notes (see also the deviation list at the bottom of this file):
#   * GRADLE_USER_HOME is forced off the NTFS work tree. The repo lives on an
#     ntfs3 mount and gradle's caches do not survive its locking semantics.
#   * JAVA_HOME is resolved to a JDK 17 install explicitly instead of relying on
#     the system default (archlinux-java may point at a newer JDK; AGP 8.6.1
#     requires 17).
#   * The runtime version of the staged BCL is read out of the PE version
#     resource of System.Private.CoreLib.dll, which is what
#     [FileVersionInfo]::GetVersionInfo().ProductVersion does on Windows.
#
set -euo pipefail

log()  { printf '%s\n' "$*"; }
warn() { printf 'WARNING: %s\n' "$*" >&2; }
die()  { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd)"

# ---------------------------------------------------------------------------
# Parameters (flags win over environment variables, which win over defaults).
# ---------------------------------------------------------------------------
VERSION_NAME="${STS2_VERSION_NAME:-0.4.0-dev19}"
VERSION_CODE="${STS2_VERSION_CODE:-40028}"
PACKAGE_NAME="${STS2_PACKAGE_NAME:-com.sts2portrait.mobile.local}"
ABI="${STS2_ABI:-arm64-v8a}"
ARG_ANDROID_HOME="${STS2_ANDROID_HOME:-}"
ARG_JAVA_HOME="${STS2_JAVA_HOME:-}"
ARG_GRADLE="${STS2_GRADLE:-${GRADLE_BIN:-}}"
KEYSTORE_PATH="${STS2_KEYSTORE:-$ROOT/tmp/localtest.keystore}"
KEYSTORE_PASSWORD="${STS2_KEYSTORE_PASSWORD:-android}"
KEYSTORE_ALIAS="${STS2_KEYSTORE_ALIAS:-androiddebugkey}"
REBUILD_MONOMOD_SHIM="${STS2_REBUILD_MONOMOD_SHIM:-0}"

usage() {
    cat <<'USAGE'
Usage: scripts/build-android-local.sh [options]

  --version-name <s>        default 0.4.0-dev19       (env STS2_VERSION_NAME)
  --version-code <n>        default 40028             (env STS2_VERSION_CODE)
  --package-name <s>        default com.sts2portrait.mobile.local
                                                      (env STS2_PACKAGE_NAME)
  --abi <arm64-v8a|x86_64|universal>
                            default arm64-v8a         (env STS2_ABI)
  --android-home <path>     default $ANDROID_HOME / $ANDROID_SDK_ROOT / ~/Android/Sdk
  --java-home <path>        default: a JDK 17 install
  --gradle <path>           default: gradle on PATH, else the version pinned by
                            android/gradle/wrapper/gradle-wrapper.properties is
                            downloaded into $GRADLE_USER_HOME/sts2-dists
  --keystore <path>         default tmp/localtest.keystore
  --keystore-password <s>   default android
  --keystore-alias <s>      default androiddebugkey
  --rebuild-monomod-shim    also recompile native/monomodshim/monomodshim.c
                            (off by default: the checked-in .so is the one the
                            known-good builds shipped)
  -h, --help
USAGE
}

while [ $# -gt 0 ]; do
    case "$1" in
        --version-name)        VERSION_NAME="${2:?}"; shift 2 ;;
        --version-name=*)      VERSION_NAME="${1#*=}"; shift ;;
        --version-code)        VERSION_CODE="${2:?}"; shift 2 ;;
        --version-code=*)      VERSION_CODE="${1#*=}"; shift ;;
        --package-name)        PACKAGE_NAME="${2:?}"; shift 2 ;;
        --package-name=*)      PACKAGE_NAME="${1#*=}"; shift ;;
        --abi)                 ABI="${2:?}"; shift 2 ;;
        --abi=*)               ABI="${1#*=}"; shift ;;
        --android-home)        ARG_ANDROID_HOME="${2:?}"; shift 2 ;;
        --android-home=*)      ARG_ANDROID_HOME="${1#*=}"; shift ;;
        --java-home)           ARG_JAVA_HOME="${2:?}"; shift 2 ;;
        --java-home=*)         ARG_JAVA_HOME="${1#*=}"; shift ;;
        --gradle)              ARG_GRADLE="${2:?}"; shift 2 ;;
        --gradle=*)            ARG_GRADLE="${1#*=}"; shift ;;
        --keystore)            KEYSTORE_PATH="${2:?}"; shift 2 ;;
        --keystore=*)          KEYSTORE_PATH="${1#*=}"; shift ;;
        --keystore-password)   KEYSTORE_PASSWORD="${2:?}"; shift 2 ;;
        --keystore-password=*) KEYSTORE_PASSWORD="${1#*=}"; shift ;;
        --keystore-alias)      KEYSTORE_ALIAS="${2:?}"; shift 2 ;;
        --keystore-alias=*)    KEYSTORE_ALIAS="${1#*=}"; shift ;;
        --rebuild-monomod-shim) REBUILD_MONOMOD_SHIM=1; shift ;;
        -h|--help)             usage; exit 0 ;;
        *) die "unknown option: $1 (try --help)" ;;
    esac
done

case "$ABI" in
    arm64-v8a|x86_64|universal) ;;
    *) die "invalid --abi '$ABI' (expected arm64-v8a, x86_64 or universal)" ;;
esac
case "$VERSION_CODE" in
    ''|*[!0-9]*) die "--version-code must be an integer, got '$VERSION_CODE'" ;;
esac

# Resolve-AndroidApkTargetAbis
if [ "$ABI" = "universal" ]; then
    TARGET_ABIS=(arm64-v8a x86_64)
else
    TARGET_ABIS=("$ABI")
fi

ANDROID_DIR="$ROOT/android"
PROJECT_PATH="$ROOT/src/STS2Mobile/STS2Mobile.csproj"
PUBLISH_DIR="$ROOT/src/STS2Mobile/bin/Release/net9.0/publish"
BCL_DIR="$ANDROID_DIR/assets/dotnet_bcl"
UPSTREAM_PUBLISH_DIR="$ROOT/upstream/godot-export/.godot/mono/publish/arm64"
PATCHER_PROJECT="$ROOT/tools/SteamKitAndroidPatch/SteamKitAndroidPatch.csproj"
PATCHER_DLL="$ROOT/tools/SteamKitAndroidPatch/bin/Release/net9.0/SteamKitAndroidPatch.dll"
FMOD_BRIDGE_SRC="$ANDROID_DIR/native/sts2fmodbridge/sts2fmodbridge.c"
MONOMOD_SHIM_SRC="$ROOT/native/monomodshim/monomodshim.c"
TMP_DIR="$ROOT/tmp"
ARTIFACT_DIR="$ROOT/artifacts/android"

for tool in dotnet unzip curl python3 sha256sum; do
    command -v "$tool" >/dev/null 2>&1 || die "required tool not found on PATH: $tool"
done

# ---------------------------------------------------------------------------
# 1. Resolve toolchain locations
# ---------------------------------------------------------------------------
resolve_android_home() {
    local candidate
    for candidate in "$ARG_ANDROID_HOME" "${ANDROID_HOME:-}" "${ANDROID_SDK_ROOT:-}" "$HOME/Android/Sdk"; do
        if [ -n "$candidate" ] && [ -d "$candidate" ]; then
            printf '%s\n' "$candidate"
            return 0
        fi
    done
    return 1
}

# AGP 8.6.1 pins JavaVersion.VERSION_17 (android/config.gradle). Rather than
# flipping the machine's default JDK with archlinux-java, pick a 17 install and
# hand it to gradle through JAVA_HOME.
java_major() {
    local home="$1"
    [ -x "$home/bin/javap" ] || return 1
    "$home/bin/java" -version 2>&1 | head -1 |
        sed -n 's/.*version "\([0-9][0-9]*\)\..*/\1/p;s/.*version "\([0-9][0-9]*\)".*/\1/p'
}

resolve_java_home() {
    local candidate major
    for candidate in \
        "$ARG_JAVA_HOME" \
        "${JAVA_HOME:-}" \
        /usr/lib/jvm/java-17-openjdk \
        /usr/lib/jvm/java-17-openjdk-amd64 \
        /usr/lib/jvm/jdk-17 \
        "$(ls -d /usr/lib/jvm/*17* 2>/dev/null | head -1)"
    do
        [ -n "$candidate" ] && [ -d "$candidate" ] || continue
        major="$(java_major "$candidate" || true)"
        [ "$major" = "17" ] || continue
        printf '%s\n' "$candidate"
        return 0
    done
    # Last resort: whatever JAVA_HOME points at, as long as it is a JDK.
    if [ -n "${ARG_JAVA_HOME:-}" ] && [ -x "$ARG_JAVA_HOME/bin/java" ]; then
        printf '%s\n' "$ARG_JAVA_HOME"
        return 0
    fi
    return 1
}

# The wrapper jar is not checked in (*.jar is gitignored), so ./gradlew cannot
# bootstrap itself. Prefer a gradle already on PATH; otherwise download exactly
# the distribution android/gradle/wrapper/gradle-wrapper.properties pins.
resolve_gradle() {
    if [ -n "$ARG_GRADLE" ]; then
        [ -x "$ARG_GRADLE" ] || die "gradle not executable: $ARG_GRADLE"
        printf '%s\n' "$ARG_GRADLE"
        return 0
    fi

    if command -v gradle >/dev/null 2>&1; then
        command -v gradle
        return 0
    fi

    local props="$ANDROID_DIR/gradle/wrapper/gradle-wrapper.properties"
    [ -f "$props" ] || die "gradle not found on PATH and no wrapper properties at $props"

    local url version dist_root dist_bin zip
    url="$(sed -n 's/^distributionUrl=//p' "$props" | sed 's/\\:/:/g' | tr -d '\r')"
    [ -n "$url" ] || die "could not read distributionUrl from $props"
    version="$(printf '%s' "$url" | sed -n 's/.*gradle-\([0-9][0-9.]*\)-.*\.zip/\1/p')"
    [ -n "$version" ] || die "could not derive gradle version from $url"

    dist_root="$GRADLE_USER_HOME/sts2-dists"
    dist_bin="$dist_root/gradle-$version/bin/gradle"
    if [ ! -x "$dist_bin" ]; then
        mkdir -p "$dist_root"
        zip="$dist_root/gradle-$version-bin.zip"
        log "Downloading Gradle $version (no gradle on PATH)..." >&2
        curl -fsSL -o "$zip" "$url" || die "failed to download $url"
        unzip -q -o "$zip" -d "$dist_root" || die "failed to unzip $zip"
        rm -f "$zip"
    fi
    [ -x "$dist_bin" ] || die "gradle distribution unpacked but $dist_bin is missing"
    printf '%s\n' "$dist_bin"
}

ANDROID_HOME_RESOLVED="$(resolve_android_home)" ||
    die "Android SDK not found. Pass --android-home or set ANDROID_HOME/ANDROID_SDK_ROOT."
JAVA_HOME_RESOLVED="$(resolve_java_home)" ||
    die "JDK 17 not found. Pass --java-home or install jdk17-openjdk."

export ANDROID_HOME="$ANDROID_HOME_RESOLVED"
export ANDROID_SDK_ROOT="$ANDROID_HOME_RESOLVED"
export JAVA_HOME="$JAVA_HOME_RESOLVED"
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$ANDROID_HOME/cmdline-tools/latest/bin:$PATH"

# Keep gradle's cache/daemon state off the NTFS mount the repo lives on.
export GRADLE_USER_HOME="${GRADLE_USER_HOME:-$HOME/.gradle}"
mkdir -p "$GRADLE_USER_HOME"

GRADLE_BIN_RESOLVED="$(resolve_gradle)"

[ -f "$KEYSTORE_PATH" ] || die "Keystore not found: $KEYSTORE_PATH"
KEYSTORE_PATH="$(cd -- "$(dirname -- "$KEYSTORE_PATH")" && pwd)/$(basename -- "$KEYSTORE_PATH")"

if [ "$PACKAGE_NAME" = "com.sts2portrait.mobile.dev" ] && [ "$(basename "$KEYSTORE_PATH")" = "localtest.keystore" ]; then
    die "Refusing to build a locally signed APK with the release/dev package name. Use the default local package name or pass a release keystore intentionally."
fi

log "Android SDK : $ANDROID_HOME"
log "JDK         : $JAVA_HOME ($("$JAVA_HOME/bin/java" -version 2>&1 | head -1))"
log "Gradle      : $GRADLE_BIN_RESOLVED ($("$GRADLE_BIN_RESOLVED" --version 2>/dev/null | sed -n 's/^Gradle //p' | head -1))"
log "Gradle home : $GRADLE_USER_HOME"
log "Keystore    : $KEYSTORE_PATH"
log "Version     : $VERSION_NAME ($VERSION_CODE)  package=$PACKAGE_NAME  abi=$ABI"

# ---------------------------------------------------------------------------
# 2. dotnet publish
# ---------------------------------------------------------------------------
log ""
log "Publishing STS2Mobile..."
dotnet publish "$PROJECT_PATH" -c Release || die "dotnet publish failed"

# ---------------------------------------------------------------------------
# 3. Stage managed dependencies
# ---------------------------------------------------------------------------
mkdir -p "$BCL_DIR"

copy_managed_dependency() {
    local name="$1"
    if [ -f "$PUBLISH_DIR/$name" ]; then
        cp -f "$PUBLISH_DIR/$name" "$BCL_DIR/$name"
        return 0
    fi
    if [ -f "$UPSTREAM_PUBLISH_DIR/$name" ]; then
        cp -f "$UPSTREAM_PUBLISH_DIR/$name" "$BCL_DIR/$name"
        return 0
    fi
    die "Required managed dependency not found: $name"
}

MANAGED_DEPENDENCIES=(
    "STS2Mobile.dll"
    "SteamKit2.dll"
    "protobuf-net.dll"
    "protobuf-net.Core.dll"
    "System.IO.Hashing.dll"
    "ZstdSharp.dll"
    "0Harmony.dll"
    "GodotSharp.dll"
    "sts2.dll"
    "Steamworks.NET.dll"
    # Sentry.dll is deliberately NOT shipped: loading it on Android spins up
    # native crash-reporter threads that abort the process on a destroyed
    # mutex a few hundred ms into boot (device log: FORTIFY pthread_mutex_lock).
    # The patch layer skips Sentry when the assembly is absent.
)

log ""
log "Staging managed dependencies into android/assets/dotnet_bcl..."
for dependency in "${MANAGED_DEPENDENCIES[@]}"; do
    copy_managed_dependency "$dependency"
done

# The exclusion above only governs what this script copies. It cannot un-stage a
# Sentry.dll an earlier run (or a manual copy) already left behind, and this
# script must not delete files under android/assets. Say so loudly instead.
if [ -f "$BCL_DIR/Sentry.dll" ]; then
    warn "android/assets/dotnet_bcl/Sentry.dll is present and WILL be packaged into the APK."
    warn "This script never copies it (see the comment above), but it does not delete staged files either."
    warn "Remove it by hand if you want the no-Sentry boot path: rm android/assets/dotnet_bcl/Sentry.dll"
fi

# ---------------------------------------------------------------------------
# 4. Stage the native Mono runtime libraries
# ---------------------------------------------------------------------------
mono_runtime_package_name() {
    if [ "$1" = "x86_64" ]; then
        printf 'microsoft.netcore.app.runtime.mono.android-x64\n'
    else
        printf 'microsoft.netcore.app.runtime.mono.android-arm64\n'
    fi
}

# Equivalent of [FileVersionInfo]::GetVersionInfo($corelib).ProductVersion:
# read the ProductVersion string out of the PE VS_VERSIONINFO resource and
# drop the "+<commit>" suffix.
read_product_version() {
    python3 - "$1" <<'PY'
import sys
data = open(sys.argv[1], 'rb').read()
key = 'ProductVersion'.encode('utf-16-le')
i = data.find(key)
if i < 0:
    sys.exit('ProductVersion not found in ' + sys.argv[1])
j = i + len(key)
while j + 1 < len(data) and data[j] == 0 and data[j + 1] == 0:
    j += 2
end = data.find(b'\x00\x00', j)
if end < 0:
    sys.exit('malformed ProductVersion in ' + sys.argv[1])
if (end - j) % 2:
    end += 1
value = data[j:end].decode('utf-16-le', errors='replace').split('+')[0].strip()
if not value:
    sys.exit('empty ProductVersion in ' + sys.argv[1])
print(value)
PY
}

RUNTIME_VERSION=""
resolve_runtime_version() {
    local corelib="$BCL_DIR/System.Private.CoreLib.dll"
    [ -f "$corelib" ] || die "Managed runtime core library not staged: $corelib"
    RUNTIME_VERSION="$(read_product_version "$corelib")" ||
        die "Could not determine Microsoft.NETCore.App runtime version from $corelib."
}

# Returns the absolute path of <library> for <abi>, extracting the runtime pack
# from the NuGet cache or downloading the .nupkg from nuget.org when absent.
resolve_native_runtime_library() {
    local target_abi="$1" library="$2"
    local package_name package_root found download_dir extract_dir package_path package_url

    package_name="$(mono_runtime_package_name "$target_abi")"
    package_root="$HOME/.nuget/packages/$package_name"

    if [ -d "$package_root" ]; then
        found="$(find "$package_root" -type f -name "$library" -path "*/$RUNTIME_VERSION/*" 2>/dev/null | sort -r | head -1)"
        if [ -n "$found" ]; then
            printf '%s\n' "$found"
            return 0
        fi
    fi

    download_dir="$TMP_DIR/nuget-runtime"
    extract_dir="$download_dir/$package_name.$RUNTIME_VERSION"
    package_path="$download_dir/$package_name.$RUNTIME_VERSION.nupkg"
    package_url="https://api.nuget.org/v3-flatcontainer/$package_name/$RUNTIME_VERSION/$package_name.$RUNTIME_VERSION.nupkg"

    mkdir -p "$download_dir"

    if [ -d "$extract_dir" ]; then
        found="$(find "$extract_dir" -type f -name "$library" 2>/dev/null | head -1)"
        if [ -n "$found" ]; then
            printf '%s\n' "$found"
            return 0
        fi
        rm -rf "$extract_dir"
    fi

    if [ ! -f "$package_path" ]; then
        log "Downloading $package_name $RUNTIME_VERSION for native Android runtime libraries..." >&2
        curl -fsSL -o "$package_path" "$package_url" ||
            die "failed to download $package_url"
    fi

    if ! unzip -q -o "$package_path" -d "$extract_dir" 2>/dev/null; then
        log "Cached $package_name $RUNTIME_VERSION package was invalid. Downloading it again..." >&2
        rm -f "$package_path"
        rm -rf "$extract_dir"
        curl -fsSL -o "$package_path" "$package_url" ||
            die "failed to download $package_url"
        unzip -q -o "$package_path" -d "$extract_dir" ||
            die "failed to unzip $package_path"
    fi

    found="$(find "$extract_dir" -type f -name "$library" 2>/dev/null | head -1)"
    [ -n "$found" ] ||
        die "Downloaded $package_name $RUNTIME_VERSION, but $library was not found."
    printf '%s\n' "$found"
}

NATIVE_RUNTIME_LIBRARIES=(
    "libSystem.Globalization.Native.so"
    "libSystem.IO.Compression.Native.so"
    "libSystem.Native.so"
    "libSystem.Security.Cryptography.Native.Android.so"
    "libmono-component-debugger.so"
    "libmono-component-diagnostics_tracing.so"
    "libmono-component-hot_reload.so"
    "libmono-component-marshal-ilgen.so"
    "libmonosgen-2.0.so"
)

resolve_runtime_version
log "Staging Mono native runtime $RUNTIME_VERSION..."
for target_abi in "${TARGET_ABIS[@]}"; do
    native_lib_dir="$ANDROID_DIR/libs/release/$target_abi"
    # Never wiped: this directory also holds libgodot_android.so, the ported
    # libmonomodshim.so / libspine_godot.*.so and the FMOD bridge.
    mkdir -p "$native_lib_dir"
    for library in "${NATIVE_RUNTIME_LIBRARIES[@]}"; do
        native_so="$(resolve_native_runtime_library "$target_abi" "$library")"
        cp -f "$native_so" "$native_lib_dir/$library"
    done
done

# ---------------------------------------------------------------------------
# 5. Patch the managed assemblies
# ---------------------------------------------------------------------------
log ""
log "Building SteamKit Android patcher..."
dotnet build "$PATCHER_PROJECT" -c Release || die "SteamKit Android patcher build failed"

log "Patching managed assemblies for Android..."
# The patcher targets net9.0 but Arch ships only the .NET 10 shared runtime, so
# the framework-dependent launch fails with "You must install or update .NET".
# Roll forward instead of retargeting the csproj — Mono.Cecil is happy on 10.
DOTNET_ROLL_FORWARD=Major dotnet "$PATCHER_DLL" \
    "$BCL_DIR/SteamKit2.dll" \
    "$BCL_DIR/STS2Mobile.dll" \
    "$BCL_DIR/sts2.dll" || die "SteamKit Android patch failed"

# ---------------------------------------------------------------------------
# 6. Rebuild the native JNI shims with the NDK
# ---------------------------------------------------------------------------
NDK_CLANG=""
resolve_ndk_clang() {
    local candidate
    candidate="$(ls -d "$ANDROID_HOME"/ndk/*/toolchains/llvm/prebuilt/linux-x86_64/bin/clang 2>/dev/null | sort -V | tail -1)"
    [ -n "$candidate" ] && [ -x "$candidate" ] || return 1
    printf '%s\n' "$candidate"
}

# minSdk from android/config.gradle
NDK_API_LEVEL=24

build_native_shim() {
    local source="$1" output="$2" label="$3"
    shift 3
    local target_triple="aarch64-linux-android$NDK_API_LEVEL"
    if [ "${TARGET_ABIS[0]}" = "x86_64" ]; then
        target_triple="x86_64-linux-android$NDK_API_LEVEL"
    fi

    if [ ! -f "$source" ]; then
        warn "$label source not found ($source); keeping the existing $(basename "$output")."
        return 0
    fi
    if [ -z "$NDK_CLANG" ]; then
        warn "Android NDK clang not found under $ANDROID_HOME/ndk; keeping the existing $(basename "$output")."
        return 0
    fi

    local staging="$output.new"
    if "$NDK_CLANG" --target="$target_triple" -shared -fPIC -O2 -o "$staging" "$source" "$@"; then
        mv -f "$staging" "$output"
        log "  built $(basename "$output")"
    else
        rm -f "$staging"
        die "$label failed to compile"
    fi
}

NDK_CLANG="$(resolve_ndk_clang || true)"
log ""
log "Building native JNI shims..."
for target_abi in "${TARGET_ABIS[@]}"; do
    [ "$target_abi" = "arm64-v8a" ] || continue   # the shims are only staged for arm64
    build_native_shim "$FMOD_BRIDGE_SRC" \
        "$ANDROID_DIR/libs/release/$target_abi/libsts2fmodbridge.so" \
        "FMOD JNI bridge" -ldl
    if [ "$REBUILD_MONOMOD_SHIM" = "1" ]; then
        build_native_shim "$MONOMOD_SHIM_SRC" \
            "$ANDROID_DIR/libs/release/$target_abi/libmonomodshim.so" \
            "MonoMod shim"
    fi
done

# ---------------------------------------------------------------------------
# 7. Gradle
# ---------------------------------------------------------------------------
gradle_abi_list="$(IFS=,; printf '%s' "${TARGET_ABIS[*]}")"

# The daemon is what makes repeat builds fast; killing it every run cost
# roughly a minute per iteration. Keep it alive.

log ""
log "Building Android APK..."
"$GRADLE_BIN_RESOLVED" \
    -p "$ANDROID_DIR" \
    assembleMonoRelease \
    "-Pexport_version_name=$VERSION_NAME" \
    "-Pexport_version_code=$VERSION_CODE" \
    "-Pexport_package_name=$PACKAGE_NAME" \
    "-Pexport_enabled_abis=$gradle_abi_list" \
    "-Prelease_keystore_file=$KEYSTORE_PATH" \
    "-Prelease_keystore_password=$KEYSTORE_PASSWORD" \
    "-Prelease_keystore_alias=$KEYSTORE_ALIAS" || die "Gradle build failed"

APK_OUT_DIR="$ANDROID_DIR/build/outputs/apk/mono/release"
APK="$(ls -t "$APK_OUT_DIR"/StS2Portrait-v*.apk 2>/dev/null | head -1 || true)"
[ -n "$APK" ] || die "APK not found after build."

EXPECTED_APK_NAME="StS2Portrait-v$VERSION_NAME.apk"
[ "$(basename "$APK")" = "$EXPECTED_APK_NAME" ] ||
    die "Unexpected APK output after build. Expected $EXPECTED_APK_NAME, got $(basename "$APK")"

log ""
log "APK built: $APK"

# ---------------------------------------------------------------------------
# 8. Verify
# ---------------------------------------------------------------------------
# Port of Test-AndroidApkContents from scripts/android-apk-utils.ps1, plus the
# three libraries this fork adds on top of the upstream template.
REQUIRED_ASSETS=(
    "assets/bootstrap.pck"
    "assets/dotnet_bcl/STS2Mobile.dll"
    "assets/dotnet_bcl/GodotSharp.dll"
    "assets/dotnet_bcl/System.Private.CoreLib.dll"
)
REQUIRED_NATIVE_RUNTIME_LIBS=(
    "libSystem.Native.so"
    "libSystem.Security.Cryptography.Native.Android.so"
    "libmonosgen-2.0.so"
)
# Not in the ps1: these are the fork's own ported natives. If one of them goes
# missing the APK boots into a broken state, so fail rather than warn.
REQUIRED_PORTED_LIBS=(
    "libmonomodshim.so"
    "libspine_godot.android.template_release.arm64.so"
    "libsts2fmodbridge.so"
)
PATCHED_MARKER=".NET: Android platform detected. Setting api_assemblies_dir to app data path"
STALE_MARKER=".NET: Android platform detected. Setting api_assemblies_dir directly to pck path"

VERIFY_DIR="$TMP_DIR/apk-verify-$$-$RANDOM"
mkdir -p "$VERIFY_DIR"
cleanup_verify_dir() { rm -rf "$VERIFY_DIR"; }
trap cleanup_verify_dir EXIT

ENTRIES="$VERIFY_DIR/entries.txt"
unzip -Z1 "$APK" > "$ENTRIES" || die "could not list APK entries: $APK"

require_entry() {
    grep -qxF "$1" "$ENTRIES" || die "APK verification failed. Missing required entry: $1"
}

for asset in "${REQUIRED_ASSETS[@]}"; do
    grep -qxF "$asset" "$ENTRIES" ||
        die "APK verification failed. Missing required asset: $asset"
done

for target_abi in "${TARGET_ABIS[@]}"; do
    require_entry "lib/$target_abi/libgodot_android.so"
    for runtime_lib in "${REQUIRED_NATIVE_RUNTIME_LIBS[@]}"; do
        require_entry "lib/$target_abi/$runtime_lib"
    done
    if [ "$target_abi" = "arm64-v8a" ]; then
        for ported_lib in "${REQUIRED_PORTED_LIBS[@]}"; do
            require_entry "lib/$target_abi/$ported_lib"
        done
    fi

    extracted="$VERIFY_DIR/$target_abi-libgodot_android.so"
    unzip -p "$APK" "lib/$target_abi/libgodot_android.so" > "$extracted" ||
        die "could not extract lib/$target_abi/libgodot_android.so"
    grep -aqF "$PATCHED_MARKER" "$extracted" ||
        die "APK verification failed. lib/$target_abi/libgodot_android.so does not contain the app-data assembly lookup marker."
    if grep -aqF "$STALE_MARKER" "$extracted"; then
        die "APK verification failed. lib/$target_abi/libgodot_android.so still contains the stale PCK assembly lookup marker."
    fi
    rm -f "$extracted"
done

log "APK verification passed for ABIs: $(IFS=', '; printf '%s' "${TARGET_ABIS[*]}")"

# Port of Get-AndroidApkIdentity / Get-KeystoreSignerSha256 from
# scripts/android-signing-utils.ps1: confirm the manifest identity landed and
# that the APK is really signed by the keystore we were pointed at.
resolve_build_tool() {
    local name="$1" candidate
    candidate="$(ls -d "$ANDROID_HOME"/build-tools/*/"$name" 2>/dev/null | sort -V | tail -1)"
    if [ -n "$candidate" ] && [ -x "$candidate" ]; then
        printf '%s\n' "$candidate"
        return 0
    fi
    command -v "$name" 2>/dev/null && return 0
    return 1
}

normalize_sha256() { tr -d ': \t\r\n' | tr '[:lower:]' '[:upper:]'; }

AAPT="$(resolve_build_tool aapt || true)"
APKSIGNER="$(resolve_build_tool apksigner || true)"

if [ -n "$AAPT" ]; then
    badging="$("$AAPT" dump badging "$APK")" || die "aapt dump badging failed for $APK"
    actual_package="$(printf '%s' "$badging" | sed -n "s/^package: name='\([^']*\)'.*/\1/p")"
    actual_code="$(printf '%s' "$badging" | sed -n "s/^package:.* versionCode='\([^']*\)'.*/\1/p")"
    actual_name="$(printf '%s' "$badging" | sed -n "s/^package:.* versionName='\([^']*\)'.*/\1/p")"
    [ "$actual_package" = "$PACKAGE_NAME" ] ||
        die "APK verification failed. packageName is '$actual_package', expected '$PACKAGE_NAME'"
    [ "$actual_code" = "$VERSION_CODE" ] ||
        die "APK verification failed. versionCode is '$actual_code', expected '$VERSION_CODE'"
    [ "$actual_name" = "$VERSION_NAME" ] ||
        die "APK verification failed. versionName is '$actual_name', expected '$VERSION_NAME'"
    log "APK identity verified: $actual_package $actual_name ($actual_code)"
else
    warn "aapt not found under \$ANDROID_HOME/build-tools; skipped manifest identity check."
fi

if [ -n "$APKSIGNER" ]; then
    certs="$("$APKSIGNER" verify --print-certs --verbose "$APK")" ||
        die "APK signature verification failed for $APK"
    apk_signer_sha="$(printf '%s' "$certs" | sed -n 's/.*[Cc]ertificate SHA-256 digest: *\([A-Fa-f0-9]*\).*/\1/p' | head -1 | normalize_sha256)"
    [ -n "$apk_signer_sha" ] || die "Unable to read APK signer SHA-256 digest from: $APK"

    keystore_sha="$(
        "$JAVA_HOME/bin/keytool" -list -v \
            -keystore "$KEYSTORE_PATH" \
            -storepass "$KEYSTORE_PASSWORD" \
            -alias "$KEYSTORE_ALIAS" 2>/dev/null |
        sed -n 's/.*SHA256: *\([A-Fa-f0-9:]*\).*/\1/p' | head -1 | normalize_sha256
    )"
    [ -n "$keystore_sha" ] ||
        die "Could not read SHA256 certificate fingerprint from keytool output."
    [ "$apk_signer_sha" = "$keystore_sha" ] ||
        die "APK is signed by $apk_signer_sha but $KEYSTORE_ALIAS in $(basename "$KEYSTORE_PATH") is $keystore_sha"

    printf '%s\n' "$certs" | sed -n 's/^\(Verified using v[0-9]* scheme.*\)/APK signing: \1/p'
    log "APK signer matches keystore alias '$KEYSTORE_ALIAS' (SHA-256 $apk_signer_sha)"
else
    die "apksigner not found under \$ANDROID_HOME/build-tools; cannot verify the APK signature."
fi

# ---------------------------------------------------------------------------
# 9. Archive
# ---------------------------------------------------------------------------
safe_version_name="$(printf '%s' "$VERSION_NAME" | sed 's/[^A-Za-z0-9._-]/_/g')"
archived_apk="$ARTIFACT_DIR/StS2Portrait-v$safe_version_name-$ABI.apk"
mkdir -p "$ARTIFACT_DIR"
cp -f "$APK" "$archived_apk"
log ""
log "APK archived: $archived_apk"

apk_sha256="$(sha256sum "$archived_apk" | cut -d' ' -f1)"
checksum_path="$archived_apk.sha256"
printf '%s  %s\n' "$apk_sha256" "$(basename "$archived_apk")" > "$checksum_path"
log "APK checksum: $checksum_path"

metadata_path="$archived_apk.json"
VERSION_NAME="$VERSION_NAME" VERSION_CODE="$VERSION_CODE" PACKAGE_NAME="$PACKAGE_NAME" \
KEYSTORE_BASENAME="$(basename "$KEYSTORE_PATH")" ABI="$ABI" \
APK_BASENAME="$(basename "$archived_apk")" APK_SHA256="$apk_sha256" \
python3 - "$metadata_path" <<'PY'
import json, os, sys, datetime
metadata = {
    "versionName": os.environ["VERSION_NAME"],
    "versionCode": int(os.environ["VERSION_CODE"]),
    "packageName": os.environ["PACKAGE_NAME"],
    "signingChannel": "local",
    "keystore": os.environ["KEYSTORE_BASENAME"],
    "abi": os.environ["ABI"],
    "apk": os.environ["APK_BASENAME"],
    "sha256": os.environ["APK_SHA256"],
    "builtAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
}
with open(sys.argv[1], "w", encoding="utf-8") as handle:
    json.dump(metadata, handle, indent=4)
    handle.write("\n")
PY
log "APK metadata: $metadata_path"
log ""
log "Done."

# ---------------------------------------------------------------------------
# Deviations from scripts/build-android-local.ps1
# ---------------------------------------------------------------------------
# 1. Defaults are 0.4.0-dev19 / 40028 instead of 0.2.0-local / 200000.
# 2. Parameters are long flags plus STS2_* environment variables instead of
#    PowerShell param().
# 3. GRADLE_USER_HOME is pinned (default ~/.gradle) so gradle never caches onto
#    the ntfs3 mount the work tree lives on.
# 4. JAVA_HOME is probed for a JDK *17* rather than trusted blindly, because
#    archlinux-java's default can be a newer JDK than AGP 8.6.1 accepts.
# 5. Gradle resolution falls back to downloading the version pinned by
#    android/gradle/wrapper/gradle-wrapper.properties. ./gradlew cannot be used:
#    gradle-wrapper.jar is not checked in (*.jar is gitignored).
# 6. The patcher runs with DOTNET_ROLL_FORWARD=Major. It targets net9.0 and Arch
#    only ships the .NET 10 shared runtime.
# 7. The runtime version is read from the PE version resource with python3,
#    standing in for [FileVersionInfo]::GetVersionInfo().
# 8. Step 6 (NDK clang build of the FMOD JNI bridge) has no counterpart in this
#    fork's ps1. A missing NDK or missing source warns and keeps the staged .so;
#    a clang failure is fatal. native/monomodshim/monomodshim.c is only rebuilt
#    with --rebuild-monomod-shim so the default run keeps shipping the .so the
#    known-good builds used.
# 9. Verification is a superset of Test-AndroidApkContents: it additionally
#    requires libmonomodshim.so, libspine_godot.android.template_release.arm64.so
#    and libsts2fmodbridge.so, and it ports Get-AndroidApkIdentity /
#    Get-KeystoreSignerSha256 (aapt badging identity + apksigner/keytool
#    fingerprint match), which the ps1 build script itself never called.
# 10. A staged android/assets/dotnet_bcl/Sentry.dll is reported loudly. Like the
#    ps1 this script never copies Sentry.dll, but neither deletes staged files,
#    and anything left in that directory does get packaged.
