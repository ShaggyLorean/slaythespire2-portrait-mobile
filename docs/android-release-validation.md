# Android release validation checklist

Current posture: launcher-overhaul has an inherited ARM64 baseline, but the new portrait package has not yet produced an integrated APK. Treat the first `StS2 Portrait` APK as a fresh validation baseline and complete the portrait matrix in [portrait-design-audit.md](portrait-design-audit.md) before announcing it.

Use this checklist after every release run (manual or tag-triggered) to confirm artifact publication before announcing a release.

## 1) Verify workflow outcome

1. In GitHub Actions, open the latest **Android Release** run.
2. Confirm both jobs completed:
   - `Build Android APK`
   - `Publish GitHub release`
3. Confirm the `Verify release update compatibility` step completed.
4. Confirm the output APK path is present in logs:
   - `android/build/outputs/apk/mono/release/StS2Portrait-v<version>.apk`

## 2) Verify update guardrails

1. Confirm repository secrets are configured:
   - `ANDROID_RELEASE_KEYSTORE_BASE64`
   - `ANDROID_RELEASE_KEYSTORE_PASSWORD`
   - `ANDROID_RELEASE_KEY_ALIAS`
2. Confirm repository variable `ANDROID_RELEASE_SIGNER_SHA256` is configured to the stable release certificate SHA-256 fingerprint.
3. Audit the current repository setup:

```powershell
.\scripts\check-android-release-readiness.ps1
```

4. If these are missing or stale, configure them from the stable release keystore:

```powershell
.\scripts\configure-android-release-signing.ps1 `
  -KeystorePath C:\path\to\release.keystore `
  -KeystorePassword "<password>" `
  -KeyAlias "<alias>"
```

5. Confirm the compatibility step reports the same package name as the installed release package.
6. Confirm the compatibility step reports the same signer SHA-256 as `ANDROID_RELEASE_SIGNER_SHA256`.
7. Confirm the compatibility step reports a higher `versionCode` than the baseline APK.
8. Do not announce the APK as update-compatible if the run used `allow_update_baseline_reset=true`; that mode creates a new stable baseline and cannot update APKs signed by a different previous key.

## 3) Verify published release assets

1. Open the release page for the tag (for example `v0.2.0`).
2. Confirm at least one APK asset exists with name pattern:
   - Current ARM64 releases: `StS2Portrait-v<version>-arm64-v8a.apk`
3. Confirm checksum file exists:
   - `StS2Portrait-v<version>-arm64-v8a.apk.sha256`
4. Confirm the release body includes generated release notes.
5. Confirm the release notes distinguish the current working ARM64 path from release-candidate status. Do not claim release-ready cloud sync until Pull and Push have both been validated, including no-accidental-upload behavior for Push.

## 4) Structural asset verification

Run the release verifier against the exact release tag and asset:

```powershell
.\scripts\verify-android-release-apk.ps1 `
  -ReleaseTag "v0.1.0" `
  -AssetName "StS2Portrait-v0.1.0-arm64-v8a.apk" `
  -Abi arm64-v8a
```

For manual checksum verification after downloading the `.sha256` sidecar:

```bash
sha256sum -c StS2Portrait-v<version>-arm64-v8a.apk.sha256
```

Expected output:

```text
StS2Portrait-v<version>-arm64-v8a.apk: OK
```

## 5) Install validation on device

```powershell
.\scripts\install-android-release.ps1 `
  -ReleaseTag "v0.1.0" `
  -AssetName "StS2Portrait-v0.1.0-arm64-v8a.apk" `
  -ClearAppData `
  -Launch `
  -CaptureDiagnostics
```

1. Start app and confirm launcher UI appears.
2. Confirm no immediate crash on cold start.
3. Confirm native splash, launcher loading/warmup, and settled launcher surfaces do not clip or distort on the target screen.
4. Confirm Steam login reaches authentication success or ownership verification.
5. Confirm game download works when game files are absent.
6. Confirm Pull from Cloud downloads real Steam Cloud files, writes Android local saves, and the game reads/surfaces the pulled profile.
7. Confirm Push to Cloud requires confirmation, cancel/no-confirm does not upload, and confirmed Push behavior is explicitly validated or deferred with overwrite-risk rationale.
8. Confirm locked-screen/focus interruption and upgrade install behavior for every new release candidate before calling it ready.

## 6) Archive and follow-up

- Record any failures in the release PR or issue tracker.
- Fix root cause before creating the next tag.
- Add a short note to `OVERHAUL_STATUS.md` if a process step changes.
