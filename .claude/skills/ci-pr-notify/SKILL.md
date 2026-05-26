---
name: ci-pr-notify
description: Make GitHub Actions CI reliably wake a subscribed Claude Code (web) session when a run completes — on success AND failure. Use when the user says their CI "doesn't notify", "stays silent on green", "only pings on failure", the agent has to poll/keep checking CI status, or they want to set up the subscribe-to-PR autonomous-iteration loop in a repo. Specific to GitHub Actions + the subscribe_pr_activity webhook bridge; not for GitLab/Jenkins/other CI.
---

# CI → PR-comment notification bridge for Claude Code on the web

Make a GitHub Actions workflow reliably wake a subscribed agent session when a
run finishes — success included — using a PR comment as the signal.

## The core fact (why naive setups fail silently)

A session subscribed via `subscribe_pr_activity` is woken by
**`issue_comment.created`** webhooks. It is **NOT** reliably woken by:

- `issue_comment.edited` — so PATCH-updating a "sticky" comment in place
  updates the comment but does **not** wake the session.
- check-run `completed` / `success` events — so a green run with no new
  comment is silent.

Therefore: **the wake signal must be a freshly *created* PR comment on every
run completion.** Everything below follows from that.

## Diagnosis checklist (when a repo "doesn't notify")

Look at the existing workflow's PR-comment step and check for these
anti-patterns, in order of likelihood:

1. The comment step is gated `if: failure()` → never fires on green. **Fix:**
   `if: always()`.
2. The step PATCHes an existing comment in place (find-by-marker → `gh api
   -X PATCH`) → edit doesn't wake the session. **Fix:** delete-then-create.
3. No comment step at all; the workflow relies on the check-run status to
   notify → unreliable. **Fix:** add the comment step below.
4. Missing `permissions: pull-requests: write` → POST/DELETE 403 silently.
5. The agent is polling (`sleep` loops, repeated `gh pr checks`) instead of
   subscribing → replace with subscribe + end-turn (see Agent side).

## Workflow side — the pattern

Add (or convert the existing comment step to) this. Runs on every outcome,
deletes any prior marked comment, then POSTs a fresh one so a `created`
event fires.

```yaml
permissions:
  contents: read
  pull-requests: write        # REQUIRED — default token is contents:read only

# ... inside the job, as the last step(s):

- name: Post CI status to PR comment
  if: always() && github.event.pull_request.number
  env:
    GH_TOKEN:   ${{ github.token }}
    PR_NUMBER:  ${{ github.event.pull_request.number }}
    RUN_URL:    ${{ github.server_url }}/${{ github.repository }}/actions/runs/${{ github.run_id }}
    JOB_STATUS: ${{ job.status }}     # success | failure | cancelled
  run: |
    MARKER='<!-- ci-status:sticky -->'
    {
      printf '%s\n' "$MARKER"
      case "$JOB_STATUS" in
        success)
          printf '## CI success — `%s`\n\n' "${{ github.run_id }}"
          printf '**Run:** %s\n\n' "$RUN_URL"
          printf '**Head SHA:** `%s`\n' "${{ github.event.pull_request.head.sha }}"
          ;;
        cancelled)
          printf '## CI cancelled — `%s`\n\n' "${{ github.run_id }}"
          printf '**Run:** %s\n' "$RUN_URL"
          ;;
        *)
          printf '## CI failure — `%s`\n\n' "${{ github.run_id }}"
          printf '**Run:** %s\n\n' "$RUN_URL"
          printf '**Head SHA:** `%s`\n\n' "${{ github.event.pull_request.head.sha }}"
          LOG=ci-run.log               # see "tee the log" below
          TAIL="$( [ -f "$LOG" ] && tail -c 50000 "$LOG" || echo '(no log captured)' )"
          printf '<details><summary>Log tail</summary>\n\n```\n%s\n```\n\n</details>\n' "$TAIL"
          ;;
      esac
    } > /tmp/ci-comment.md
    # Delete every prior marked comment, then POST fresh (fires created event).
    IDS="$(gh api "repos/${{ github.repository }}/issues/${PR_NUMBER}/comments" \
      --jq "[.[] | select(.body | startswith(\"$MARKER\")) | .id] | .[]" 2>/dev/null || true)"
    for ID in ${IDS}; do
      gh api -X DELETE "repos/${{ github.repository }}/issues/comments/${ID}" >/dev/null || true
    done
    gh api -X POST "repos/${{ github.repository }}/issues/${PR_NUMBER}/comments" \
      -F body=@/tmp/ci-comment.md >/dev/null
```

Notes:
- **Delete-then-POST, not PATCH.** This is the whole point — a created comment
  wakes the session; an edited one does not. The marker keeps the thread to a
  single current comment despite re-creating it each run.
- **Tee the log for failure diagnostics.** Earlier in the job run the build/
  test command as `set -o pipefail; <cmd> 2>&1 | tee ci-run.log`. Bound the
  tail to ~50 KB so the comment stays under GitHub's 65 536-char limit.
- The head SHA + run URL in the body let the agent correlate the event to a
  commit without extra API calls.
- Triggers: `pull_request: [opened, synchronize, reopened]`. A no-op/empty
  commit changes no path and can skip path-filtered workflows — don't rely on
  empty commits to re-trigger.

## Agent side

- Call `subscribe_pr_activity(owner, repo, pullNumber)` **once**, then end the
  turn. Events arrive as `<github-webhook-activity>` messages that wake the
  session.
- **Do not poll.** No `sleep` loops, no repeated `gh pr checks` / status reads.
  The created-comment event is the signal. (Check-run *failure* events
  sometimes also arrive, but never depend on them — the comment is the
  dependable channel, especially for success.)
- On each event: read the comment body (heading line tells you
  success/failure/cancelled); on failure, read the embedded log tail,
  diagnose, push a fix — the push triggers a new run and a new comment.

## What does NOT work (don't try)

- `WebFetch` cannot read Actions step logs or artifact zips — they're
  auth-gated even on public repos. The PR-comment bridge is the only reliable
  way to get CI diagnostics into the session.
- Relying on `$GITHUB_STEP_SUMMARY` to notify — it's not a comment, fires no
  webhook, and the scraper often misses its HTML.

## Verify it works

After landing the workflow change, push a commit and confirm a
`<github-webhook-activity>` event arrives for the run **on success** (the
common regression is that only failures were ever wired). If the success run
is silent, re-check: `if: always()`, delete-then-POST (not PATCH), and
`pull-requests: write`.
