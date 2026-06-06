#!/bin/bash
# Rhythm Doctor Trainer · macOS 一键安装（原生，无需 BepInEx / Rosetta）
#
# 懒人一键（在「终端」粘贴运行）：
#   curl -fsSL https://raw.githubusercontent.com/MrTangLuyao/RhythmDoctorTrainerMac/refs/heads/main/install.sh | bash
#
# 脚本会自动：装 .NET SDK（若无）→ 拉取源码 → 编译 → 备份游戏文件 → 织入加载器。
# 游戏更新或 Steam「验证文件完整性」后重跑本命令即可。
set -euo pipefail

REPO_GIT="https://github.com/MrTangLuyao/RhythmDoctorTrainerMac.git"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

# --- self-bootstrap: when piped via `curl | bash` there's no source tree, so clone it & re-exec ---
SELF="${BASH_SOURCE[0]:-$0}"
if [ -f "$SELF" ]; then HERE="$(cd "$(dirname "$SELF")" && pwd)"; else HERE=""; fi
if [ -z "$HERE" ] || [ ! -f "$HERE/mac/RDTrainerMac/RDTrainerMac.csproj" ]; then
  if ! command -v git >/dev/null 2>&1; then
    echo "ERROR: 需要 git。先运行：xcode-select --install"; exit 1
  fi
  SRC="${RDT_SRC:-$HOME/.rdtrainer-mac/src}"
  echo "==> 获取源码 -> $SRC"
  if [ -d "$SRC/.git" ]; then git -C "$SRC" pull --ff-only || true
  else mkdir -p "$(dirname "$SRC")"; git clone --depth 1 "$REPO_GIT" "$SRC"; fi
  exec bash "$SRC/install.sh" "$@"
fi

# --- ensure .NET SDK (installs locally to ~/.dotnet, no sudo, fully removable) ---
if ! command -v dotnet >/dev/null 2>&1; then
  echo "==> 未发现 .NET SDK，自动安装到 ~/.dotnet（约 200MB，一次性）…"
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet" --no-path
  export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
fi

# --- locate the game's Managed folder (override: MANAGED=... ) ---
MANAGED="${MANAGED:-$HOME/Library/Application Support/Steam/steamapps/common/Rhythm Doctor/Rhythm Doctor.app/Contents/Resources/Data/Managed}"
if [ ! -f "$MANAGED/Assembly-CSharp.dll" ]; then
  echo "ERROR: 找不到 Assembly-CSharp.dll："; echo "  $MANAGED"
  echo "用 MANAGED=/path/.../Data/Managed 指定游戏路径后重试。"; exit 1
fi

# --- refuse to patch while the game is running (the DLL is mapped in memory) ---
if pgrep -f "Rhythm Doctor.app/Contents/MacOS/Rhythm Doctor" >/dev/null 2>&1; then
  echo "ERROR: 游戏正在运行，请先退出再安装。"; exit 1
fi

echo "==> 编译修改器 DLL"
dotnet build -c Release "$HERE/mac/RDTrainerMac/RDTrainerMac.csproj" -p:Managed="$MANAGED" | tail -3

HARMONY="$(ls "$HOME"/.nuget/packages/lib.harmony/*/lib/net472/0Harmony.dll 2>/dev/null | sort -V | tail -1 || true)"
if [ -z "${HARMONY:-}" ] || [ ! -f "$HARMONY" ]; then
  echo "ERROR: NuGet 缓存里找不到 net472 0Harmony.dll。"; exit 1
fi

echo "==> 织入 Assembly-CSharp.dll（把 Loader.Init 织进 RDStartup.Setup）"
dotnet run -c Release --project "$HERE/mac/Patcher/Patcher.csproj" -- \
  "$MANAGED" \
  "$HERE/mac/RDTrainerMac/bin/Release/RDTrainerMac.dll" \
  "$HARMONY"

cat <<EOF

==> 完成！正常通过 Steam 启动游戏，进任意关卡按 F3 开/关菜单。
    验证：  grep RDTrainerMac "\$HOME/Library/Logs/7th Beat Games/Rhythm Doctor/Player.log"
    卸载：  curl -fsSL https://raw.githubusercontent.com/MrTangLuyao/RhythmDoctorTrainerMac/refs/heads/main/uninstall.sh | bash
    NOTE: 游戏更新或 Steam「验证文件完整性」会还原补丁 —— 重跑安装命令即可。
EOF
