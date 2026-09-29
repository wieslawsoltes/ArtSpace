#!/usr/bin/env bash
# Compare immutable, successful base artifacts. Never builds or runs PR-supplied native tools from the base.
set -euo pipefail
: "${BASE_SHA:?BASE_SHA is required}"
: "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required}"
[[ "$BASE_SHA" =~ ^[0-9a-f]{40}$ ]] || { echo 'Invalid base SHA' >&2; exit 1; }
mkdir -p artifacts/ui-performance
run=$(gh api "repos/$GITHUB_REPOSITORY/actions/workflows/build.yml/runs?branch=main&event=push&status=success&per_page=30" \
  --jq ".workflow_runs | map(select(.head_sha == \"$BASE_SHA\" and .head_repository.full_name == \"$GITHUB_REPOSITORY\")) | .[0].id // empty")
if [ -z "$run" ]; then
  echo 'No retained successful Build for this exact base; baseline comparison skipped.'
  exit 0
fi
artifact=$(gh api "repos/$GITHUB_REPOSITORY/actions/runs/$run/artifacts" --jq '.artifacts[] | select(.name == "ArtSpace-browser" and .expired == false) | .id')
if [ -z "$artifact" ]; then
  echo 'Base browser artifact is unavailable; baseline comparison skipped.'
  exit 0
fi
gh run download "$run" --repo "$GITHUB_REPOSITORY" --name ArtSpace-browser --dir artifacts/baseline
jq -e --arg sha "$BASE_SHA" '.commit == $sha' artifacts/baseline/build-info.json
cp artifacts/baseline/build-info.json artifacts/ui-performance/baseline-build.json
cp artifacts/site/build-info.json artifacts/ui-performance/optimized-build.json
python3 scripts/serve-site.py --directory artifacts/baseline --port 4174 > artifacts/ui-performance/baseline-server.log 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT
ARTSPACE_URL=http://127.0.0.1:4174/ArtSpace/ ARTSPACE_BENCHMARK_BASELINE=1 \
  npx playwright test tests/browser/ui-performance.spec.mjs --grep 'selection latency report' --reporter=line --output=artifacts/baseline-test-results
python3 - <<'PY'
import json
from pathlib import Path
root = Path('artifacts/ui-performance')
a = json.loads((root / 'baseline.json').read_text())
b = json.loads((root / 'optimized.json').read_text())
assert a['nodes'] == b['nodes']
assert len(a['samplesMs']) == len(b['samplesMs'])
import statistics
assert a['medianMs'] == statistics.median(a['samplesMs'])
assert b['medianMs'] == statistics.median(b['samplesMs'])
report = {
  'baseline': a,
  'optimized': b,
  'medianRatio': a['medianMs'] / b['medianMs'],
  'p95Ratio': a['p95Ms'] / b['p95Ms'],
  'samplesPerVariant': len(a['samplesMs']),
  'note': 'One sequential paired CI experiment on software graphics. Baseline runs after optimized. Both variants wait for matching inspector controls when available. Diagnostics implementation differs between builds, so this is not an instrumentation-neutral benchmark or a hardware presentation/FPS claim.'
}
(root / 'comparison.json').write_text(json.dumps(report, indent=2))
print('PAIRED_UI_SELECTION ' + json.dumps(report))
PY
