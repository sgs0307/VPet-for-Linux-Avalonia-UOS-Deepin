#!/usr/bin/env bash
# 安装 VPet 桌宠到 ~/.local/share/vpet, 并生成桌面快捷方式
#
# 用法: ./scripts/install.sh [安装目录]
# 默认安装目录: ${XDG_DATA_HOME:-$HOME/.local/share}/vpet
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$ROOT/publish"
INSTALL_DIR="${1:-${XDG_DATA_HOME:-$HOME/.local/share}/vpet}"
DESKTOP_DIR="${XDG_DESKTOP_DIR:-$HOME/Desktop}"

if [[ ! -d "$SRC" ]]; then
  echo "错误: 发布目录不存在: $SRC"
  echo "请先运行 ./scripts/build.sh"
  exit 1
fi

# 中文桌面目录(统信UOS/Deepin 常见 ~/桌面)
if [[ -d "$HOME/桌面" ]]; then
  DESKTOP_DIR="$HOME/桌面"
fi

# ---- 定位 dotnet ----
DOTNET_BIN="${VPET_DOTNET:-}"
if [[ -z "$DOTNET_BIN" ]]; then
  if command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN="$(command -v dotnet)"
  else
    for c in "$HOME/dotnet/dotnet" "/usr/share/dotnet/dotnet" "/usr/lib/dotnet/dotnet" "/opt/dotnet/dotnet" "/usr/bin/dotnet"; do
      if [[ -x "$c" ]]; then DOTNET_BIN="$c"; break; fi
    done
  fi
fi
if [[ -z "$DOTNET_BIN" || ! -x "$DOTNET_BIN" ]]; then
  echo "错误: 未找到 dotnet(.NET 8 运行时)。可用 VPET_DOTNET=/path/to/dotnet 指定。"
  exit 1
fi
echo "[install] dotnet: $DOTNET_BIN"

# ---- 结束正在运行的实例 ----
# 覆盖安装时如果桌宠还在跑: cp 会原地 truncate 同名 dll, 而进程已经把这些 dll
# mmap 进内存, 内核会立刻给进程发 SIGBUS。表现是宠物界面僵在原地不动、点不动,
# 日志里只有一行 "总线错误(核心已转储)"(coredumpctl 里 signal 7)。
# 所以装之前先优雅结束(TERM 会走正常退出流程保存存档), 等不到再 KILL。
running_pids() {
  pgrep -f -- "$INSTALL_DIR/VPet-Avalonia.App" 2>/dev/null || true
  pgrep -f -- "$INSTALL_DIR/vpet.sh" 2>/dev/null || true
}

pids="$(running_pids | sort -u | tr '\n' ' ')"
if [[ -n "${pids// /}" ]]; then
  echo "[install] 检测到运行中的实例: $pids"
  echo "[install] 先结束它们(正常退出会保存存档)"
  # shellcheck disable=SC2086
  kill -TERM $pids 2>/dev/null || true
  for _ in $(seq 1 24); do
    sleep 0.25
    [[ -z "$(running_pids)" ]] && break
  done
  left="$(running_pids | sort -u | tr '\n' ' ')"
  if [[ -n "${left// /}" ]]; then
    echo "[install] 仍未退出, 强制结束: $left"
    # shellcheck disable=SC2086
    kill -KILL $left 2>/dev/null || true
    sleep 0.5
  fi
fi

# ---- 拷贝程序 ----
echo "[install] 安装目录: $INSTALL_DIR"
# VPET_SKIP_CLEAN=1 时不清空旧目录, 只覆盖安装(升级/重跑时用, 避免整目录删除)
if [[ "${VPET_SKIP_CLEAN:-0}" != "1" && -d "$INSTALL_DIR" ]]; then
  rm -rf "$INSTALL_DIR"
fi
mkdir -p "$INSTALL_DIR"
# 优先 rsync: 它默认"写临时文件再 rename 替换", 不会 truncate 正在被 mmap 的 dll;
# 没有 rsync 时退回 cp -a(上面已把实例结束, 此时覆盖是安全的)。
if command -v rsync >/dev/null 2>&1; then
  rsync -a "$SRC"/ "$INSTALL_DIR"/
else
  cp -a "$SRC"/. "$INSTALL_DIR"/
fi
[[ -d "$INSTALL_DIR/mod" ]] || echo "[install] 警告: 发布目录没有 mod, 桌宠资源缺失时将无法加载宠物"

# ---- 生成启动器 ----
sed -e "s|@INSTALL_DIR@|$INSTALL_DIR|g" -e "s|@DOTNET_BIN@|$DOTNET_BIN|g" \
  "$ROOT/scripts/vpet.sh.in" > "$INSTALL_DIR/vpet.sh"
chmod +x "$INSTALL_DIR/vpet.sh"

# ---- 安装图标 ----
ICON_DIR="$INSTALL_DIR/assets/icon"
mkdir -p "$ICON_DIR"
cp -f "$ROOT/assets/icon/vpet.png" "$ICON_DIR/vpet.png" 2>/dev/null || true
mkdir -p "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
cp -f "$ROOT/assets/icon/vpet.png" "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps/vpet.png" 2>/dev/null || true
ICON_PATH="$ICON_DIR/vpet.png"

# ---- 桌面快捷方式 ----
TMP_DESKTOP="$(mktemp)"
sed -e "s|@INSTALL_DIR@|$INSTALL_DIR|g" -e "s|@ICON_PATH@|$ICON_PATH|g" \
  "$ROOT/packaging/vpet.desktop" > "$TMP_DESKTOP"

mkdir -p "${XDG_DATA_HOME:-$HOME/.local/share}/applications" "$DESKTOP_DIR"
cp -f "$TMP_DESKTOP" "${XDG_DATA_HOME:-$HOME/.local/share}/applications/vpet.desktop"
cp -f "$TMP_DESKTOP" "$DESKTOP_DIR/VPet.desktop"
chmod +x "${XDG_DATA_HOME:-$HOME/.local/share}/applications/vpet.desktop" "$DESKTOP_DIR/VPet.desktop"
rm -f "$TMP_DESKTOP"

# Deepin/UOS 需要把桌面文件标记为可信任, 否则双击会弹"是否信任"
if command -v gio >/dev/null 2>&1; then
  gio set "$DESKTOP_DIR/VPet.desktop" metadata::trusted true 2>/dev/null || true
fi

# 顺手在 ~/bin 放一个命令行入口(若该目录在 PATH 中)
if [[ -d "$HOME/bin" ]]; then
  ln -sf "$INSTALL_DIR/vpet.sh" "$HOME/bin/vpet" 2>/dev/null || true
fi

echo ""
echo "[install] 安装完成"
echo "  程序目录 : $INSTALL_DIR"
echo "  启动脚本 : $INSTALL_DIR/vpet.sh"
echo "  桌面图标 : $DESKTOP_DIR/VPet.desktop"
echo "  应用菜单 : ${XDG_DATA_HOME:-$HOME/.local/share}/applications/vpet.desktop"
echo ""
echo "现在可以直接双击桌面上的「VPet 桌宠」图标运行。"
