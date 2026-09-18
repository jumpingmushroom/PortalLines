#!/usr/bin/env bash
# Build PortalLines and copy it into the r2modman profile on the target machine.
#
#   ./build/deploy.sh                 # build + deploy to the default target
#   VALHEIM_SSH=user@host ./build/deploy.sh
#   ./build/deploy.sh --local /path/to/profile   # deploy to a local profile instead
set -euo pipefail

# The SDK on the build box has no ICU; without this dotnet aborts before parsing arguments.
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJ="$ROOT/src/PortalLines/PortalLines.csproj"
OUT="$ROOT/src/PortalLines/bin/Release/net472/PortalLines.dll"

SSH_TARGET="${VALHEIM_SSH:-equ@192.168.1.160}"
PROFILE="${VALHEIM_PROFILE:-/home/equ/.config/r2modmanPlus-local/Valheim/profiles/Mods}"
PLUGIN_DIR="$PROFILE/BepInEx/plugins/PortalLines"

echo "==> building"
dotnet build "$PROJ" -c Release --nologo -v minimal

[ -f "$OUT" ] || { echo "build produced no DLL at $OUT" >&2; exit 1; }

if [ "${1:-}" = "--local" ]; then
    DEST="${2:?--local needs a profile path}/BepInEx/plugins/PortalLines"
    echo "==> deploying to $DEST"
    mkdir -p "$DEST"
    cp "$OUT" "$DEST/"
else
    echo "==> deploying to $SSH_TARGET:$PLUGIN_DIR"
    # Replace atomically. Mono memory-maps plugin DLLs, so overwriting the file in place while
    # the game is running corrupts the loaded image (BadImageFormatException: bad method token
    # at the next reflection, e.g. Jotunn reading attributes on connect). A rename swaps the
    # directory entry and leaves the running game's mapping on the old inode.
    ssh "$SSH_TARGET" "mkdir -p '$PLUGIN_DIR'"
    scp -q "$OUT" "$SSH_TARGET:$PLUGIN_DIR/.PortalLines.dll.new"
    ssh "$SSH_TARGET" "mv -f '$PLUGIN_DIR/.PortalLines.dll.new' '$PLUGIN_DIR/PortalLines.dll'; \
        pgrep -x valheim.x86_64 >/dev/null && echo '    game is running: the new DLL is used on the next launch' || true"
fi

echo "==> done: $(basename "$OUT") $(stat -c%s "$OUT") bytes"
