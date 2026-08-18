param(
    [string]$GameDir = "D:\Projects\sts2-pctest-game",
    [string]$ShotsDir = "",
    [int]$FakeTopInsetPx = 140,
    [int]$TimeoutSec = 300,
    # Godot CLI wants WxH for --resolution and X,Y for --position.
    [string]$WindowSize = "1298x2856",
    [string]$WindowPosition = "10020,60"
)

# PC visual pre-screening runner (docs/TESTS.md).
#
# Stages the pctest mod into the game's user://mods, launches the sandboxed
# desktop game at a window whose width maps to the same portrait canvas bucket
# as the reference phone (1298 wide -> canvas 1180x2596), collects the
# screenshots the in-process driver captures, and always unstages the mod.
#
# The game is started from this non-interactive process with an off-screen
# window position, so it does not take keyboard focus from the user; all UI
# driving happens inside the game process, never through OS input.

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if (-not $ShotsDir) {
    $ShotsDir = Join-Path $root "tmp\pctest\shots-$(Get-Date -Format yyyyMMdd-HHmmss)"
}

$exe = Join-Path $GameDir "SlayTheSpire2.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Sandbox game not found: $exe"
}

# On desktop the game scans the "mods" directory NEXT TO THE EXECUTABLE
# (ModManager.Initialize), not user://mods; that path is Android-launcher only.
$modDir = Join-Path $GameDir "mods\sts2-portrait-pctest"
$modBin = Join-Path $root "tools\pctest-mod\bin\Release\net9.0"

Write-Host "Building STS2Mobile + pctest mod..."
dotnet build (Join-Path $root "src\STS2Mobile\STS2Mobile.csproj") -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "STS2Mobile build failed" }
dotnet build (Join-Path $root "tools\pctest-mod\Sts2PortraitPcTest.csproj") -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "pctest mod build failed" }

New-Item -ItemType Directory -Force $ShotsDir | Out-Null

Write-Host "Staging mod into $modDir"
if (Test-Path -LiteralPath $modDir) { Remove-Item -LiteralPath $modDir -Recurse -Force }
New-Item -ItemType Directory -Force $modDir | Out-Null
Copy-Item (Join-Path $root "tools\pctest-mod\manifest.json") $modDir
Copy-Item (Join-Path $modBin "sts2-portrait-pctest.dll") $modDir
Copy-Item (Join-Path $modBin "STS2Mobile.dll") $modDir

$env:STS2_PCTEST_OUT = $ShotsDir
$env:STS2_PORTRAIT_FAKE_TOP_INSET = "$FakeTopInsetPx"

# Pre-seed the steamless profile (user://default/1) so the run is headless:
# the mods warning and the Early Access popup would otherwise block the boot
# waiting for a click. This profile is only used by --force-steam=off runs and
# is separate from any Steam-account profile.
$settingsDir = Join-Path $env:APPDATA "SlayTheSpire2\default\1"
$settingsPath = Join-Path $settingsDir "settings.save"
New-Item -ItemType Directory -Force $settingsDir | Out-Null
$settings = if (Test-Path -LiteralPath $settingsPath) {
    Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
} else {
    [pscustomobject]@{}
}
$modSettings = [pscustomobject]@{
    mods_enabled = $true
}
$settings | Add-Member -NotePropertyName "mod_settings" -NotePropertyValue $modSettings -Force
$settings | Add-Member -NotePropertyName "seen_ea_disclaimer" -NotePropertyValue $true -Force
$settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

# Drop all profile state from previous rounds (runs live under
# default/1/modded/profile1) so every walk starts from a virgin main menu;
# otherwise the menu switches to Continue mode and the scripted scenario
# diverges. Steamless test profile only; Steam-account profiles are elsewhere.
foreach ($profileDir in @("modded", "profile1")) {
    Remove-Item (Join-Path $settingsDir $profileDir) -Recurse -Force -ErrorAction SilentlyContinue
}

$gameLog = Join-Path $ShotsDir "game-stdout.log"
$gameErr = Join-Path $ShotsDir "game-stderr.log"

try {
    Write-Host "Launching game (window $WindowSize at $WindowPosition, timeout ${TimeoutSec}s)..."
    # --force-steam=off is the game's own switch: skips Steamworks entirely,
    # so the sandbox never touches Steam or Steam Cloud.
    $proc = Start-Process -FilePath $exe `
        -ArgumentList @("--windowed", "--resolution", $WindowSize, "--position", $WindowPosition, "--verbose", "--force-steam=off") `
        -WorkingDirectory $GameDir `
        -RedirectStandardOutput $gameLog `
        -RedirectStandardError $gameErr `
        -PassThru

    if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
        Write-Host "Timeout reached; killing game process."
        try { $proc.Kill($true) } catch {}
    }
    Write-Host "Game exited with code $($proc.ExitCode)"
} finally {
    Remove-Item -LiteralPath $modDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:STS2_PCTEST_OUT -ErrorAction SilentlyContinue
    Remove-Item Env:STS2_PORTRAIT_FAKE_TOP_INSET -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Artifacts in ${ShotsDir}:"
Get-ChildItem $ShotsDir | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length/1KB)) KB)" }
