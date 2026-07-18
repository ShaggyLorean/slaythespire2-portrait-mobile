"""
Assemble the portrait Android app.

You provide two things this repo cannot ship: a community StS2 mobile launcher APK
(the engine runtime) and your own Slay the Spire 2 install. The script builds the
portrait mod, injects it into the launcher's managed entry, rebrands the APK
(name, icon, portrait orientation) and signs it.

    python3 build.py --launcher-apk /path/StS2Launcher.apk --game-dir "/path/Slay the Spire 2"

Needs python3, java 11+ and the .NET 9 SDK. Run tools/fetch-tools.sh once first.
"""
import argparse, os, re, shutil, subprocess, sys, tempfile, zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
TOOLS = ROOT / "tools" / "bin"
APKTOOL = TOOLS / "apktool.jar"
SIGNER = TOOLS / "uber-apk-signer.jar"
MOD_CSPROJ = ROOT / "src" / "Sts2Portrait" / "Sts2Portrait.csproj"
MOD_DLL = ROOT / "src" / "Sts2Portrait" / "bin" / "Release" / "net9.0" / "Sts2Portrait.dll"
INJECTOR = ROOT / "tools" / "mobile-injector"
PUBLISH_SUBDIR = "assets/dotnet_bcl"          # where the .NET assemblies live inside the APK
DISABLE_PATCHES = "UiScalePatches"            # launcher UI patches that fight our portrait layout


def run(cmd, **kw):
    print("  $", " ".join(str(c) for c in cmd))
    subprocess.run([str(c) for c in cmd], check=True, **kw)


def need(path, what):
    if not Path(path).exists():
        sys.exit(f"error: {what} not found: {path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--launcher-apk", required=True, help="community StS2 mobile launcher APK (you provide)")
    ap.add_argument("--game-dir", required=True, help="your Slay the Spire 2 install (has data_sts2_windows_x86_64)")
    ap.add_argument("--app-name", default="Slay the Spire 2")
    ap.add_argument("--output", default=str(ROOT / "dist" / "SlayTheSpire2-Portrait.apk"))
    ap.add_argument("--keep-work", action="store_true", help="don't delete the work dir (for debugging)")
    a = ap.parse_args()

    need(APKTOOL, "apktool.jar (run tools/fetch-tools.sh)")
    need(SIGNER, "uber-apk-signer.jar (run tools/fetch-tools.sh)")
    need(a.launcher_apk, "launcher APK")
    gamedata = Path(a.game_dir) / "data_sts2_windows_x86_64"
    need(gamedata, "game data dir (data_sts2_windows_x86_64)")

    work = Path(tempfile.mkdtemp(prefix="sts2p-build-"))
    dec = work / "apk"
    try:
        # 1. Build the portrait mod.
        print("[1/7] Building portrait mod...")
        run(["dotnet", "build", MOD_CSPROJ, "-c", "Release", "-v", "q", f"-p:GameDir={a.game_dir}"])
        need(MOD_DLL, "built mod DLL")

        # 2. Pull the launcher's own managed entry (STS2Mobile.dll) out of the APK.
        print("[2/7] Extracting engine layer from launcher APK...")
        with zipfile.ZipFile(a.launcher_apk) as z:
            names = [n for n in z.namelist() if n.startswith(PUBLISH_SUBDIR + "/") and n.endswith(".dll")]
            refs = work / "refs"
            refs.mkdir()
            for n in names:
                z.extract(n, work / "bcl_all")
            bcl = work / "bcl_all" / PUBLISH_SUBDIR
            for dll in bcl.glob("*.dll"):
                shutil.copy(dll, refs / dll.name)      # reference set for the injector
        sts2mobile = refs / "STS2Mobile.dll"
        need(sts2mobile, "STS2Mobile.dll inside the launcher APK")

        # 3. Inject PortraitMod.Init into STS2Mobile.ModEntry.Apply + disable conflicting UI patches.
        print("[3/7] Injecting portrait entry into the launcher's managed layer...")
        injected = work / "STS2Mobile.dll"
        search = ";".join([str(refs), str(gamedata), str(MOD_DLL.parent)])
        run(["dotnet", "run", "--project", INJECTOR, "-c", "Release", "--",
             str(sts2mobile), str(MOD_DLL), str(injected), search, DISABLE_PATCHES])
        need(injected, "injected STS2Mobile.dll")

        # 4. Decode the APK (resources only; keep dex byte-identical).
        print("[4/7] Decoding APK (apktool -s)...")
        run(["java", "-jar", APKTOOL, "d", "-s", "-f", a.launcher_apk, "-o", dec])

        # 5. Rebrand: label -> app name, orientation -> portrait, drop in our assemblies.
        print(f"[5/7] Rebranding to \"{a.app_name}\" + portrait...")
        manifest = dec / "AndroidManifest.xml"
        txt = manifest.read_text()
        txt = re.sub(r'android:label="[^"]*"', f'android:label="{a.app_name}"', txt)
        txt = txt.replace('android:screenOrientation="sensorLandscape"',
                          'android:screenOrientation="sensorPortrait"')
        manifest.write_text(txt)
        # custom icon (legacy + adaptive foreground/background layers)
        brand = ROOT / "branding"
        for name in ("icon.png", "icon_foreground.png", "icon_background.png"):
            src = brand / name
            if not src.exists():
                continue
            for dst in dec.glob(f"res/mipmap*/{name}"):
                shutil.copy(src, dst)
        bcl_out = dec / PUBLISH_SUBDIR
        shutil.copy(injected, bcl_out / "STS2Mobile.dll")
        shutil.copy(MOD_DLL, bcl_out / "Sts2Portrait.dll")

        # 6. Rebuild.
        print("[6/7] Rebuilding APK...")
        built = work / "unsigned.apk"
        run(["java", "-jar", APKTOOL, "b", dec, "-o", built])

        # 7. Zipalign + sign (v1/v2/v3) with a throwaway debug key.
        print("[7/7] Aligning + signing...")
        run(["java", "-jar", SIGNER, "-a", built, "--allowResign", "--overwrite"])

        out = Path(a.output)
        out.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy(built, out)
        print(f"\nDone -> {out}")
        print("Install:  adb install -r", out)
        print("It appears on the home screen as \"" + a.app_name + "\" and boots straight in portrait.")
        print("Game files: copy your SlayTheSpire2.pck + data_sts2_windows_x86_64 where the launcher")
        print("expects them (or use its Steam-login download), same as the stock launcher.")
    finally:
        if not a.keep_work:
            shutil.rmtree(work, ignore_errors=True)
        else:
            print("work dir:", work)


if __name__ == "__main__":
    main()
