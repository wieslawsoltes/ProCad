#!/usr/bin/env bash

set -euo pipefail

package_dir="artifacts/packages"
tag_name=""
version=""
target=""
prerelease="false"
dry_run="false"

usage() {
  cat <<'EOF'
Usage: scripts/publish-github-release.sh [options]

Options:
      --directory <Path>    Directory containing release assets.
      --tag <Tag>           Release tag, for example v0.1.0.
      --version <Version>   Release version, for example 0.1.0.
      --target <Sha>        Target commit SHA for the release.
      --prerelease <Bool>   Mark the GitHub release as prerelease.
      --dry-run             Validate inputs without creating or updating the release.
  -h, --help                Show this help text.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --directory)
      package_dir="$2"
      shift 2
      ;;
    --tag)
      tag_name="$2"
      shift 2
      ;;
    --version)
      version="$2"
      shift 2
      ;;
    --target)
      target="$2"
      shift 2
      ;;
    --prerelease)
      prerelease="$2"
      shift 2
      ;;
    --dry-run)
      dry_run="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "$tag_name" ]]; then
  echo "::error::Release tag is required."
  exit 1
fi

if [[ -z "$version" ]]; then
  echo "::error::Release version is required."
  exit 1
fi

if [[ -z "$target" ]]; then
  echo "::error::Release target commit is required."
  exit 1
fi

case "$prerelease" in
  true|false)
    ;;
  *)
    echo "::error::Prerelease must be true or false."
    exit 1
    ;;
esac

if [[ ! -d "$package_dir" ]]; then
  echo "::error::Release asset directory does not exist: ${package_dir}"
  exit 1
fi

shopt -s nullglob
assets=("${package_dir}"/*.nupkg "${package_dir}"/*.snupkg)
if [[ "${#assets[@]}" -eq 0 ]]; then
  echo "::error::No release assets were found in ${package_dir}."
  exit 1
fi

if [[ "$dry_run" == "true" ]]; then
  echo "Would publish GitHub release ${tag_name} (${version}) for ${target}."
  printf 'Would upload %s\n' "${assets[@]}"
  exit 0
fi

if [[ -z "${GH_TOKEN:-}" && -z "${GITHUB_TOKEN:-}" ]]; then
  echo "::error::GH_TOKEN or GITHUB_TOKEN is required to publish the GitHub release."
  exit 1
fi

release_args=(
  --title "Release ${version}"
  --target "$target"
)

if [[ "$prerelease" == "true" ]]; then
  release_args+=(--prerelease)
else
  release_args+=(--prerelease=false)
fi

if gh release view "$tag_name" >/dev/null 2>&1; then
  echo "Updating GitHub release ${tag_name}."
  gh release edit "$tag_name" "${release_args[@]}"
else
  echo "Creating GitHub release ${tag_name}."
  create_args=(
    --title "Release ${version}"
    --target "$target"
    --generate-notes
  )

  if [[ "$prerelease" == "true" ]]; then
    create_args+=(--prerelease)
  fi

  gh release create "$tag_name" "${create_args[@]}"
fi

for asset in "${assets[@]}"; do
  echo "Uploading release asset ${asset}."
  gh release upload "$tag_name" "$asset" --clobber
done
