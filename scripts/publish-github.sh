#!/usr/bin/env bash
# 把本地仓库推到 GitHub 并创建 Release（上传安装包）。
#
# 用法:
#   GITHUB_TOKEN=ghp_xxx ./scripts/publish-github.sh                  # 推代码+tag+建 Release+传全部资产
#   GITHUB_TOKEN=ghp_xxx ./scripts/publish-github.sh --only-source    # 只推代码和 tag
#   GITHUB_TOKEN=ghp_xxx ./scripts/publish-github.sh --skip-upload    # 建 Release 但不传附件
#   GITHUB_TOKEN=ghp_xxx ./scripts/publish-github.sh --dry-run        # 只打印将要做什么
#
# token 需要 repo 权限(classic) 或 Contents + Releases 写权限(fine-grained)。
# token 只在本进程内使用: 推送走带 token 的临时 URL, 不写进 .git/config, 不落盘。
#
# 注意: 本机全局配了 url.<ghproxy>.insteadOf 把 github.com 改写成镜像, 而镜像只读,
# 推送会失败。带 token 的 URL 前缀不匹配该规则, 天然绕过。
if [ -z "${BASH_VERSION:-}" ]; then
  exec bash "$0" "$@"
fi
set -euo pipefail

OWNER="sgs0307"
REPO="VPet-for-Linux-Avalonia-UOS-Deepin"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

ONLY_SOURCE=0
SKIP_UPLOAD=0
DRY_RUN=0
ALLOW_OVERWRITE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --only-source)      ONLY_SOURCE=1; shift ;;
    --skip-upload)      SKIP_UPLOAD=1; shift ;;
    --dry-run)          DRY_RUN=1; shift ;;
    --allow-overwrite)  ALLOW_OVERWRITE=1; shift ;;
    -h|--help)          sed -n '2,15p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "未知参数: $1"; exit 1 ;;
  esac
done

VERSION="$(tr -d '[:space:]' < VERSION)"
TAG="v$VERSION"
API="https://api.github.com/repos/$OWNER/$REPO"
UPLOAD="https://uploads.github.com/repos/$OWNER/$REPO"

if [[ -z "${GITHUB_TOKEN:-}" && "$DRY_RUN" != "1" ]]; then
  cat <<'EOF'
错误: 没有设置 GITHUB_TOKEN

生成一个(勾 repo 权限):
  https://github.com/settings/tokens/new?scopes=repo&description=VPet-UOS

然后:
  GITHUB_TOKEN=ghp_xxx ./scripts/publish-github.sh
EOF
  exit 1
fi

api() {  # api <METHOD> <URL> [额外 curl 参数...]
  local method="$1" url="$2"; shift 2
  curl -sS -X "$method" \
    -H "Authorization: Bearer ${GITHUB_TOKEN:-}" \
    -H "Accept: application/vnd.github+json" \
    -H "X-GitHub-Api-Version: 2022-11-28" \
    "$@" "$url"
}

json_get() { python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('$1',''))" 2>/dev/null || true; }

echo "=== 0/5 环境检查 ==="
echo "仓库 : $OWNER/$REPO"
echo "版本 : $TAG"
echo "提交 : $(git rev-parse --short HEAD)  $(git log -1 --format=%s)"
echo "tag  : $(git tag -l "$TAG" | head -1)"
echo "分支 : $(git symbolic-ref --short HEAD)"

if [[ "$DRY_RUN" != "1" ]]; then
  # 远端 main 目前只有一个 315 字节的占位 README。多于 3 个条目说明有人往里放过东西,
  # 这时不强推, 免得冲掉别人的提交。
  REMOTE_N=$(api GET "$API/git/trees/main?recursive=1" | python3 -c "
import sys,json
try: print(len(json.load(sys.stdin).get('tree',[])))
except Exception: print(-1)
" 2>/dev/null | head -1)
  echo "远端 main 条目数: ${REMOTE_N:-未知}"
  if [[ "${REMOTE_N:-0}" -gt 3 && "$ALLOW_OVERWRITE" != "1" ]]; then
    echo "警告: 远端 main 不止占位 README, 为避免覆盖他人提交已停止。"
    echo "      确认要覆盖请加 --allow-overwrite"
    exit 1
  fi
fi

echo
echo "=== 1/5 推送 main ==="
PUSH_URL="https://x-access-token:${GITHUB_TOKEN:-TOKEN}@github.com/$OWNER/$REPO.git"
if [[ "$DRY_RUN" == "1" ]]; then
  echo "[dry-run] git push <token>@github.com:$OWNER/$REPO HEAD:main --force"
  echo "[dry-run] git push <token>@github.com:$OWNER/$REPO --tags --force"
else
  # 远端 Initial commit 与本仓库无共同祖先 → 必须 force
  git push "$PUSH_URL" HEAD:main --force
fi

echo
echo "=== 2/5 推送 tag ==="
if [[ "$DRY_RUN" != "1" ]]; then
  git push "$PUSH_URL" --tags --force
fi

if [[ "$ONLY_SOURCE" == "1" ]]; then
  echo
  echo "已按要求只推源码, 跳过 Release。"
  exit 0
fi

echo
echo "=== 3/5 创建 Release $TAG ==="
BODY_FILE="docs/RELEASE_NOTES-$TAG.md"
[[ -f "$BODY_FILE" ]] || BODY_FILE="CHANGELOG.md"
echo "正文来源: $BODY_FILE"

if [[ "$DRY_RUN" == "1" ]]; then
  echo "[dry-run] POST $API/releases  (tag=$TAG, 正文=$BODY_FILE)"
  RELEASE_ID="DRYRUN"
else
  RELEASE_ID=$(api GET "$API/releases/tags/$TAG" | json_get id)
  if [[ -n "$RELEASE_ID" ]]; then
    echo "Release 已存在 (id=$RELEASE_ID), 更新正文"
    BODYJSON=$(mktemp)
    python3 - "$BODY_FILE" "$TAG" > "$BODYJSON" <<'PY'
import json, sys
body = open(sys.argv[1], encoding="utf-8").read()
print(json.dumps({"name": "VPet UOS Linux " + sys.argv[2], "body": body}, ensure_ascii=False))
PY
    api PATCH "$API/releases/$RELEASE_ID" --data-binary @"$BODYJSON" > /dev/null
    rm -f "$BODYJSON"
  else
    BODYJSON=$(mktemp)
    python3 - "$BODY_FILE" "$TAG" > "$BODYJSON" <<'PY'
import json, sys
body = open(sys.argv[1], encoding="utf-8").read()
print(json.dumps({
    "tag_name": sys.argv[2],
    "name": "VPet UOS Linux " + sys.argv[2],
    "body": body,
    "draft": False,
    "prerelease": False,
}, ensure_ascii=False))
PY
    RELEASE_ID=$(api POST "$API/releases" --data-binary @"$BODYJSON" | json_get id)
    rm -f "$BODYJSON"
    echo "Release 已创建 id=$RELEASE_ID"
  fi
  if [[ -z "$RELEASE_ID" ]]; then
    echo "错误: 创建 Release 失败"
    exit 1
  fi
fi

if [[ "$SKIP_UPLOAD" == "1" ]]; then
  echo "已跳过附件上传"
  exit 0
fi

echo
echo "=== 4/5 上传附件 ==="
shopt -s nullglob
ASSETS=(dist/VPet-UOS-"$TAG"-linux-*.tar.gz dist/VPet-UOS-"$TAG"-linux-*-sha256.txt)
if [[ ${#ASSETS[@]} -eq 0 ]]; then
  echo "错误: dist/ 里没有 $TAG 的产物, 先跑 ./scripts/release.sh"
  exit 1
fi

for f in "${ASSETS[@]}"; do
  name="$(basename "$f")"
  printf -- "--- %s (%s)\n" "$name" "$(du -h "$f" | cut -f1)"

  if [[ "$DRY_RUN" == "1" ]]; then
    echo "    [dry-run] POST $UPLOAD/releases/<id>/assets?name=$name"
    continue
  fi

  # 同名附件先删掉, 否则 POST 返回 422 already_exists
  OLD_ID=$(api GET "$API/releases/$RELEASE_ID/assets?per_page=100" | python3 -c "
import sys, json
name = sys.argv[1] if len(sys.argv) > 1 else ''
for a in json.load(sys.stdin):
    if a['name'] == name:
        print(a['id']); break
" "$name" 2>/dev/null || true)
  if [[ -n "${OLD_ID:-}" ]]; then
    echo "    删除同名旧附件 id=$OLD_ID"
    api DELETE "$API/releases/assets/$OLD_ID" > /dev/null
  fi

  # 大文件要给足超时; GitHub 单附件上限 2 GB
  RESP=$(curl -sS --retry 3 --retry-delay 10 --max-time 7200 \
    -X POST \
    -H "Authorization: Bearer $GITHUB_TOKEN" \
    -H "Content-Type: application/octet-stream" \
    --data-binary @"$f" \
    "$UPLOAD/releases/$RELEASE_ID/assets?name=$name" 2>&1)
  echo "$RESP" | python3 -c "
import sys, json
try:
    d = json.load(sys.stdin)
except Exception:
    print('    上传返回非 JSON, 原文:', sys.argv[1][:300] if len(sys.argv) > 1 else '')
    sys.exit(0)
if d.get('id'):
    print('    已上传:', d.get('name'), d.get('size'), 'bytes')
else:
    print('    失败:', d)
" "$RESP"
done

echo
echo "=== 5/5 完成 ==="
echo "仓库   : https://github.com/$OWNER/$REPO"
echo "Release: https://github.com/$OWNER/$REPO/releases/tag/$TAG"
[[ "$DRY_RUN" != "1" ]] && {
  echo
  echo "收尾: 核对附件大小后, 到 https://github.com/settings/tokens 撤销本次 token"
}
