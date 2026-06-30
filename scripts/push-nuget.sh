#!/usr/bin/env bash

set -euo pipefail

api_key="${NUGET_API_KEY:-}"
source="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
package_dir="artifacts/packages"
dry_run="false"

usage() {
  cat <<'EOF'
Usage: scripts/push-nuget.sh [options]

Options:
      --api-key <Key>       NuGet API key. Defaults to NUGET_API_KEY.
      --source <Source>     NuGet push source. Defaults to NUGET_SOURCE or nuget.org.
      --directory <Path>    Directory containing .nupkg and .snupkg files.
      --dry-run             Validate package metadata and symbol/package pairing without pushing.
  -h, --help                Show this help text.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --api-key)
      api_key="$2"
      shift 2
      ;;
    --source)
      source="$2"
      shift 2
      ;;
    --directory)
      package_dir="$2"
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

read_package_metadata() {
  python3 - "$1" <<'PY'
import sys
import zipfile
import xml.etree.ElementTree as ET

path = sys.argv[1]

try:
    with zipfile.ZipFile(path) as archive:
        nuspec_names = [name for name in archive.namelist() if name.endswith(".nuspec")]
        if len(nuspec_names) != 1:
            raise ValueError(f"expected exactly one .nuspec, found {len(nuspec_names)}")

        root = ET.fromstring(archive.read(nuspec_names[0]))
except Exception as ex:
    print(f"Failed to read package metadata from {path}: {ex}", file=sys.stderr)
    sys.exit(1)

namespace = ""
if root.tag.startswith("{"):
    namespace = root.tag.split("}", 1)[0][1:]

prefix = f"{{{namespace}}}" if namespace else ""
metadata = root.find(f"{prefix}metadata")
if metadata is None:
    print(f"Package {path} has no nuspec metadata.", file=sys.stderr)
    sys.exit(1)

id_node = metadata.find(f"{prefix}id")
version_node = metadata.find(f"{prefix}version")
if id_node is None or version_node is None or not id_node.text or not version_node.text:
    print(f"Package {path} has no nuspec id/version.", file=sys.stderr)
    sys.exit(1)

print(f"{id_node.text.strip()}\t{version_node.text.strip()}")
PY
}

validate_symbol_package_matches_package() {
  python3 - "$1" "$2" <<'PY'
import sys
import zipfile
import xml.etree.ElementTree as ET

package_path = sys.argv[1]
symbols_path = sys.argv[2]

def read_metadata(path):
    with zipfile.ZipFile(path) as archive:
        nuspec_names = [name for name in archive.namelist() if name.endswith(".nuspec")]
        if len(nuspec_names) != 1:
            raise ValueError(f"expected exactly one .nuspec, found {len(nuspec_names)}")

        root = ET.fromstring(archive.read(nuspec_names[0]))

    namespace = ""
    if root.tag.startswith("{"):
        namespace = root.tag.split("}", 1)[0][1:]

    prefix = f"{{{namespace}}}" if namespace else ""
    metadata = root.find(f"{prefix}metadata")
    if metadata is None:
        raise ValueError("missing nuspec metadata")

    id_node = metadata.find(f"{prefix}id")
    version_node = metadata.find(f"{prefix}version")
    if id_node is None or version_node is None or not id_node.text or not version_node.text:
        raise ValueError("missing nuspec id/version")

    return id_node.text.strip(), version_node.text.strip()

try:
    package_id, package_version = read_metadata(package_path)
    symbols_id, symbols_version = read_metadata(symbols_path)
except Exception as ex:
    print(f"Failed to read package metadata: {ex}", file=sys.stderr)
    sys.exit(1)

if (package_id, package_version) != (symbols_id, symbols_version):
    print(
        f"Symbol package {symbols_path} has metadata {symbols_id} {symbols_version}, "
        f"but package {package_path} has {package_id} {package_version}.",
        file=sys.stderr,
    )
    sys.exit(1)

with zipfile.ZipFile(package_path) as package_archive:
    package_entries = set(package_archive.namelist())

with zipfile.ZipFile(symbols_path) as symbols_archive:
    pdb_entries = [
        name
        for name in symbols_archive.namelist()
        if name.endswith(".pdb")
        and (name.startswith("lib/") or name.startswith("runtimes/"))
    ]

missing = []
for pdb_entry in pdb_entries:
    dll_entry = f"{pdb_entry[:-4]}.dll"
    if dll_entry not in package_entries:
        missing.append((pdb_entry, dll_entry))

if missing:
    for pdb_entry, dll_entry in missing:
        print(
            f"Symbol package contains {pdb_entry}, but {package_path} does not contain {dll_entry}.",
            file=sys.stderr,
        )
    sys.exit(1)
PY
}

nuget_package_exists() {
  local package_id="$1"
  local package_version="$2"
  local lower_id
  local versions_file
  local status

  lower_id="$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')"
  versions_file="$(mktemp)"

  if ! curl -fsSL --retry 3 --retry-delay 2 \
      -o "$versions_file" \
      "https://api.nuget.org/v3-flatcontainer/${lower_id}/index.json" \
      2>/dev/null; then
    rm -f "$versions_file"
    return 1
  fi

  if python3 - "$versions_file" "$package_version" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8-sig") as handle:
    payload = json.load(handle)

target = sys.argv[2].lower()
versions = {str(version).lower() for version in payload.get("versions", [])}
sys.exit(0 if target in versions else 1)
PY
  then
    status=0
  else
    status=1
  fi

  rm -f "$versions_file"
  return "$status"
}

push_regular_package() {
  local package="$1"
  local package_id="$2"
  local package_version="$3"
  local output_file
  local push_status

  last_package_publish_result=""

  echo "Publishing package ${package}"
  output_file="$(mktemp)"
  set +e
  dotnet nuget push "$package" \
    --api-key "$api_key" \
    --source "$source" \
    --no-symbols 2>&1 | tee "$output_file"
  push_status=${PIPESTATUS[0]}
  set -e

  if [[ "$push_status" -eq 0 ]]; then
    last_package_publish_result="uploaded"
    rm -f "$output_file"
    return 0
  fi

  if nuget_package_exists "$package_id" "$package_version"; then
    echo "::warning::Package ${package_id} ${package_version} already exists on NuGet.org; continuing."
    last_package_publish_result="exists"
    rm -f "$output_file"
    return 0
  fi

  echo "::error::Package ${package_id} ${package_version} was not published and is not present on NuGet.org."
  echo "::error::dotnet nuget push failed with exit code ${push_status}; see output above."
  rm -f "$output_file"
  return "$push_status"
}

find_matching_package() {
  local package_id="$1"
  local package_version="$2"
  local package
  local metadata_output
  local candidate_id
  local candidate_version
  local extra_metadata

  for package in "${packages[@]}"; do
    metadata_output="$(read_package_metadata "$package")"
    IFS=$'\t' read -r candidate_id candidate_version extra_metadata <<< "$metadata_output"
    if [[ "$candidate_id" == "$package_id" && "$candidate_version" == "$package_version" && -z "${extra_metadata:-}" ]]; then
      printf '%s\n' "$package"
      return 0
    fi
  done

  return 1
}

package_was_uploaded_this_run() {
  local package_id="$1"
  local package_version="$2"
  local package_key
  local uploaded_key

  if [[ "${#uploaded_package_keys[@]}" -eq 0 ]]; then
    return 1
  fi

  package_key="${package_id}"$'\t'"${package_version}"
  for uploaded_key in "${uploaded_package_keys[@]}"; do
    if [[ "$uploaded_key" == "$package_key" ]]; then
      return 0
    fi
  done

  return 1
}

push_symbol_package() {
  local package="$1"
  local output_file
  local push_status

  echo "Publishing symbols ${package}"
  output_file="$(mktemp)"
  set +e
  dotnet nuget push "$package" \
    --api-key "$api_key" \
    --source "$source" 2>&1 | tee "$output_file"
  push_status=${PIPESTATUS[0]}
  set -e

  if [[ "$push_status" -eq 0 ]]; then
    rm -f "$output_file"
    return 0
  fi

  if grep -Eq "already exists|Conflict" "$output_file"; then
    echo "::warning::Symbol package ${package} already exists on NuGet.org; continuing."
    rm -f "$output_file"
    return 0
  fi

  if grep -Fq "does not exist. Please upload the package before uploading its symbols" "$output_file"; then
    echo "::warning::NuGet has not made the matching package visible to the symbol endpoint yet. Re-run this workflow later to publish ${package}."
    rm -f "$output_file"
    return 0
  fi

  echo "::error::Symbol package ${package} was not published."
  echo "::error::dotnet nuget push failed with exit code ${push_status}; see output above."
  rm -f "$output_file"
  return "$push_status"
}

if [[ "$dry_run" != "true" && -z "$api_key" ]]; then
  echo "::error::NUGET_API_KEY secret is required to publish release packages."
  exit 1
fi

if [[ ! -d "$package_dir" ]]; then
  echo "::error::NuGet package directory does not exist: ${package_dir}"
  exit 1
fi

packages=()
while IFS= read -r package; do
  packages+=("$package")
done < <(find "$package_dir" -maxdepth 1 -type f -name '*.nupkg' | sort)

if [[ "${#packages[@]}" -eq 0 ]]; then
  echo "::error::No NuGet packages were produced in ${package_dir}."
  exit 1
fi

symbols=()
while IFS= read -r package; do
  symbols+=("$package")
done < <(find "$package_dir" -maxdepth 1 -type f -name '*.snupkg' | sort)

uploaded_package_keys=()

for package in "${packages[@]}"; do
  metadata_output="$(read_package_metadata "$package")"
  IFS=$'\t' read -r package_id package_version extra_metadata <<< "$metadata_output"
  if [[ -z "$package_id" || -z "$package_version" || -n "${extra_metadata:-}" ]]; then
    echo "::error::Could not read package ID and version from ${package}."
    exit 1
  fi

  if [[ "$dry_run" == "true" ]]; then
    echo "Found package ${package_id} ${package_version} in ${package}"
    continue
  fi

  push_regular_package "$package" "$package_id" "$package_version"
  if [[ "$last_package_publish_result" == "uploaded" ]]; then
    uploaded_package_keys+=("${package_id}"$'\t'"${package_version}")
  fi
done

if [[ "${#symbols[@]}" -eq 0 ]]; then
  exit 0
fi

for package in "${symbols[@]}"; do
  metadata_output="$(read_package_metadata "$package")"
  IFS=$'\t' read -r package_id package_version extra_metadata <<< "$metadata_output"
  if [[ -z "$package_id" || -z "$package_version" || -n "${extra_metadata:-}" ]]; then
    echo "::error::Could not read symbol package ID and version from ${package}."
    exit 1
  fi

  matching_package="$(find_matching_package "$package_id" "$package_version" || true)"
  if [[ -z "$matching_package" ]]; then
    echo "::error::Missing matching NuGet package for symbols ${package_id} ${package_version}."
    exit 1
  fi

  validate_symbol_package_matches_package "$matching_package" "$package"

  if [[ "$dry_run" == "true" ]]; then
    echo "Found symbols ${package_id} ${package_version} in ${package}"
    continue
  fi

  if ! package_was_uploaded_this_run "$package_id" "$package_version"; then
    echo "::warning::Skipping symbols for ${package_id} ${package_version} because the matching NuGet package was not uploaded in this run."
    echo "::warning::NuGet packages are immutable; publish a new package version instead of reusing symbols from a later build."
    continue
  fi

  push_symbol_package "$package"
done
