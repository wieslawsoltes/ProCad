#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

failures=0

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  failures=$((failures + 1))
}

pass() {
  printf 'PASS: %s\n' "$1"
}

declare -a old_patterns=(
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

declare -a old_grep_args=()
for pattern in "${old_patterns[@]}"; do
  old_grep_args+=(-e "$pattern")
done

printf 'Repository: %s\n' "$repo_root"

if [[ -f ProCad.slnx ]]; then
  pass "ProCad.slnx exists"
else
  fail "ProCad.slnx is missing"
fi

expected_projects=(
  "ProCad/ProCad.csproj"
  "ProCad.Browser/ProCad.Browser.csproj"
  "ProCad.Collaboration/ProCad.Collaboration.csproj"
  "ProCad.Collaboration.ServerHost/ProCad.Collaboration.ServerHost.csproj"
  "ProCad.Controls/ProCad.Controls.csproj"
  "ProCad.Controls.Avalonia/ProCad.Controls.Avalonia.csproj"
  "ProCad.Controls.Maui/ProCad.Controls.Maui.csproj"
  "ProCad.Controls.Skia/ProCad.Controls.Skia.csproj"
  "ProCad.Controls.Tests/ProCad.Controls.Tests.csproj"
  "ProCad.Controls.Uno/ProCad.Controls.Uno.csproj"
  "ProCad.Core/ProCad.Core.csproj"
  "ProCad.Desktop/ProCad.Desktop.csproj"
  "ProCad.Editing/ProCad.Editing.csproj"
  "ProCad.Editing.Tests/ProCad.Editing.Tests.csproj"
  "ProCad.Generators/ProCad.Generators.csproj"
  "ProCad.IO/ProCad.IO.csproj"
  "ProCad.Rendering/ProCad.Rendering.csproj"
  "ProCad.Scripting/ProCad.Scripting.csproj"
  "ProCad.Tests/ProCad.Tests.csproj"
  "ProCad.TraceCli/ProCad.TraceCli.csproj"
)

missing_projects=0
for project in "${expected_projects[@]}"; do
  if [[ ! -f "$project" ]]; then
    printf 'missing project: %s\n' "$project" >&2
    missing_projects=$((missing_projects + 1))
  fi
done

if [[ "$missing_projects" -eq 0 ]]; then
  pass "all expected ProCad project files exist"
else
  fail "$missing_projects expected ProCad project files are missing"
fi

content_hits="$(git grep -n -I "${old_grep_args[@]}" -- . \
  ':(exclude)external/ACadSharp/**' \
  ':(exclude)external/ProEdit/**' \
  ':(exclude)artifacts/**' \
  ':(exclude)PROCAD-RENAME-REPORT.md' \
  ':(exclude)scripts/rename-to-procad.sh' \
  ':(exclude)scripts/verify-procad-rename.sh' \
  ':(exclude)plan/procad_rename_report_*.md' || true)"

if [[ -z "$content_hits" ]]; then
  pass "no legacy product identifiers remain in tracked text content"
else
  printf '%s\n' "$content_hits" >&2
  fail "legacy product identifiers remain in tracked text content"
fi

path_hits="$(git ls-files | rg 'ACADINSPECTOR|ACadInspector|AcadInspector|CADINSPECTOR|CadInspector|acadInspector|cadInspector|acad-inspector|cad-inspector|acadinspector|cadinspector' || true)"
path_hits="$(printf '%s\n' "$path_hits" | rg -v '^(PROCAD-RENAME-REPORT\.md|scripts/rename-to-procad\.sh|scripts/verify-procad-rename\.sh|plan/procad_rename_report_[^/]+\.md)$' || true)"

if [[ -z "$path_hits" ]]; then
  pass "no legacy product identifiers remain in tracked paths"
else
  printf '%s\n' "$path_hits" >&2
  fail "legacy product identifiers remain in tracked paths"
fi

old_top_level_dirs="$(find . -maxdepth 1 -type d \( -name '*ACadInspector*' -o -name '*CadInspector*' -o -name '*acadinspector*' -o -name '*cadinspector*' \) -print | sort)"
if [[ -z "$old_top_level_dirs" ]]; then
  pass "no legacy product-named top-level directories remain"
else
  printf '%s\n' "$old_top_level_dirs" >&2
  fail "legacy product-named top-level directories remain"
fi

if [[ -f ProCad.slnx ]]; then
  missing_solution_paths=0
  while IFS= read -r project_path; do
    if [[ ! -f "$project_path" ]]; then
      printf 'missing solution project path: %s\n' "$project_path" >&2
      missing_solution_paths=$((missing_solution_paths + 1))
    fi
  done < <(perl -ne 'while (/Path="([^"]+)"/g) { print "$1\n" }' ProCad.slnx)

  if [[ "$missing_solution_paths" -eq 0 ]]; then
    pass "all ProCad.slnx project paths resolve"
  else
    fail "$missing_solution_paths ProCad.slnx project paths are missing"
  fi
fi

missing_project_references=0
while IFS= read -r csproj; do
  project_dir="$(dirname "$csproj")"
  while IFS= read -r include_path; do
    normalized_include="${include_path//\\//}"
    resolved_path="$project_dir/$normalized_include"
    if [[ ! -f "$resolved_path" ]]; then
      printf 'missing ProjectReference from %s: %s\n' "$csproj" "$include_path" >&2
      missing_project_references=$((missing_project_references + 1))
    fi
  done < <(perl -ne 'while (/<ProjectReference\s+Include="([^"]+)"/g) { print "$1\n" }' "$csproj")
done < <(git ls-files '*.csproj' ':!external/ACadSharp/**' ':!external/ProEdit/**' | sort)

if [[ "$missing_project_references" -eq 0 ]]; then
  pass "all ProjectReference paths resolve"
else
  fail "$missing_project_references ProjectReference paths are missing"
fi

if [[ "$failures" -eq 0 ]]; then
  printf 'Verification complete: PASS\n'
  exit 0
fi

printf 'Verification complete: FAIL (%d checks failed)\n' "$failures" >&2
exit 1
