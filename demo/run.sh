#!/usr/bin/env bash
#
# Build ConfigKit and the demo mods, put them in a Cairn pack, and launch it.
#
# The pack is the point: Cairn owns the game install and a clean data directory, so this
# never touches the real game or the mods you actually play with. Re-run it after any
# change - it rebuilds and replaces the zips in place.
#
#   demo/run.sh              build, sync the pack, launch
#   demo/run.sh --no-launch  build and sync only
#   demo/run.sh --reset      throw away the pack's saved config and world first
#
set -euo pipefail

PACK=configkitdemo
CAIRN="${CAIRN:-$HOME/src/cairn/artifacts/osx-arm64/cairn-cli}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"

launch=1
reset=0
for arg in "$@"; do
    case "$arg" in
        --no-launch) launch=0 ;;
        --reset)     reset=1 ;;
        *) echo "unknown argument: $arg" >&2; exit 2 ;;
    esac
done

[ -x "$CAIRN" ] || {
    echo "cairn-cli not found at $CAIRN" >&2
    echo "build it with: cd ~/src/cairn && ./dev.sh --cli" >&2
    exit 1
}

# The newest install Cairn has, rather than a pinned point release.
export VINTAGE_STORY="${VINTAGE_STORY:-$(ls -d "$HOME"/.cairn/games/*.app 2>/dev/null | sort -V | tail -1)}"
[ -n "$VINTAGE_STORY" ] || { echo "no game install under ~/.cairn/games" >&2; exit 1; }

GAME="$(basename "$VINTAGE_STORY" .app)"
echo "game     $GAME"
echo "install  $VINTAGE_STORY"

# ---------------------------------------------------------------- build

echo
echo "building..."
dotnet build "$ROOT/configkit/configkit.csproj"            -c Debug -v quiet --nologo
dotnet build "$HERE/configkitdemo/configkitdemo.csproj"    -c Debug -v quiet --nologo

# ---------------------------------------------------------------- the pack

PACKS="$HOME/.cairn/packs/$PACK"

if [ ! -f "$PACKS/pack.json" ]; then
    echo
    echo "creating pack '$PACK' on $GAME"
    "$CAIRN" init "ConfigKit demo" --id "$PACK" --game "$GAME"
fi

if [ "$reset" = 1 ]; then
    echo "resetting the pack's data directory"
    rm -rf "$PACKS/data"
fi

mkdir -p "$PACKS/Mods"
rm -f "$PACKS/Mods"/configkit_*.zip "$PACKS/Mods"/configkitdemo*.zip

zip_mod() {
    local from="$1" name="$2"
    ( cd "$from" && zip -qr "$PACKS/Mods/$name.zip" . )
    echo "  $name.zip"
}

echo
echo "packing:"
zip_mod "$ROOT/configkit/bin/Debug/Mods/mod"        configkit_dev
zip_mod "$HERE/configkitdemo/bin/Debug/Mods/mod"    configkitdemo_dev
zip_mod "$HERE/configkitdemodef"                    configkitdemodef_dev

# ---------------------------------------------------------------- go

if [ "$launch" = 0 ]; then
    echo
    echo "pack ready. launch it with: $CAIRN launch $PACK"
    exit 0
fi

echo
echo "launching. press P in game, or use Mod settings in the pause menu."
exec "$CAIRN" launch "$PACK"
