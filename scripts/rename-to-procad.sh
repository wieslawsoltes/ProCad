#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage:
  scripts/rename-to-procad.sh --dry-run
  scripts/rename-to-procad.sh --apply

Renames the repository product identity from ACadInspector/CadInspector to ProCad.
The script updates tracked text content and filesystem paths, excluding submodules,
build output, and packaged artifacts.
USAGE
}

mode="${1:---dry-run}"
case "$mode" in
  --dry-run) dry_run=1 ;;
  --apply) dry_run=0 ;;
  -h|--help) usage; exit 0 ;;
  *) usage >&2; exit 2 ;;
esac

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

declare -a grep_patterns=(
  "ACADINSPECTOR"
  "ACadInspector"
  "AcadInspector"
  "CADINSPECTOR"
  "CadInspector"
  "acadInspector"
  "cadInspector"
  "acad-inspector"
  "cad-inspector"
  "acadinspector"
  "cadinspector"
)

declare -a grep_args=()
for pattern in "${grep_patterns[@]}"; do
  grep_args+=(-e "$pattern")
done

declare -a path_find_names=(
  "-name" "*ACADINSPECTOR*"
  "-o" "-name" "*ACadInspector*"
  "-o" "-name" "*AcadInspector*"
  "-o" "-name" "*CADINSPECTOR*"
  "-o" "-name" "*CadInspector*"
  "-o" "-name" "*acadInspector*"
  "-o" "-name" "*cadInspector*"
  "-o" "-name" "*acad-inspector*"
  "-o" "-name" "*cad-inspector*"
  "-o" "-name" "*acadinspector*"
  "-o" "-name" "*cadinspector*"
)

is_excluded_path() {
  local path="$1"
  case "$path" in
    ./.git|./.git/*) return 0 ;;
    ./external/ACadSharp|./external/ACadSharp/*) return 0 ;;
    ./external/ProEdit|./external/ProEdit/*) return 0 ;;
    ./artifacts|./artifacts/*) return 0 ;;
    ./PROCAD-RENAME-REPORT.md) return 0 ;;
    ./scripts/rename-to-procad.sh) return 0 ;;
    ./scripts/verify-procad-rename.sh) return 0 ;;
    */bin|*/bin/*) return 0 ;;
    */obj|*/obj/*) return 0 ;;
  esac

  return 1
}

replace_name() {
  local value="$1"
  value="${value//ACADINSPECTOR/PROCAD}"
  value="${value//ACadInspector/ProCad}"
  value="${value//AcadInspector/ProCad}"
  value="${value//CADINSPECTOR/PROCAD}"
  value="${value//CadInspector/ProCad}"
  value="${value//acadInspector/proCad}"
  value="${value//cadInspector/proCad}"
  value="${value//acad-inspector/procad}"
  value="${value//cad-inspector/procad}"
  value="${value//acadinspector/procad}"
  value="${value//cadinspector/procad}"
  printf '%s' "$value"
}

replace_file_content() {
  local file="$1"
  perl -0pi -e '
    s/ACADINSPECTOR/PROCAD/g;
    s/ACadInspector/ProCad/g;
    s/AcadInspector/ProCad/g;
    s/CADINSPECTOR/PROCAD/g;
    s/CadInspector/ProCad/g;
    s/acadInspector/proCad/g;
    s/cadInspector/proCad/g;
    s/acad-inspector/procad/g;
    s/cad-inspector/procad/g;
    s/acadinspector/procad/g;
    s/cadinspector/procad/g;
  ' -- "$file"
}

collect_content_files() {
  git grep -Il -z "${grep_args[@]}" -- . \
    ':(exclude)external/ACadSharp/**' \
    ':(exclude)external/ProEdit/**' \
    ':(exclude)artifacts/**' \
    ':(exclude)PROCAD-RENAME-REPORT.md' \
    ':(exclude)scripts/rename-to-procad.sh' \
    ':(exclude)scripts/verify-procad-rename.sh'
}

collect_path_renames() {
  find . -depth \( "${path_find_names[@]}" \) -print0 |
    while IFS= read -r -d '' path; do
      if is_excluded_path "$path"; then
        continue
      fi

      local path_dir
      local path_name
      local new_name
      local new_path
      path_dir="$(dirname "$path")"
      path_name="$(basename "$path")"
      new_name="$(replace_name "$path_name")"
      new_path="$path_dir/$new_name"
      if [[ "$new_path" != "$path" ]]; then
        printf '%s\0%s\0' "$path" "$new_path"
      fi
    done
}

validate_path_renames() {
  local failed=0
  while IFS= read -r -d '' source && IFS= read -r -d '' target; do
    if [[ ! -e "$source" ]]; then
      printf 'missing source: %s\n' "$source" >&2
      failed=1
      continue
    fi

    if [[ -e "$target" ]]; then
      printf 'target already exists: %s\n' "$target" >&2
      failed=1
    fi
  done < <(collect_path_renames)

  if [[ "$failed" -ne 0 ]]; then
    exit 1
  fi
}

rename_path() {
  local source="$1"
  local target="$2"
  local source_rel="${source#./}"

  if git ls-files --error-unmatch "$source_rel" >/dev/null 2>&1; then
    git mv "$source" "$target"
    return
  fi

  if [[ -d "$source" ]] && [[ -n "$(git ls-files "$source_rel")" ]]; then
    git mv "$source" "$target"
    return
  fi

  mv "$source" "$target"
}

printf 'Repository: %s\n' "$repo_root"
if [[ "$dry_run" -eq 1 ]]; then
  printf 'Mode: dry-run\n'
else
  printf 'Mode: apply\n'
fi

content_files=()
while IFS= read -r -d '' file; do
  content_files+=("$file")
done < <(collect_content_files)

printf 'Tracked text files with product identifiers: %d\n' "${#content_files[@]}"
for file in "${content_files[@]}"; do
  printf 'content %s\n' "$file"
done

path_pairs=()
while IFS= read -r -d '' path_part; do
  path_pairs+=("$path_part")
done < <(collect_path_renames)

path_rename_count=$((${#path_pairs[@]} / 2))
printf 'Filesystem paths to rename: %d\n' "$path_rename_count"
for ((i = 0; i < ${#path_pairs[@]}; i += 2)); do
  printf 'path %s -> %s\n' "${path_pairs[i]}" "${path_pairs[i + 1]}"
done

validate_path_renames

if [[ "$dry_run" -eq 1 ]]; then
  printf 'Dry-run complete. No files changed.\n'
  exit 0
fi

for file in "${content_files[@]}"; do
  replace_file_content "$file"
done

for ((i = 0; i < ${#path_pairs[@]}; i += 2)); do
  rename_path "${path_pairs[i]}" "${path_pairs[i + 1]}"
done

printf 'Rename complete. Run scripts/verify-procad-rename.sh next.\n'
