#!/usr/bin/env bash
# 一键发版: 改版本号 → 构建 → 打包 → 提交 → 打 tag → (可选)推送 → (可选)建 Release
#
# 用法:
#   ./scripts/release.sh --version 1.3.2 -m "一句话说明"
#   ./scripts/release.sh --version 1.3.2 -m "修 xxx" --push
#   ./scripts/release.sh --version 1.3.2 -m "xxx" --push --release
#   ./scripts/release.sh --version 1.3.2 -m "xxx" --dry-run     # 只打印要做的事
#
# 选项:
#   --version X.Y.Z    必填, 版本号(不带 v)
#   -m, --message      必填, 提交与 tag 的一句话说明
#   --skip-build       跳过编译(直接用现有 publish/ 和 dist/_* 目录)
#   --skip-x64         不交叉编译 x64(只出 arm64 包)
#   --push             推送到 origin(需先配好 remote 和凭据)
#   --release          用 gh CLI 创建 GitHub Release(隐含 --push, 需要已装 gh 并登录)
#   --notes-file       指定 Release 正文(默认 docs/RELEASE_NOTES-v<版本>.md)
#   --framework-dependent  打框架依赖包(默认自包含: 运行时打进包里, 目标机不用装 .NET)
#   --dry-run          只打印步骤, 不改任何文件
#
# 说明:
#   - 桌宠资源(mod)不进仓库, 打包时从 publish/mod 取, 可用 VPET_MOD_SRC 覆盖
#   - 自包含包每个约 90~95 MB(比框架依赖大 70 MB), 但省掉用户装运行时的全部麻烦
#   - x64 是交叉编译产物; 本机是 arm64 时无法运行验证, 只有 arm64 包做过实机自检
if [ -z "${BASH_VERSION:-}" ]; then
  exec bash "$0" "$@"
fi
# 用 sh 调用时自动切回 bash(dash 不支持 pipefail)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION=""
MESSAGE=""
SKIP_BUILD=0
SKIP_X64=0
DO_PUSH=0
DO_RELEASE=0
DRY_RUN=0
NOTES_FILE=""
# 默认自包含: 目标机不用装 .NET(详见 README「安装」)
SELF_CONTAINED=1

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)     VERSION="$2"; shift 2 ;;
    -m|--message)  MESSAGE="$2"; shift 2 ;;
    --skip-build)  SKIP_BUILD=1; shift ;;
    --skip-x64)    SKIP_X64=1; shift ;;
    --push)        DO_PUSH=1; shift ;;
    --release)     DO_RELEASE=1; DO_PUSH=1; shift ;;
    --notes-file)  NOTES_FILE="$2"; shift 2 ;;
    --framework-dependent) SELF_CONTAINED=0; shift ;;
    --dry-run)     DRY_RUN=1; shift ;;
    -h|--help)     sed -n '2,22p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "未知参数: $1"; exit 1 ;;
  esac
done

die() { echo "错误: $*" >&2; exit 1; }
step() { echo; echo "=== $* ==="; }
# 干跑模式下不真正执行, 只打印
run() {
  if [[ "$DRY_RUN" == "1" ]]; then
    echo "[dry-run] $*"
  else
    "$@"
  fi
}

# ---- 参数校验 ----
[[ -n "$VERSION" ]] || die "缺少 --version(例: --version 1.3.2)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "版本号格式应为 X.Y.Z, 收到: $VERSION"
[[ -n "$MESSAGE" ]] || die "缺少 -m/--message"
[[ -n "$NOTES_FILE" ]] || NOTES_FILE="$ROOT/docs/RELEASE_NOTES-v${VERSION}.md"

TAG="v${VERSION}"
SC_FLAG="false"
[[ "$SELF_CONTAINED" == "1" ]] && SC_FLAG="true"
cd "$ROOT"

# ---- 前置检查 ----
step "前置检查"
if [[ "$DRY_RUN" == "0" ]]; then
  [[ -d .git ]] || die "当前目录不是 git 仓库"
  if [[ -n "$(git status --porcelain)" ]]; then
    echo "未提交的改动:"; git status --short
    die "工作区不干净。请先提交或 stash —— 发版必须从一个干净的提交开始"
  fi
  if git rev-parse -q --verify "refs/tags/$TAG" >/dev/null; then
    die "tag $TAG 已存在。换个版本号, 或先 git tag -d $TAG"
  fi
fi
echo "版本   : $VERSION"
echo "tag    : $TAG"
echo "说明   : $MESSAGE"
echo "Release正文: ${NOTES_FILE#$ROOT/}$([[ -f "$NOTES_FILE" ]] && echo '' || echo ' (不存在, 到时手动写)')"

# ---- 1. 写版本号 ----
step "1/6 更新版本号"
run bash -c "printf '%s\n' '$VERSION' > '$ROOT/VERSION'"
# README 徽章: badge/version-1.3.1-blue
if [[ -f "$ROOT/README.md" ]]; then
  run sed -i "s#badge/version-[0-9][0-9.]*-blue#badge/version-${VERSION}-blue#" "$ROOT/README.md"
  echo "README 徽章 → $VERSION"
fi
echo "提醒: CHANGELOG.md 的顶部条目仍需手写(脚本不替你编内容)"

# ---- 2. 构建 ----
DOTNET_BIN="${VPET_DOTNET:-}"
if [[ -z "$DOTNET_BIN" ]]; then
  if command -v dotnet >/dev/null 2>&1; then DOTNET_BIN="$(command -v dotnet)"
  else
    for c in "$HOME/dotnet/dotnet" "/usr/share/dotnet/dotnet" "/usr/lib/dotnet/dotnet" "/opt/dotnet/dotnet" "/usr/bin/dotnet"; do
      [[ -x "$c" ]] && { DOTNET_BIN="$c"; break; }
    done
  fi
fi
[[ -n "$DOTNET_BIN" && -x "$DOTNET_BIN" ]] || die "未找到 dotnet, 可用 VPET_DOTNET=/path/to/dotnet 指定"

if [[ "$SKIP_BUILD" == "1" ]]; then
  step "2/6 构建(已跳过)"
else
  step "2/6 构建 arm64"
  # 低内存机器上 MSBuild 子节点会被 OOM 杀掉, 统一用 -m:1
  # 自包含要拉 Microsoft.NETCore.App.Runtime.linux-arm64, 本地源可能没有, 指一下公网源
  run env DOTNET_ROOT="$(dirname "$DOTNET_BIN")" \
      DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 \
      DOTNET_GCHeapHardLimitSOH=0x18000000 DOTNET_GCHeapHardLimitLOH=0x08000000 DOTNET_GCHeapHardLimitPOH=0x04000000 \
      "$DOTNET_BIN" publish "$ROOT/src/VPet-Avalonia.App" -c Release -r linux-arm64 \
      --self-contained "$SC_FLAG" -o "$ROOT/dist/_arm64" -m:1 -p:CopyModResources=false \
      --source https://api.nuget.org/v3/index.json --nologo
fi

# ---- 3. 交叉编译 x64 ----
if [[ "$SKIP_X64" == "1" ]]; then
  step "3/6 x64 构建(已跳过)"
elif [[ "$SKIP_BUILD" == "1" ]]; then
  step "3/6 x64 构建(已跳过)"
  [[ -d "$ROOT/dist/_x64" ]] || echo "警告: dist/_x64 不存在, x64 包会打不出来"
else
  step "3/6 交叉编译 x64"
  # 本机 NuGet 可能只配了离线源, 交叉编译要显式指向公网源
  run env DOTNET_ROOT="$(dirname "$DOTNET_BIN")" \
      DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 \
      DOTNET_GCHeapHardLimitSOH=0x18000000 DOTNET_GCHeapHardLimitLOH=0x08000000 DOTNET_GCHeapHardLimitPOH=0x04000000 \
      "$DOTNET_BIN" publish "$ROOT/src/VPet-Avalonia.App" -c Release -r linux-x64 \
      --self-contained "$SC_FLAG" -o "$ROOT/dist/_x64" -m:1 -p:CopyModResources=false \
      --source https://api.nuget.org/v3/index.json --nologo
fi

# ---- 4. 打包 ----
step "4/6 打安装包"
MOD_FROM="${VPET_MOD_SRC:-$ROOT/publish/mod}"
if [[ -d "$MOD_FROM" ]]; then
  run "$ROOT/scripts/package-release.sh" --src "$ROOT/dist/_arm64" --arch arm64 --mod-from "$MOD_FROM"
  [[ "$SKIP_X64" == "0" ]] && run "$ROOT/scripts/package-release.sh" --src "$ROOT/dist/_x64" --arch x64 --mod-from "$MOD_FROM"
else
  echo "警告: 没找到桌宠资源目录($MOD_FROM), 只能打 nomod 包"
  run "$ROOT/scripts/package-release.sh" --src "$ROOT/dist/_arm64" --arch arm64 --no-mod
  [[ "$SKIP_X64" == "0" ]] && run "$ROOT/scripts/package-release.sh" --src "$ROOT/dist/_x64" --arch x64 --no-mod
fi

# ---- 5. 提交 + tag ----
step "5/6 提交并打 tag"
run git add -A
run git commit -m "v${VERSION}: ${MESSAGE}"
run git tag -a "$TAG" -m "v${VERSION} — ${MESSAGE}"

# ---- 6. 推送 / 发 Release ----
step "6/6 推送"
if [[ "$DO_PUSH" == "1" ]]; then
  run git push origin "$(git symbolic-ref --short HEAD 2>/dev/null || echo main)"
  run git push origin "$TAG"
else
  echo "已跳过推送。手动执行:"
  echo "  git push origin main && git push origin $TAG"
fi

if [[ "$DO_RELEASE" == "1" ]]; then
  step "创建 GitHub Release"
  command -v gh >/dev/null 2>&1 || die "未安装 gh CLI: https://github.com/cli/cli#installation"
  ASSETS=()
  for f in "$ROOT"/dist/VPet-UOS-v"${VERSION}"-linux-*.tar.gz "$ROOT"/dist/VPet-UOS-v"${VERSION}"-linux-*-sha256.txt; do
    [[ -f "$f" ]] && ASSETS+=("$f")
  done
  [[ ${#ASSETS[@]} -gt 0 ]] || die "dist/ 里没有 v${VERSION} 的包"
  NOTES_ARGS=(--notes "$MESSAGE")
  [[ -f "$NOTES_FILE" ]] && NOTES_ARGS=(--notes-file "$NOTES_FILE")
  run gh release create "$TAG" --title "v${VERSION} — ${MESSAGE}" "${NOTES_ARGS[@]}" "${ASSETS[@]}"
else
  echo "已跳过 Release 创建。手动到仓库 Releases 页面:"
  echo "  1. Draft a new release → 选 tag $TAG"
  echo "  2. 正文粘贴 ${NOTES_FILE#$ROOT/} 的内容"
  echo "  3. 上传 dist/VPet-UOS-v${VERSION}-linux-*.tar.gz + sha256.txt"
fi

step "完成"
echo "版本 $VERSION 已就绪。产物:"
ls -lh "$ROOT"/dist/VPet-UOS-v"${VERSION}"-linux-*.tar.gz 2>/dev/null | awk '{print "  " $5 "\t" $9}' || true
echo
echo "别忘了:"
echo "  - CHANGELOG.md 顶部补一条 [$VERSION] 记录(脚本不代写)"
echo "  - docs/RELEASE_NOTES-v${VERSION}.md 若不存在, Release 正文用提交说明代替"
echo "  - arm64 包可本机自检: HOME=/tmp/x VPET_SELFTEST=1 VPET_SELFTEST_LIGHT=1 dist/_arm64/VPet-Avalonia.App"
