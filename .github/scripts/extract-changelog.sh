#!/usr/bin/env bash
# 从 CHANGELOG.md 提取与当前 tag 对应的版本小节,写入 RELEASE_BODY.md,作为 GitHub Release 正文。
#
# 用法: bash .github/scripts/extract-changelog.sh <tag> [changelog] [out]
#   <tag>       形如 v1.14.0(GitHub Actions 里为 $GITHUB_REF_NAME)
#   [changelog] 默认 CHANGELOG.md
#   [out]       默认 RELEASE_BODY.md
#
# 匹配规则:CHANGELOG 小节标题形如 "## [v1.14] - 2026-09-10"(常省略补丁号),
# 因此先按完整版本 [1.14.0] 匹配,再退回主次版本 v1.14] 匹配;取该小节到下一个 "## [" 之前。
# 匹配不到时退化为一段简短说明,保证 Release 步骤不会因为没有正文而失败。
set -euo pipefail

tag="${1:-}"
changelog="${2:-CHANGELOG.md}"
out="${3:-RELEASE_BODY.md}"

ver="${tag#v}"     # v1.14.0 -> 1.14.0
mm="${ver%.*}"     # 1.14.0  -> 1.14

awk -v ver="$ver" -v mm="$mm" '
  /^## \[/ {
    if (found) exit
    if (index($0, "[" ver "]") > 0 || index($0, "v" mm "]") > 0) found = 1
  }
  found { print }
' "$changelog" > "$out"

if [ ! -s "$out" ]; then
  {
    echo "## ${tag}"
    echo
    echo "本次发布未在 \`${changelog}\` 中找到对应版本小节,详见仓库内 [CHANGELOG.md](${changelog})。"
  } > "$out"
fi

cat "$out"
