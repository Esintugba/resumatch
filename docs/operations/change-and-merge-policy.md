# Change and merge policy

A merge to `main` deploys to production and applies pending migrations to Neon. These rules apply to every pull request, including automated ones.

1. **Explicit approval.** Merge only a pull request the owner approved by number and title.
2. **Check before merging.** Run `gh pr view <n> --json number,title,headRefName,author,mergeStateStatus` and compare the number, title and branch with the approval. If any of them differs, do not merge.
3. **Dependabot.** Never merge a Dependabot pull request without its own approval.
4. **One at a time.** Merge one pull request, finish its production check, then move to the next. The production check is:
   - The `main` run succeeds, including the migration step.
   - `/health/version` reports the merge commit.
   - `/health/ready` returns 200.
   - The smoke test passes.
5. **Fresh CI.** Before merging, bring the branch up to date with `main` and wait for a green run on the result.
6. **Failures.** If a migration or deploy fails, stop. Do not retry and do not run `Down`; report what happened.

A pull request that contains a migration needs approval for the migration as well, because merging applies it.

[`scripts/merge-pr.sh`](../../scripts/merge-pr.sh) applies rules 2 and 4–6: it checks the pull request against the approval and CI, merges once without retrying, and verifies production. It stops on the first mismatch or failure.
