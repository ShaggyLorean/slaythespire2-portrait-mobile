#!/usr/bin/env bash
# Modu derleyip oyunun mods/ klasörüne kurar (PC geliştirme döngüsü).
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
_D="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"; [ -f "$_D/local.env" ] && . "$_D/local.env"
GAME="${GAME:-}"; [ -n "$GAME" ] || echo "HATA: GAME tanımsız — scripts/local.env oluşturun (bkz. README)" >&2
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"

dotnet build "$REPO/src/Sts2Portrait/Sts2Portrait.csproj" -c Release -v q

MOD_DIR="$GAME/mods/Sts2Portrait"
mkdir -p "$MOD_DIR"
cp "$REPO/src/Sts2Portrait/bin/Release/net9.0/Sts2Portrait.dll" "$MOD_DIR/"
cp "$REPO/src/Sts2Portrait/manifest/Sts2Portrait.json" "$MOD_DIR/"

echo "OK -> $MOD_DIR"
ls -la "$MOD_DIR"
