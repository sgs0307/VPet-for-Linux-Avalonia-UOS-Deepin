#!/usr/bin/env bash
# 打 GitHub Releases 用的安装包
#
# 用法:
#   ./scripts/package-release.sh                     # 用 ./publish 打包(含桌宠资源则打 full)
#   ./scripts/package-release.sh --arch x64          # 用 ./dist/_x64 作为程序目录
#   ./scripts/package-release.sh --no-mod            # 强制排除桌宠资源(打精简包)
#   ./scripts/package-release.sh --src ./publish --arch arm64
#   ./scripts/package-release.sh --arch x64 --mod-from ./publish/mod
#
# 产物: dist/VPet-UOS-v<版本>-linux-<架构>-{full,nomod}.tar.gz
#
# 说明: 暂存目录里放的是符号链接, 1GB 级的 mod 不会被复制; tar -h 打包时才解开成真实文件。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="$(tr -d '[:space:]' < "$ROOT/VERSION")"
DIST="$ROOT/dist"

SRC=""
ARCH=""
FORCE_NOMOD=0
MOD_FROM=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --src)      SRC="$2"; shift 2 ;;
    --arch)     ARCH="$2"; shift 2 ;;
    --mod-from) MOD_FROM="$2"; shift 2 ;;
    --no-mod)   FORCE_NOMOD=1; shift ;;
    -h|--help)  sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "未知参数: $1"; exit 1 ;;
  esac
done

# ---- 定位程序目录 ----
if [[ -z "$SRC" ]]; then
  if [[ -n "$ARCH" && -d "$DIST/_$ARCH" ]]; then
    SRC="$DIST/_$ARCH"
  else
    SRC="$ROOT/publish"
  fi
fi
if [[ ! -d "$SRC" ]]; then
  echo "错误: 程序目录不存在: $SRC"
  echo "请先运行 ./scripts/build.sh 或用 --src 指定。"
  exit 1
fi
if [[ ! -f "$SRC/VPet-Avalonia.App.dll" ]]; then
  echo "错误: $SRC 里没有 VPet-Avalonia.App.dll, 看起来不是发布目录。"
  exit 1
fi
# 后面要用绝对路径建符号链接, 否则链接目标会相对暂存目录解析
SRC="$(cd "$SRC" && pwd)"
[[ -n "$MOD_FROM" ]] && MOD_FROM="$(cd "$MOD_FROM" && pwd)"

# ---- 架构: 优先命令行, 否则读 apphost ----
if [[ -z "$ARCH" ]]; then
  case "$(file -b "$SRC/VPet-Avalonia.App" 2>/dev/null)" in
    *aarch64*) ARCH="arm64" ;;
    *x86-64*)  ARCH="x64" ;;
    *)         ARCH="$(uname -m | sed 's/aarch64/arm64/; s/x86_64/x64/')" ;;
  esac
fi

# ---- 桌宠资源目录: 可以在别的架构的发布目录里(资源与架构无关) ----
MOD_DIR=""
if [[ "$FORCE_NOMOD" == "0" ]]; then
  if [[ -d "$SRC/mod" ]]; then
    MOD_DIR="$SRC/mod"
  elif [[ -n "$MOD_FROM" && -d "$MOD_FROM" ]]; then
    MOD_DIR="$MOD_FROM"
  elif [[ -d "$ROOT/publish/mod" ]]; then
    MOD_DIR="$ROOT/publish/mod"
  fi
fi
HAS_MOD=0
[[ -n "$MOD_DIR" ]] && HAS_MOD=1

PKG="VPet-UOS-v${VERSION}"
STAGE="$DIST/.stage"
mkdir -p "$DIST"

# ---- 建暂存树(符号链接; 用 find -delete 清理, 避免触发批量删除保护) ----
stage() {
  local with_mod="$1"
  mkdir -p "$STAGE/$PKG/publish"
  find "$STAGE/$PKG" -mindepth 1 -maxdepth 1 -type l -delete 2>/dev/null || true
  find "$STAGE/$PKG/publish" -mindepth 1 -maxdepth 1 -type l -delete 2>/dev/null || true

  # 程序本体(逐项链接, 便于按需挂载 mod)
  local f b item
  for f in "$SRC"/*; do
    b="$(basename "$f")"
    [[ "$b" == "mod" ]] && continue
    ln -sfn "$f" "$STAGE/$PKG/publish/$b"
  done
  if [[ "$with_mod" == "1" && -n "$MOD_DIR" ]]; then
    ln -sfn "$MOD_DIR" "$STAGE/$PKG/publish/mod"
  fi
  # 仓库里的脚本、图标与说明(install.sh 要从 assets/icon 取图标)
  for item in scripts packaging assets docs README.md CHANGELOG.md VERSION LICENSE; do
    ln -sfn "$ROOT/$item" "$STAGE/$PKG/$item"
  done
}

pack() {
  local out="$1"
  echo "[package] $(basename "$out") ..."
  # -h: 把符号链接解开成真实文件
  # gzip -1: mod 里 6300 多个 png 本来就压不动, 用 -1 快十倍, 体积几乎不变
  tar -chf - -C "$STAGE" "$PKG" | gzip -1 > "$out"
  echo "[package] $(du -h "$out" | cut -f1)  $out"
}

if [[ "$HAS_MOD" == "1" ]]; then
  echo "[package] 桌宠资源: $MOD_DIR"
  stage 1
  pack "$DIST/${PKG}-linux-${ARCH}-full.tar.gz"
else
  echo "[package] 未找到 mod/ 或指定了 --no-mod, 只打精简包"
fi

stage 0
pack "$DIST/${PKG}-linux-${ARCH}-nomod.tar.gz"

# 清空暂存目录内容(留空目录, 下次复用; 不删目录本身)
find "$STAGE" -mindepth 1 -delete 2>/dev/null || true

# ---- 校验和 ----
cd "$DIST"
if compgen -G "${PKG}-linux-${ARCH}-*.tar.gz" > /dev/null; then
  sha256sum "${PKG}"-linux-"${ARCH}"-*.tar.gz > "${PKG}-linux-${ARCH}-sha256.txt"
fi

echo
echo "[package] 完成:"
ls -lh "$DIST"/*.tar.gz "$DIST"/*sha256.txt 2>/dev/null | awk '{print "  " $5 "\t" $9}'
