#!/usr/bin/env bash
# 构建并发布 VPet Linux 适配版到 ./publish
#
# 用法:
#   ./scripts/build.sh                    # 正常构建(含桌宠资源 mod)
#   ./scripts/build.sh --no-mod           # 只编译程序, 不复制 1GB 级的桌宠资源
#   ./scripts/build.sh --self-contained   # 把 .NET 运行时打进产物(目标机不用装 .NET)
#   VPET_MOD_SRC=/path/to/mod ./scripts/build.sh
#   VPET_RID=linux-arm64 ./scripts/build.sh --self-contained   # 指定自包含包的目标架构
#
# 说明: 桌宠资源(mod)体积约 1GB, 不纳入仓库, 构建时从本地上游目录复制。
if [ -z "${BASH_VERSION:-}" ]; then
  exec bash "$0" "$@"
fi
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/publish"
PROJECT="$ROOT/src/VPet-Avalonia.App/VPet-Avalonia.App.csproj"

COPY_MOD=1
SELF_CONTAINED=0
for a in "$@"; do
  case "$a" in
    --no-mod)            COPY_MOD=0 ;;
    --self-contained|-sc) SELF_CONTAINED=1 ;;
    -h|--help)           sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "未知参数: $a"; exit 1 ;;
  esac
done

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
  echo "错误: 未找到 dotnet。请安装 .NET 8 SDK, 或用 VPET_DOTNET=/path/to/dotnet 指定。"
  exit 1
fi
echo "[build] dotnet: $DOTNET_BIN"
"$DOTNET_BIN" --version

# ---- 定位桌宠资源目录 ----
find_mod_src() {
  if [[ -n "${VPET_MOD_SRC:-}" && -d "${VPET_MOD_SRC}" ]]; then
    echo "${VPET_MOD_SRC}"; return
  fi
  local candidates=(
    "$ROOT/assets/mod"
    "$ROOT/../VPet-main/VPet-main/VPet-Simulator.Windows/mod"
    "$ROOT/../../VPet-main/VPet-main/VPet-Simulator.Windows/mod"
    "$ROOT/src/VPet-Simulator.Windows/mod"
  )
  for d in "${candidates[@]}"; do
    if [[ -d "$d" ]]; then echo "$d"; return; fi
  done
  echo ""
}

# ---- 构建 ----
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

rm -rf "$OUT"

PUB_ARGS=(-c Release -o "$OUT" -m:1 --nologo)
if [[ "$SELF_CONTAINED" == "1" ]]; then
  RID="${VPET_RID:-}"
  if [[ -z "$RID" ]]; then
    case "$(uname -m)" in
      x86_64|amd64)  RID=linux-x64 ;;
      aarch64|arm64) RID=linux-arm64 ;;
      *) echo "错误: 无法识别的架构 $(uname -m), 请用 VPET_RID=linux-x64 之类指定"; exit 1 ;;
    esac
  fi
  echo "[build] 自包含发布: $RID (目标机无需安装 .NET 运行时)"
  PUB_ARGS+=(-r "$RID" --self-contained true)
fi

if [[ "$COPY_MOD" == "1" ]]; then
  MOD_SRC="$(find_mod_src)"
  if [[ -n "$MOD_SRC" ]]; then
    echo "[build] 桌宠资源: $MOD_SRC"
    # -m:1 关闭多节点并行构建: 低内存环境(如容器/瘦客户端)下 MSBuild 子节点会被 OOM 杀掉
    "$DOTNET_BIN" publish "$PROJECT" "${PUB_ARGS[@]}" -p:VPetModSource="$MOD_SRC"
  else
    echo "[build] 警告: 未找到桌宠资源目录(mod), 只发布程序本体。"
    echo "[build] 可用 VPET_MOD_SRC=/path/to/mod 指定, 之后把 mod 放到 $OUT/mod 即可。"
    "$DOTNET_BIN" publish "$PROJECT" "${PUB_ARGS[@]}" -p:CopyModResources=false
  fi
else
  echo "[build] 跳过桌宠资源复制"
  "$DOTNET_BIN" publish "$PROJECT" "${PUB_ARGS[@]}" -p:CopyModResources=false
fi

echo "[build] 完成, 发布目录: $OUT"
ls -la "$OUT" | head -20
[[ -d "$OUT/mod" ]] && echo "[build] 桌宠资源已包含: $OUT/mod" || echo "[build] 注意: 发布目录中没有 mod, 运行时会找不到宠物资源"
