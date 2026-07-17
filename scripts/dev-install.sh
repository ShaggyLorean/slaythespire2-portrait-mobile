#!/usr/bin/env bash
# Modu derleyip oyunun mods/ klasörüne kurar (PC geliştirme döngüsü).
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
GAME="${GAME:-/home/whispersgone/Downloads/Slay-the-Spire-2-AnkerGames/Slay the Spire 2}"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

dotnet build "$REPO/src/Sts2Portrait/Sts2Portrait.csproj" -c Release -v q

MOD_DIR="$GAME/mods/Sts2Portrait"
mkdir -p "$MOD_DIR"
cp "$REPO/src/Sts2Portrait/bin/Release/net9.0/Sts2Portrait.dll" "$MOD_DIR/"
cp "$REPO/src/Sts2Portrait/manifest/Sts2Portrait.json" "$MOD_DIR/"

echo "OK -> $MOD_DIR"
ls -la "$MOD_DIR"
