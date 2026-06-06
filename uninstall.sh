#!/bin/bash
# Rhythm Doctor Trainer · macOS 一键卸载 —— 还原游戏到原版。
set -euo pipefail

MANAGED="${MANAGED:-$HOME/Library/Application Support/Steam/steamapps/common/Rhythm Doctor/Rhythm Doctor.app/Contents/Resources/Data/Managed}"
BK="$MANAGED/Assembly-CSharp.dll.rdtrainer-backup"

if pgrep -f "Rhythm Doctor.app/Contents/MacOS/Rhythm Doctor" >/dev/null 2>&1; then
  echo "ERROR: Rhythm Doctor is running. Quit it first, then re-run."; exit 1
fi

if [ -f "$BK" ]; then
  cp -f "$BK" "$MANAGED/Assembly-CSharp.dll"
  rm -f "$BK"
  echo "restored Assembly-CSharp.dll from backup"
else
  echo "no backup found; if still patched, use Steam → Verify integrity of game files"
fi
rm -f "$MANAGED/RDTrainerMac.dll" "$MANAGED/0Harmony.dll"
echo "removed RDTrainerMac.dll + 0Harmony.dll"
echo "Done — 游戏已还原为原版。"
