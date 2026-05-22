#!/bin/bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

"${SCRIPT_DIR}/build-docs.sh"

BUILD_DIR="${SCRIPT_DIR}/site/.lunet/build/www"

if [ ! -f "${BUILD_DIR}/index.html" ]; then
  echo "Missing generated docs index: ${BUILD_DIR}/index.html" >&2
  exit 1
fi

if [ ! -d "${BUILD_DIR}/articles" ]; then
  echo "Missing generated articles directory." >&2
  exit 1
fi

if [ ! -d "${BUILD_DIR}/api" ]; then
  echo "Missing generated API directory." >&2
  exit 1
fi

if grep -R --include='*.html' -nE 'href="[^"]+\.md("|#)' "${BUILD_DIR}" >/tmp/procad-docs-md-links.txt; then
  cat /tmp/procad-docs-md-links.txt >&2
  echo "Generated docs contain raw .md links." >&2
  exit 1
fi

if grep -R --include='*.html' -nE 'href="[^"]+/readme(/|#|")' "${BUILD_DIR}" >/tmp/procad-docs-readme-links.txt; then
  cat /tmp/procad-docs-readme-links.txt >&2
  echo "Generated docs contain /readme links." >&2
  exit 1
fi

echo "Docs validation passed: ${BUILD_DIR}"
