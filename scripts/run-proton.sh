#!/usr/bin/env bash
# StS2'yi GE-Proton ile izole bir prefix'te, tam logging açık çalıştırır.
# Kullanım: run-proton.sh [--rendering-driver vulkan] [oyun argümanları...]
set -uo pipefail

_D="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"; [ -f "$_D/local.env" ] && . "$_D/local.env"
GAME_DIR="${GAME_DIR:-}"; [ -n "$GAME_DIR" ] || { echo "HATA: GAME_DIR tanımsız — scripts/local.env oluşturun" >&2; exit 1; }
EXE="$GAME_DIR/SlayTheSpire2.exe"
PROTON="${PROTON:-$HOME/.steam/steam/compatibilitytools.d/GE-Proton11-1/proton}"
PREFIX="${PREFIX:-$HOME/sts2-portrait/proton-prefix}"
LOGDIR="${LOGDIR:-$HOME/sts2-portrait/shots}"

mkdir -p "$PREFIX"

export STEAM_COMPAT_CLIENT_INSTALL_PATH="$HOME/.steam/steam"
export STEAM_COMPAT_DATA_PATH="$PREFIX"
# OnlineFix crack winmm proxy'sinin yüklenmesi için:
export WINEDLLOVERRIDES="winmm=n,b"
# Proton detaylı log:
export PROTON_LOG=1
export PROTON_LOG_DIR="$LOGDIR"
export WINEDEBUG="${WINEDEBUG:-fixme-all,+seh}"

cd "$GAME_DIR" || exit 1
exec "$PROTON" run "$EXE" "$@"
