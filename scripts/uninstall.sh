#!/usr/bin/env bash
# 卸载 VPet 桌宠
#
# 用法: ./scripts/uninstall.sh [安装目录]
set -euo pipefail

INSTALL_DIR="${1:-${XDG_DATA_HOME:-$HOME/.local/share}/vpet}"
DESKTOP_DIR="${XDG_DESKTOP_DIR:-$HOME/Desktop}"
[[ -d "$HOME/桌面" ]] && DESKTOP_DIR="$HOME/桌面"

echo "[uninstall] 移除程序目录: $INSTALL_DIR"
rm -rf "$INSTALL_DIR"

echo "[uninstall] 移除桌面快捷方式"
rm -f "$DESKTOP_DIR/VPet.desktop"
rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/applications/vpet.desktop"
rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps/vpet.png"
rm -f "$HOME/bin/vpet"

echo "[uninstall] 完成。用户存档数据如需一并清除, 请手动删除桌宠自定义的存档目录。"
