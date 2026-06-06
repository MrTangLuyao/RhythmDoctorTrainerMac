#!/bin/bash
# Rhythm Doctor Trainer · macOS 一键安装（原生，无需 BepInEx / Rosetta）
# Build the trainer, back up the game assembly, and weave the loader into RDStartup.Setup.
# Re-run any time after a game update or Steam "verify integrity of game files".
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: .NET SDK not found. Install it (one-off):"
  echo "  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir \"\$HOME/.dotnet\""
  exit 1
fi

# --- locate the game's Managed folder (override: MANAGED=... ./install.sh) ---
MANAGED="${MANAGED:-$HOME/Library/Application Support/Steam/steamapps/common/Rhythm Doctor/Rhythm Doctor.app/Contents/Resources/Data/Managed}"
if [ ! -f "$MANAGED/Assembly-CSharp.dll" ]; then
  echo "ERROR: Assembly-CSharp.dll not found under:"; echo "  $MANAGED"
  echo "Set MANAGED=/path/to/.../Data/Managed and re-run."; exit 1
fi

# --- refuse to patch while the game is running (the DLL is mapped in memory) ---
if pgrep -f "Rhythm Doctor.app/Contents/MacOS/Rhythm Doctor" >/dev/null 2>&1; then
  echo "ERROR: Rhythm Doctor is running. Quit it first, then re-run."; exit 1
fi

echo "==> Building trainer DLL"
dotnet build -c Release "$HERE/mac/RDTrainerMac/RDTrainerMac.csproj" -p:Managed="$MANAGED" | tail -3

HARMONY="$(ls "$HOME"/.nuget/packages/lib.harmony/*/lib/net472/0Harmony.dll 2>/dev/null | sort -V | tail -1 || true)"
if [ -z "${HARMONY:-}" ] || [ ! -f "$HARMONY" ]; then
  echo "ERROR: could not find net472 0Harmony.dll in the NuGet cache."; exit 1
fi

echo "==> Patching Assembly-CSharp.dll (weaving Loader.Init into RDStartup.Setup)"
dotnet run -c Release --project "$HERE/mac/Patcher/Patcher.csproj" -- \
  "$MANAGED" \
  "$HERE/mac/RDTrainerMac/bin/Release/RDTrainerMac.dll" \
  "$HARMONY"

cat <<EOF

==> Done. 正常通过 Steam 启动游戏，进任意关卡按 F3 开/关菜单。
    Verify:    grep RDTrainerMac "\$HOME/Library/Logs/7th Beat Games/Rhythm Doctor/Player.log"
    Uninstall: "$HERE/uninstall.sh"
    NOTE: 游戏更新或 Steam「验证文件完整性」会还原补丁 —— 重新跑一次本脚本即可。
EOF
