#!/usr/bin/env bash
# 安装 VPet 桌宠到 ~/.local/share/vpet, 并生成桌面快捷方式
#
# 用法: ./scripts/install.sh [安装目录]
# 默认安装目录: ${XDG_DATA_HOME:-$HOME/.local/share}/vpet
#
# 用 sh install.sh 调用也能跑: 很多用户习惯敲 sh, 而 /bin/sh 在 UOS/Deepin 上是 dash,
# dash 不支持 pipefail, 会直接报 "set: Illegal option -o pipefail" 然后什么都不做。
# 这里检测到不是 bash 就自己重新用 bash 执行一遍。
if [ -z "${BASH_VERSION:-}" ]; then
  exec bash "$0" "$@"
fi
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="$ROOT/publish"
INSTALL_DIR="${1:-${XDG_DATA_HOME:-$HOME/.local/share}/vpet}"
DESKTOP_DIR="${XDG_DESKTOP_DIR:-$HOME/Desktop}"

# 安装目录统一转绝对路径: 桌面快捷方式的 Exec 会写死这个路径,
# 传相对路径(./scripts/install.sh ./app)时生成的 Exec 是相对路径, 换个目录启动就失效。
# realpath -m 允许目标不存在, 失败(极老系统)就退回原样。
INSTALL_DIR="$(realpath -m -- "$INSTALL_DIR" 2>/dev/null || echo "$INSTALL_DIR")"

if [[ ! -d "$SRC" ]]; then
  echo "错误: 发布目录不存在: $SRC"
  echo "请先运行 ./scripts/build.sh"
  exit 1
fi

# 中文桌面目录(统信UOS/Deepin 常见 ~/桌面)
if [[ -d "$HOME/桌面" ]]; then
  DESKTOP_DIR="$HOME/桌面"
fi

# ---- 是否需要外部 .NET 运行时 ----
# 自包含包把运行时一起打进了发布目录, runtimeconfig.json 里写的是 includedFrameworks
# 而不是 framework。这种包在没装 .NET 的机器上也能直接跑, 不必再让用户去装运行时。
SELF_CONTAINED=0
if grep -q '"includedFrameworks"' "$SRC/VPet-Avalonia.App.runtimeconfig.json" 2>/dev/null; then
  SELF_CONTAINED=1
fi

dotnet_help() {
  echo ""
  echo "怎么办(任选一种):"
  echo "  1) 用自带运行时的包: 文件名含 full 的发布包已内置 .NET 8, 解压即用, 不需要系统装 dotnet"
  echo "  2) 用官方脚本装到用户目录(不需要 root, 装完重跑本脚本即可):"
  echo "       wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh"
  echo "       bash /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir \"\$HOME/.dotnet\""
  echo "  3) 系统包管理器(软件源里有的话): sudo apt install -y dotnet-runtime-8.0"
  echo "  4) 已经装在别处: VPET_DOTNET=/path/to/dotnet ./scripts/install.sh"
  echo ""
}

# 依次在 PATH、常见安装位置里找 dotnet。VPET_DOTNET 优先级最高。
probe_dotnet() {
  local c
  if [[ -n "${VPET_DOTNET:-}" ]]; then
    if [[ -x "$VPET_DOTNET" ]]; then echo "$VPET_DOTNET"; return 0; fi
    echo "错误: VPET_DOTNET=$VPET_DOTNET 不存在或不可执行" >&2
    return 1
  fi
  c="$(command -v dotnet 2>/dev/null || true)"
  if [[ -n "$c" && -x "$c" ]]; then echo "$c"; return 0; fi
  # 官方脚本($HOME/.dotnet)、UOS 软件源(/usr/share/dotnet 等)、snap、手动解压到 /opt
  for c in \
    "$ROOT/dotnet/dotnet" \
    "$HOME/.dotnet/dotnet" \
    "$HOME/dotnet/dotnet" \
    "/usr/share/dotnet/dotnet" \
    "/usr/lib/dotnet/dotnet" \
    "/usr/local/share/dotnet/dotnet" \
    "/opt/dotnet/dotnet" \
    "/snap/bin/dotnet" \
    "/var/lib/snapd/snap/bin/dotnet"; do
    [[ -x "$c" ]] && { echo "$c"; return 0; }
  done
  for c in /opt/*/dotnet /usr/lib/*/dotnet; do
    [[ -x "$c" ]] && { echo "$c"; return 0; }
  done
  return 1
}

has_net8() {
  local out
  out="$("$1" --list-runtimes 2>/dev/null || true)"
  [[ "$out" == *"Microsoft.NETCore.App 8."* ]]
}

DOTNET_BIN=""
if [[ "$SELF_CONTAINED" == "1" ]]; then
  echo "[install] 自包含包: 已内置 .NET 运行时, 无需系统安装 dotnet"
else
  if ! DOTNET_BIN="$(probe_dotnet)"; then
    echo "错误: 未找到 dotnet(.NET 8 运行时)。"
    dotnet_help
    exit 1
  fi
  if ! has_net8 "$DOTNET_BIN"; then
    echo "错误: $DOTNET_BIN 里没有 .NET 8 运行时, 桌宠无法启动。"
    echo "      该 dotnet 里现有的运行时:"
    "$DOTNET_BIN" --list-runtimes 2>/dev/null | sed 's/^/        /' || true
    dotnet_help
    exit 1
  fi
  echo "[install] dotnet: $DOTNET_BIN"
fi

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

# 版本文件: 程序启动时优先读同目录/上级目录的 VERSION(见 AppInfo.cs),
# 不拷过去就只剩编译期兜底值 —— 以后发版「关于」里的版本号会对不上
# (注意不能用 `[[ -f x ]] && cp`: 文件不存在时循环体返回非零, set -e 会直接退出)
for f in VERSION CHANGELOG.md; do
  if [[ -f "$ROOT/$f" ]]; then
    cp -f "$ROOT/$f" "$INSTALL_DIR/$f"
  fi
done

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
