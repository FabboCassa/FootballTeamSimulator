#!/usr/bin/env bash
# Sim.Core test shards for CI (.github/workflows/ci.yml, job test-simcore).
#   simcore-shards.sh filter <n>   print the dotnet test --filter of shard n (1..count)
#   simcore-shards.sh count        print the number of shards
#   simcore-shards.sh check        prove every test runs in exactly one shard (needs a Release build)
# Shards are balanced on measured fixture time (seconds, PR #166 CI run). The last shard is the
# catch-all: everything not named below, so a new fixture can never be silently skipped.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NS="Sim.Core.Tests"

# ~1101 s
SHARD_1=(
  "$NS.Match.CausalityTests"              # 331
  "$NS.Match.V11PositioningHarnessTests"  # 281
  "$NS.Match.V11ActionSelectionTests"     # 272
  "$NS.Match.DefensiveDutyTests"          # 217
)
# ~1141 s
SHARD_2=(
  "$NS.Match.DefendingAxesTests"             # 293
  "$NS.Match.TeamPhaseHarnessTests"          # 280
  "$NS.Match.InstructionsTests"              # 248
  "$NS.Match.InPossessionInstructionsTests"  # 231
  "$NS.Market.WorldEconomyDeterminismTests"  # 89
)
# Shard 3 (catch-all) ~1128 s: Referee 174, V11SetPiece 157, DeterminismCheck 128, BallDecision 127,
# V11RealismTuning 112, PositionStream 104, ~120 small fixtures 326.
NAMED=2
COUNT=$((NAMED + 1))

# The trailing dot pins the class name, so InstructionsTests never matches InPossessionInstructionsTests.
filter() {
  local n="$1" sep="" out="" fixture
  if [[ "$n" -ge 1 && "$n" -le "$NAMED" ]]; then
    local -n fixtures="SHARD_$n"
    for fixture in "${fixtures[@]}"; do out+="${sep}FullyQualifiedName~${fixture}."; sep="|"; done
  elif [[ "$n" -eq "$COUNT" ]]; then
    local i
    for ((i = 1; i <= NAMED; i++)); do
      local -n fixtures="SHARD_$i"
      for fixture in "${fixtures[@]}"; do out+="${sep}FullyQualifiedName!~${fixture}."; sep="&"; done
      unset -n fixtures
    done
  else
    echo "shard must be 1..$COUNT, got '$n'" >&2; return 2
  fi
  printf '%s\n' "$out"
}

# Fully qualified names: `dotnet test --list-tests` prints bare method names, which collide across fixtures.
list_tests() {
  local out="$1"; shift
  dotnet vstest "$DLL" --ListFullyQualifiedTests "--ListTestsTargetPath:$out" "$@" >/dev/null
  tr -d '\r' <"$out" | sed '/^$/d' | sort >"$out.sorted"
}

check() {
  cd "$ROOT"
  DLL="shared/Sim.Core.Tests/bin/Release/net10.0/Sim.Core.Tests.dll"
  [[ -f "$DLL" ]] || { echo "missing $DLL: build Sim.Core.Tests -c Release first" >&2; return 1; }
  local dir="shared/Sim.Core.Tests/bin/shard-check" fail=0 n
  rm -rf "$dir"; mkdir -p "$dir"

  # The matrix must run exactly shards 1..COUNT, or the catch-all could be the shard nobody runs.
  local matrix
  matrix="$(grep -oE 'shard: \[[0-9, ]+\]' .github/workflows/ci.yml | tr -dc '0-9,')"
  if [[ "$matrix" != "$(seq -s, 1 "$COUNT")" ]]; then
    echo "ci.yml matrix shards [$matrix] != 1..$COUNT"; fail=1
  fi

  list_tests "$dir/all.txt"
  : >"$dir/union.txt"
  for ((n = 1; n <= COUNT; n++)); do
    list_tests "$dir/shard$n.txt" "--TestCaseFilter:$(filter "$n")"
    echo "shard $n: $(wc -l <"$dir/shard$n.txt.sorted") tests"
    cat "$dir/shard$n.txt.sorted" >>"$dir/union.txt"
  done
  sort "$dir/union.txt" -o "$dir/union.txt"
  echo "all:     $(wc -l <"$dir/all.txt.sorted") tests"

  # Multiset compare: a test in two shards shows up as an extra copy, a skipped one as missing.
  local missing extra
  missing="$(comm -23 "$dir/all.txt.sorted" "$dir/union.txt")"
  extra="$(comm -13 "$dir/all.txt.sorted" "$dir/union.txt")"
  [[ -z "$missing" ]] || { echo "in no shard:"; echo "$missing"; fail=1; }
  [[ -z "$extra" ]] || { echo "in more than one shard:"; echo "$extra"; fail=1; }
  for ((n = 1; n <= NAMED; n++)); do
    local -n fixtures="SHARD_$n"
    for fixture in "${fixtures[@]}"; do
      grep -q "^${fixture//./\\.}\." "$dir/shard$n.txt.sorted" || { echo "shard $n: no tests for $fixture"; fail=1; }
    done
    unset -n fixtures
  done
  [[ $fail -eq 0 ]] && echo "OK: every Sim.Core test runs in exactly one shard"
  return $fail
}

case "${1:-}" in
  filter) filter "${2:-}" ;;
  count) echo "$COUNT" ;;
  check) check ;;
  *) echo "usage: $0 filter <n> | count | check" >&2; exit 2 ;;
esac
