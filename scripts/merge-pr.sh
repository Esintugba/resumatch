#!/usr/bin/env bash
# Merge one approved pull request and verify production, following
# docs/operations/change-and-merge-policy.md.
#
# Usage:
#   scripts/merge-pr.sh <number> "<approved title>" <branch> [options]
#
# Options:
#   --migration-approved  Required when the PR changes API/Migrations/.
#   --dry-run             Run every pre-merge check, then stop without merging.
#   --verify-only         Skip the checks and the merge; only verify production
#                         for a PR that is already merged.
#
# Environment:
#   PRODUCTION_URL   Defaults to the repository variable of the same name.
#   REQUIRED_CHECKS  Space-separated check names that must have succeeded.
#                    Defaults to every job in ci.yml on main except deploy.
#
# Read calls are retried. The merge call runs at most once and is never
# retried: if it fails, check the PR by hand. If the main run, a migration or
# the deploy fails, the script stops; do not retry and do not run Down.
#
# Needs gh (authenticated) and curl. jq is not required: gh --jq is used.
set -uo pipefail

usage() {
  sed -n '2,/^set /{/^#/p}' "$0" | sed 's/^# \{0,1\}//' >&2
  exit 2
}

[ "$#" -ge 3 ] || usage
number="$1" expected_title="$2" expected_branch="$3"
shift 3
migration_approved=false dry_run=false verify_only=false
for option in "$@"; do
  case "$option" in
    --migration-approved) migration_approved=true ;;
    --dry-run) dry_run=true ;;
    --verify-only) verify_only=true ;;
    *) usage ;;
  esac
done
[[ "$number" =~ ^[0-9]+$ ]] || { echo "PR number must be numeric." >&2; exit 2; }

base_branch=main
workflow_file=ci.yml

stop() { echo "STOP: $*" >&2; exit 1; }
info() { echo "--- $*"; }

# Retries a read-only command; prints its stdout once it succeeds.
read_retry() {
  local attempt output
  for attempt in 1 2 3 4; do
    if output=$("$@" 2>/dev/null); then
      printf '%s' "$output" | tr -d '\r'
      return 0
    fi
    sleep $((attempt * 5))
  done
  echo "Read failed after 4 attempts: $*" >&2
  return 1
}

repo=$(read_retry gh repo view --json nameWithOwner --jq .nameWithOwner) ||
  stop "Cannot resolve the repository."

pre_merge_checks() {
  info "Checking PR #$number in $repo"
  local fields
  fields=$(read_retry gh pr view "$number" --json \
    number,title,headRefName,baseRefName,state,isDraft,author,mergeStateStatus,headRefOid \
    --jq '[.number, .title, .headRefName, .baseRefName, .state, .isDraft, .author.login, .mergeStateStatus, .headRefOid] | @tsv') ||
    stop "Cannot read PR #$number."

  local actual_number title branch base state draft author merge_state
  IFS=$'\t' read -r actual_number title branch base state draft author merge_state head_sha <<< "$fields"
  echo "number:     $actual_number"
  echo "title:      $title"
  echo "branch:     $branch -> $base"
  echo "author:     $author"
  echo "state:      $state (draft: $draft), merge state: $merge_state"
  echo "head:       $head_sha"

  [ "$actual_number" = "$number" ] || stop "PR number differs from the approval."
  [ "$title" = "$expected_title" ] || stop "Title differs from the approval: '$title'."
  [ "$branch" = "$expected_branch" ] || stop "Branch differs from the approval: '$branch'."
  [ "$base" = "$base_branch" ] || stop "Base branch is '$base', not '$base_branch'."
  [ "$state" = "OPEN" ] || stop "PR is $state, not OPEN."
  [ "$draft" = "false" ] || stop "PR is a draft."
  [ "$merge_state" = "CLEAN" ] || stop "Merge state is $merge_state, not CLEAN."

  local behind
  behind=$(read_retry gh api "repos/$repo/compare/$base_branch...$head_sha" --jq .behind_by) ||
    stop "Cannot compare the branch with $base_branch."
  [ "$behind" = "0" ] ||
    stop "Branch is $behind commit(s) behind $base_branch. Merge $base_branch into it and wait for a green run."

  local migrations
  migrations=$(read_retry gh pr view "$number" --json files \
    --jq '.files[].path | select(startswith("API/Migrations/"))') ||
    stop "Cannot list the changed files."
  if [ -n "$migrations" ]; then
    echo "Migration files:"
    printf '  %s\n' $migrations
    $migration_approved || stop "PR changes migrations; the approval must cover them (--migration-approved)."
  fi

  info "CI on $head_sha"
  local checks
  checks=$(read_retry gh pr view "$number" --json statusCheckRollup \
    --jq '.statusCheckRollup[] | [(.name // .context), (.status // "COMPLETED"), (.conclusion // .state)] | @tsv') ||
    stop "Cannot read the checks."
  [ -n "$checks" ] || stop "No checks reported for the head commit."

  local name status conclusion failed=0
  while IFS=$'\t' read -r name status conclusion; do
    echo "  $name: $status $conclusion"
    [ "$status" = "COMPLETED" ] || failed=1
    case "$conclusion" in SUCCESS | SKIPPED | NEUTRAL) ;; *) failed=1 ;; esac
  done <<< "$checks"
  [ "$failed" = 0 ] || stop "CI is not green (pending or failed checks above)."

  # By default every job of the base branch's workflow except deploy, which
  # runs only on main. Read from the base, so a PR cannot drop its own checks.
  local required required_checks="${REQUIRED_CHECKS:-}"
  if [ -z "$required_checks" ]; then
    required_checks=$(read_retry gh api -H "Accept: application/vnd.github.raw" \
      "repos/$repo/contents/.github/workflows/$workflow_file?ref=$base_branch" |
      sed -n 's/^    name: *//p' | grep -vxF deploy) ||
      stop "Cannot read the required checks from $workflow_file on $base_branch."
  fi
  [ -n "$required_checks" ] || stop "No required checks found."
  echo "Required: $(printf '%s ' $required_checks)"
  for required in $required_checks; do
    printf '%s\n' "$checks" | grep -qxF "$required"$'\tCOMPLETED\tSUCCESS' ||
      stop "Required check '$required' did not succeed on the head commit."
  done
}

merge() {
  info "Merging PR #$number at $head_sha (single attempt)"
  if ! gh pr merge "$number" --merge --match-head-commit "$head_sha"; then
    stop "The merge call failed. It is not retried; inspect PR #$number by hand."
  fi
}

verify_production() {
  local merge_commit
  merge_commit=$(read_retry gh pr view "$number" --json state,mergeCommit \
    --jq 'select(.state == "MERGED") | .mergeCommit.oid') ||
    stop "Cannot read the merge commit."
  [ -n "$merge_commit" ] || stop "PR #$number is not merged."
  info "Merge commit $merge_commit"

  local run_id="" deadline=$((SECONDS + 300))
  while [ -z "$run_id" ] && [ "$SECONDS" -lt "$deadline" ]; do
    run_id=$(read_retry gh run list --workflow "$workflow_file" --branch "$base_branch" \
      --event push --commit "$merge_commit" --json databaseId --jq '.[0].databaseId // empty') || true
    [ -n "$run_id" ] || sleep 15
  done
  [ -n "$run_id" ] || stop "No $base_branch run started for $merge_commit within 5 minutes."
  info "Waiting for run $run_id (https://github.com/$repo/actions/runs/$run_id)"

  local result="" status conclusion
  deadline=$((SECONDS + 4200))
  while [ "$SECONDS" -lt "$deadline" ]; do
    result=$(read_retry gh run view "$run_id" --json status,conclusion \
      --jq '[.status, .conclusion] | @tsv') || true
    IFS=$'\t' read -r status conclusion <<< "$result"
    [ "$status" = "completed" ] && break
    sleep 30
  done
  [ "${status:-}" = "completed" ] || stop "Run $run_id did not finish within 70 minutes."

  info "Jobs"
  read_retry gh run view "$run_id" --json jobs \
    --jq '.jobs[] | "  \(.name): \(.conclusion)"'
  echo
  echo
  info "Deploy steps"
  local steps
  steps=$(read_retry gh run view "$run_id" --json jobs \
    --jq '.jobs[] | select(.name == "deploy") | .steps[] | "\(.name)\t\(.conclusion)"') ||
    stop "Cannot read the deploy steps."
  printf '%s\n' "$steps" | sed 's/^/  /; s/\t/: /'

  [ "$conclusion" = "success" ] ||
    stop "Run $run_id concluded '$conclusion'. Do not retry and do not run Down; report it."
  printf '%s\n' "$steps" | grep -qxF $'Apply migrations\tsuccess' || stop "Apply migrations did not succeed."
  printf '%s\n' "$steps" | grep -qxF $'Smoke test\tsuccess' || stop "The CI smoke test did not succeed."

  local url="${PRODUCTION_URL:-}"
  [ -n "$url" ] || url=$(read_retry gh variable get PRODUCTION_URL) || stop "PRODUCTION_URL is not set."
  url="${url%/}"

  info "$url/health/version"
  local body reported
  body=$(curl --silent --max-time 60 "$url/health/version")
  reported=$(printf '%s' "$body" | sed -n 's/.*"commit" *: *"\([0-9a-f]*\)".*/\1/p')
  echo "  reported: ${reported:-$body}"
  [ "$reported" = "$merge_commit" ] || stop "/health/version does not report $merge_commit."

  info "$url/health/ready"
  local code
  code=$(curl --silent --output /dev/null --write-out '%{http_code}' --max-time 60 "$url/health/ready")
  echo "  status: $code"
  [ "$code" = "200" ] || stop "/health/ready returned $code."

  if command -v jq > /dev/null; then
    info "Local smoke test"
    bash "$(dirname "$0")/smoke-test.sh" "$url" || stop "The local smoke test failed."
  else
    echo "jq is not installed; relying on the CI smoke test step, which passed."
  fi

  echo
  echo "PR #$number merged as $merge_commit and verified in production."
}

if $verify_only; then
  verify_production
  exit 0
fi

pre_merge_checks
if $dry_run; then
  echo
  echo "Dry run: every check passed. Not merging."
  exit 0
fi
merge
verify_production
