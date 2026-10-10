#!/usr/bin/env bash
# Safely cherry-pick one verified upstream commit onto a fork, automatically
# retrying textual conflicts with Git's deterministic `-X theirs` strategy.
# Never pushes. Test failures leave the patch branch available for inspection.
set -Eeuo pipefail

usage() {
  cat <<'USAGE'
Usage (run inside the destination fork's clean Git working tree):
  "$0" https://github.com/OWNER/UPSTREAM.git FULL_COMMIT_SHA

Optional custom test command:
  PATCH_TEST_CMD='dotnet test && ./scripts/smoke-test.sh' "$0" URL SHA

Requires: git, gh (authenticated with GitHub), jq. Test tools are project-specific.
USAGE
}
fail() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

[[ $# -eq 2 ]] || { usage >&2; exit 2; }
SOURCE_URL=$1
COMMIT=${2,,}
[[ "$COMMIT" =~ ^[0-9a-f]{40}$ ]] || fail 'Provide the full 40-character Git commit SHA, not a branch name or abbreviated SHA.'

for cmd in git gh jq; do command -v "$cmd" >/dev/null 2>&1 || fail "Missing dependency: $cmd"; done
gh auth status >/dev/null 2>&1 || fail 'Authenticate first with: gh auth login'
ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || fail 'Run this inside the destination fork clone.'
cd "$ROOT"
[[ -z "$(git status --porcelain --untracked-files=all)" ]] || fail 'Working tree is not clean. Commit or stash changes first.'
ORIGIN_URL=$(git remote get-url origin 2>/dev/null) || fail 'Destination repo must have an origin remote.'

SOURCE_REPO=$(gh repo view "$SOURCE_URL" --json nameWithOwner --jq '.nameWithOwner') || fail "Could not identify source GitHub repository: $SOURCE_URL"
DEST_REPO=$(gh repo view "$ORIGIN_URL" --json nameWithOwner --jq '.nameWithOwner') || fail "Could not identify destination origin on GitHub: $ORIGIN_URL"
DEST_API=$(gh api "repos/$DEST_REPO") || fail "Could not read GitHub metadata for destination $DEST_REPO."
IS_FORK=$(jq -r '.fork // false' <<< "$DEST_API")
PARENT_REPO=$(jq -r '.parent.full_name // empty' <<< "$DEST_API")
[[ "$IS_FORK" == true ]] || fail "$DEST_REPO is not marked as a GitHub fork. No changes made."
[[ -n "$PARENT_REPO" ]] || fail "$DEST_REPO is marked as a fork, but GitHub returned no parent.full_name metadata. No changes made."
[[ "${PARENT_REPO,,}" == "${SOURCE_REPO,,}" ]] || fail "$DEST_REPO is a fork of '$PARENT_REPO', not '$SOURCE_REPO'. No changes made."
printf 'Fork verified: %s -> %s\n' "$DEST_REPO" "$SOURCE_REPO"

API_SHA=$(gh api "repos/$SOURCE_REPO/commits/$COMMIT" --jq '.sha') || fail "Commit $COMMIT was not found in $SOURCE_REPO."
[[ "${API_SHA,,}" == "$COMMIT" ]] || fail 'GitHub returned a different commit SHA.'
git fetch --no-tags "$SOURCE_URL" "$COMMIT" || fail 'Could not fetch the requested commit.'
git cat-file -e "$COMMIT^{commit}" 2>/dev/null || fail 'Fetched object is not a commit.'

if git merge-base --is-ancestor "$COMMIT" HEAD; then
  printf 'Commit %s is already an ancestor of HEAD; nothing to apply.\n' "$COMMIT"
  exit 0
fi
PARENT_COUNT=$(git rev-list --parents -n 1 "$COMMIT" | awk '{print NF-1}')
(( PARENT_COUNT <= 1 )) || fail 'Merge commits are intentionally rejected: select a non-merge commit to avoid guessing its mainline.'
(( PARENT_COUNT == 1 )) || fail 'Root commits have no parent to compare against and are not supported by this one-commit cherry-pick workflow.'

BASE=$(git rev-parse HEAD)
ORIGINAL_BRANCH=$(git branch --show-current)
SHORT=${COMMIT:0:12}
BRANCH="auto-patch/${SHORT}-$(date +%Y%m%d-%H%M%S)-$$"
git switch -c "$BRANCH"

cleanup_failed_branch() {
  # Clear any in-progress pick before returning the checkout to its original ref.
  git cherry-pick --abort >/dev/null 2>&1 || true
  if [[ -n "$ORIGINAL_BRANCH" ]]; then
    git switch "$ORIGINAL_BRANCH" >/dev/null 2>&1 || git switch --detach "$BASE" >/dev/null 2>&1 || true
  else
    git switch --detach "$BASE" >/dev/null 2>&1 || true
  fi
  git branch -D "$BRANCH" >/dev/null 2>&1 || true
}

printf 'Attempting a normal cherry-pick of %s...\n' "$COMMIT"
if git -c advice.mergeConflict=false cherry-pick --no-edit "$COMMIT"; then
  RESOLUTION='clean cherry-pick'
else
  # Retry only when Git actually reports unresolved index entries. Do not mask
  # unrelated failures such as hooks, identity errors, or an empty cherry-pick.
  if [[ -n "$(git ls-files -u)" ]]; then
    printf '\nConflict detected. Aborting the first attempt and retrying with Git strategy option -X theirs.\n'
    git cherry-pick --abort || { cleanup_failed_branch; fail 'Could not abort the conflicted cherry-pick cleanly; returned to the original branch if possible.'; }
    if git -c advice.mergeConflict=false cherry-pick --no-edit -X theirs "$COMMIT"; then
      RESOLUTION='automatic -X theirs merge (incoming side wins overlapping hunks; nearby fork edits in the same hunk may be replaced)'
    else
      git cherry-pick --abort >/dev/null 2>&1 || true
      cleanup_failed_branch
      fail 'Git could not automatically resolve all conflicts, even with -X theirs. Original branch restored; no patch branch retained.'
    fi
  else
    cleanup_failed_branch
    fail 'Cherry-pick failed for a non-conflict reason. Original branch restored; inspect Git identity/hooks and retry.'
  fi
fi

printf 'Patch applied using %s.\n' "$RESOLUTION"

run_tests() {
  if [[ -n "${PATCH_TEST_CMD:-}" ]]; then
    printf 'Running custom tests: %s\n' "$PATCH_TEST_CMD"
    bash -lc "$PATCH_TEST_CMD"
  elif [[ -f Cargo.toml ]] && command -v cargo >/dev/null 2>&1; then
    cargo test
  elif [[ -f go.mod ]] && command -v go >/dev/null 2>&1; then
    go test ./...
  elif [[ -f package.json ]] && jq -e '.scripts.test? | strings' package.json >/dev/null 2>&1; then
    if [[ -f pnpm-lock.yaml ]] && command -v pnpm >/dev/null 2>&1; then pnpm test
    elif [[ -f yarn.lock ]] && command -v yarn >/dev/null 2>&1; then yarn test
    elif command -v npm >/dev/null 2>&1; then npm test
    else printf 'No Node package manager found.\n' >&2; return 125; fi
  elif [[ -f pyproject.toml || -f pytest.ini || -f setup.cfg ]]; then
    if python -c 'import pytest' >/dev/null 2>&1; then python -m pytest
    elif [[ -d tests || -d test ]]; then python -m unittest discover
    else printf 'Python project found, but no supported test runner/tests detected.\n' >&2; return 125; fi
  elif command -v dotnet >/dev/null 2>&1; then
    local solution test_project
    solution=$(find . -maxdepth 5 -type f \( -name '*.sln' -o -name '*.slnx' \) -not -path './.git/*' -not -path '*/bin/*' -not -path '*/obj/*' -print -quit)
    test_project=$(find . -maxdepth 6 -type f -iname '*test*.csproj' -not -path './.git/*' -not -path '*/bin/*' -not -path '*/obj/*' -print -quit)
    if [[ -n "$solution" ]]; then dotnet test "$solution" --verbosity minimal
    elif [[ -n "$test_project" ]]; then dotnet test "$test_project" --verbosity minimal
    else printf 'No .NET solution or test project detected. Set PATCH_TEST_CMD to the project\x27s test/build command.\n' >&2; return 125; fi
  elif [[ -f Makefile ]] && grep -Eq '^[[:space:]]*test[[:space:]]*:' Makefile && command -v make >/dev/null 2>&1; then
    make test
  elif [[ -f build/CTestTestfile.cmake ]] && command -v ctest >/dev/null 2>&1; then
    ctest --test-dir build --output-on-failure
  else
    printf 'No supported test command detected. Set PATCH_TEST_CMD to the project test command.\n' >&2
    return 125
  fi
}

set +e
run_tests
TEST_STATUS=$?
set -e
if (( TEST_STATUS == 0 )); then
  printf '\nSUCCESS: patch applied and tests passed.\nResolution: %s\nBranch: %s\nNothing was pushed. Review with: git show --stat HEAD\n' "$RESOLUTION" "$BRANCH"
elif (( TEST_STATUS == 125 )); then
  printf '\nPATCH APPLIED, BUT TESTS WERE NOT RUN.\nResolution: %s\nBranch: %s\nSet PATCH_TEST_CMD or review with: git show --stat HEAD\n' "$RESOLUTION" "$BRANCH" >&2
  exit 2
else
  printf '\nPATCH APPLIED, BUT TESTS FAILED (exit %s).\nResolution: %s\nBranch: %s\nThe original branch remains untouched. Inspect the failure before deciding whether to keep this commit.\n' "$TEST_STATUS" "$RESOLUTION" "$BRANCH" >&2
  exit 1
fi
