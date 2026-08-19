# Builds the Spine GDExtension for Android arm64.
#
# The game draws every character, the logo and all VFX through Spine, which
# ships as a GDExtension. The Steam build only carries Windows, Linux and macOS
# libraries, so on Android the class SpineSkeletonDataResource does not exist
# and everything Spine-backed renders as nothing: no logo, no character art, no
# effects. This produces the missing arm64 library.
#
# The binary is deliberately not committed. The Spine Runtimes License requires
# every user of the runtime to hold their own Spine license, so each person
# builds it locally from Esoteric Software's sources.
#
# Skeleton data in the game is version 4.2.43, so the runtime must be 4.2.

param(
    [string]$WorkDir = "$PSScriptRoot\..\..\spine-android-build",
    [string]$SpineBranch = "4.2",
    [string]$GodotCppBranch = "4.5",
    [string]$NdkVersion = "28.1.13356709",
    [string]$AndroidHome = $(if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "$env:LOCALAPPDATA\Android\Sdk" })
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$WorkDir = [System.IO.Path]::GetFullPath($WorkDir)
$ndkPath = Join-Path $AndroidHome "ndk\$NdkVersion"

if (-not (Test-Path -LiteralPath $ndkPath)) {
    throw "Android NDK $NdkVersion not found at $ndkPath. Install it from the SDK manager or pass -NdkVersion."
}

python -c "import SCons" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installing SCons..."
    python -m pip install --quiet scons
    if ($LASTEXITCODE -ne 0) { throw "Could not install SCons" }
}

$spineDir = Join-Path $WorkDir "spine-runtimes"
if (-not (Test-Path -LiteralPath $spineDir)) {
    New-Item -ItemType Directory -Force $WorkDir | Out-Null
    Write-Host "Cloning spine-runtimes $SpineBranch..."
    git clone --depth 1 --branch $SpineBranch https://github.com/EsotericSoftware/spine-runtimes.git $spineDir
    if ($LASTEXITCODE -ne 0) { throw "spine-runtimes clone failed" }
}

$godotDir = Join-Path $spineDir "spine-godot"
$cppDir = Join-Path $godotDir "godot-cpp"
if (-not (Test-Path -LiteralPath $cppDir)) {
    Write-Host "Cloning godot-cpp $GodotCppBranch..."
    git clone --depth 1 --branch $GodotCppBranch https://github.com/godotengine/godot-cpp.git $cppDir
    if ($LASTEXITCODE -ne 0) { throw "godot-cpp clone failed" }
}

# The build expects the C++ runtime sources next to the Godot glue code; this is
# what spine's own setup-extension.sh does before invoking SCons.
$spineCppTarget = Join-Path $godotDir "spine_godot\spine-cpp"
if (-not (Test-Path -LiteralPath $spineCppTarget)) {
    Copy-Item -Recurse (Join-Path $spineDir "spine-cpp\spine-cpp") $spineCppTarget
}

Push-Location $godotDir
try {
    $env:ANDROID_HOME = $AndroidHome
    python -m SCons platform=android arch=arm64 target=template_release ndk_version=$NdkVersion -j $env:NUMBER_OF_PROCESSORS
    if ($LASTEXITCODE -ne 0) { throw "SCons build failed" }
}
finally {
    Pop-Location
}

$built = Join-Path $godotDir "bin\android\libspine_godot.android.template_release.arm64.so"
if (-not (Test-Path -LiteralPath $built)) { throw "Build produced no library at $built" }

$dest = Join-Path $root "android\libs\release\arm64-v8a"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item -Force $built $dest

Write-Host ""
Write-Host "Spine GDExtension built for Android arm64:"
Write-Host "  source: $built"
Write-Host "  staged: $(Join-Path $dest 'libspine_godot.android.template_release.arm64.so')"
Write-Host "Rebuild the APK with scripts\build-android-local.ps1 to pick it up."
