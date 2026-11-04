#!/usr/bin/env bash
# C1 browser gate: the application is real Angular; HTTP responses are synthetic.
# No SQL Server or production resources are used by this runner.
set -Eeuo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
requested_source=''
if [[ $# == 2 && "$1" == '--source' && "$2" =~ ^[0-9a-f]{40}$ ]]; then
  requested_source="$2"
else
  printf 'Usage: %s --source FULL_40_CHARACTER_COMMIT\n' "$0" >&2
  exit 2
fi
web_root="$repo_root/apps/prodimt-pedidos-web"
e2e_root="$repo_root/tests/e2e"
gate_path="${BUILD_C1_BROWSER_GATE_JSON:-$e2e_root/playwright-report/c1-browser-gate/gate.json}"
gate_path="$(node -e 'console.log(require("node:path").resolve(process.argv[1]))' "$gate_path")"
evidence_root="$(dirname -- "$gate_path")"
report_path="$evidence_root/playwright.json"
summary_path="$evidence_root/diagnostico-resumen.json"
source_commit=''
stage='preflight'
completed_stages=''
current_log=''

mkdir -p -- "$evidence_root"
# Remove stale evidence before any check. A failed rerun can never reuse a pass.
rm -f -- "$gate_path" "$report_path" "$summary_path"

write_gate() {
  node - "$gate_path" "$source_commit" "$1" "$stage" "$report_path" "$2" "$completed_stages" <<'NODE'
const fs = require('node:fs');
const crypto = require('node:crypto');
const [path, sourceCommit, status, stage, reportPath, exitCode, completed] = process.argv.slice(2);
let report = null;
let reportSha256 = null;
if (fs.existsSync(reportPath)) {
  const bytes = fs.readFileSync(reportPath);
  reportSha256 = crypto.createHash('sha256').update(bytes).digest('hex');
  try { report = JSON.parse(bytes.toString('utf8')); } catch {}
}
const stats = report?.stats ?? {};
const expectedCases = 240;
const countCases = suites => (suites ?? []).reduce((n, suite) => n +
  (suite.specs ?? []).reduce((m, spec) => m + (spec.tests ?? []).length, 0) + countCases(suite.suites), 0);
const collected = countCases(report?.suites);
if (status === 'passed' && (collected !== expectedCases || stats.expected !== expectedCases || stats.unexpected !== 0 ||
    stats.skipped !== 0 || stats.flaky !== 0 || (report.errors?.length ?? 0) !== 0)) {
  throw new Error('C1 requires all 240 browser cases to pass, without skipped/flaky cases or runner errors.');
}
const evidence = {
  schema: 'prodimt.c1.browser-gate.v1',
  status,
  sourceCommit,
  recordedAtUtc: new Date().toISOString(),
  api: 'mock',
  sqlServerIncluded: false,
  humanAcceptance: false,
  nodeVersion: process.version,
  expectedCases,
  suite: 'all',
  collected,
  passed: stats.expected ?? 0,
  failed: stats.unexpected ?? 0,
  skipped: stats.skipped ?? 0,
  flaky: stats.flaky ?? 0,
  completedStages: completed.split(',').filter(Boolean),
  lastStage: stage,
  exitCode: Number(exitCode),
  reportSha256
};
fs.writeFileSync(path, `${JSON.stringify(evidence, null, 2)}\n`, { mode: 0o600 });
NODE
}

on_exit() {
  local result=$?
  if (( result != 0 )); then
    write_gate failed "$result" || true
    printf 'C1 browser gate failed during %s (exit %s).\n' "$stage" "$result" >&2
    local summarized=false
    if [[ -f "$report_path" ]] && node "$e2e_root/summarize-c1-browser.mjs" "$report_path" "$summary_path" >&2; then
      summarized=true
    fi
    if [[ -n "$current_log" && -f "$current_log" && "$summarized" != true ]]; then
      tail -50 -- "$current_log" >&2
    fi
    if [[ -n "$current_log" ]]; then printf 'Full log: %s\n' "$current_log" >&2; fi
    printf 'Evidence: %s\n' "$gate_path" >&2
  fi
}
trap on_exit EXIT

run_step() {
  stage="$1"
  shift
  current_log="$evidence_root/$stage.log"
  printf 'C1: %s\n' "$stage"
  "$@" > "$current_log" 2>&1
  completed_stages="${completed_stages:+$completed_stages,}$stage"
}

node -e 'if (Number(process.versions.node.split(".")[0]) !== 24) { console.error("Use Node.js 24 for the C1 validation runtime."); process.exit(1); }'
command -v git >/dev/null
command -v npm >/dev/null
source_commit="$(git -C "$repo_root" rev-parse HEAD)"
if [[ ! "$source_commit" =~ ^[0-9a-f]{40}$ ]]; then
  printf 'Cannot resolve the exact source commit.\n' >&2
  exit 1
fi
if [[ "$source_commit" != "$requested_source" ]]; then
  printf 'The checkout does not match the requested C1 source commit.\n' >&2
  exit 1
fi
if [[ -n "$(git -C "$repo_root" status --porcelain --untracked-files=all)" ]]; then
  printf 'The C1 checkout must be clean before validation. Use the delivered bundle checkout.\n' >&2
  exit 1
fi
write_gate running 0
export CI=1
run_step npm-web npm ci --prefix "$web_root"
run_step npm-e2e npm ci --prefix "$e2e_root"
run_step angular-build npm run build --prefix "$web_root"
run_step components npm test --prefix "$web_root"
run_step typecheck npm run typecheck --prefix "$e2e_root"

if [[ -n "${PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH:-}" ]]; then
  if [[ ! -x "$PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH" ]]; then
    printf 'PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH is not executable.\n' >&2
    exit 1
  fi
else
  # Download only to the current user's Playwright cache. No sudo or OS changes.
  run_step chromium-install node "$e2e_root/node_modules/playwright/cli.js" install chromium
fi

stage='browser-suite'
current_log="$evidence_root/browser-suite.log"
printf 'C1: browser-suite (all 240 cases, synthetic HTTP API)\n'
printf 'La suite completa puede tardar varios minutos; el resumen aparecerá al terminar.\n'
(
  cd -- "$e2e_root"
  PLAYWRIGHT_JSON_OUTPUT_FILE="$report_path" node node_modules/playwright/cli.js test \
    --workers=1 --max-failures=0 --reporter=json
) > "$current_log" 2>&1
completed_stages="${completed_stages:+$completed_stages,}browser-suite"

stage='final-source-check'
if [[ "$(git -C "$repo_root" rev-parse HEAD)" != "$source_commit" ||
      -n "$(git -C "$repo_root" status --porcelain --untracked-files=all)" ]]; then
  printf 'Source changed during validation; rerun using a clean checkout.\n' >&2
  exit 1
fi
current_log=''
stage='completed'
write_gate passed 0
printf 'C1 browser gate passed: %s\n' "$gate_path"
