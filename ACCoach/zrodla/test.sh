#!/bin/bash
# Smoke test on Linux (Mono): a simulated session through the real trainer – window included – then the
# analysis in English and Polish and the track map in a headless browser. Run ./build.sh first.
set -uo pipefail
cd "$(dirname "$0")"                       # ACCoach/zrodla
B=tools/test/bin
W=$(mktemp -d)
cp ../ACCoach.exe "../HOW TO USE.txt" "../JAK UZYWAC.txt" "$B/System.Speech.dll" "$B/AcSim.exe" "$B/Harness.exe" "$W/"
export HOME="$W/home"                      # a test user: your own settings stay untouched
DATA="$W/data"; mkdir -p "$DATA"
XPID=
if [ -z "${DISPLAY:-}" ]; then Xvfb :97 -screen 0 1400x1000x24 >/dev/null 2>&1 & XPID=$!; export DISPLAY=:97; sleep 1; fi
trap '[ -n "$XPID" ] && kill $XPID 2>/dev/null; rm -rf "$W"' EXIT
fail=0
ok()  { echo "   OK: $1"; }
bad() { echo "   FAIL: $1"; fail=1; }

echo "1/5 simulated session with the window (about 90 s)"
(cd "$W" && timeout 240 mono Harness.exe AcSim.exe -- ACCoach.exe --test-shm --once --train --lang en --data "$DATA" > session.log 2>&1)
laps=$(ls "$DATA"/*/laps/*.csv 2>/dev/null | wc -l)
[ "$laps" -ge 4 ] && ok "$laps laps recorded" || bad "$laps laps recorded (log: $(tail -3 "$W/session.log"))"
head -2 "$(ls "$DATA"/*/laps/*.csv | head -1)" | grep -q slip_ratio_lp && ok "wheel and tyre channels saved" || bad "no wheel channels in the laps"

echo "2/5 offline sessions: lap timer and lap counter change in different frames (about 2 x 50 s)"
for ord in time laps; do
  D2="$W/data_$ord"; mkdir -p "$D2"
  (cd "$W" && timeout 120 mono Harness.exe AcSim.exe --order $ord --laps 4 -- ACCoach.exe --test-shm --once --lang en --data "$D2" > "session_$ord.log" 2>&1)
  n=$(ls "$D2"/*/laps/*.csv 2>/dev/null | wc -l)
  [ "$n" -ge 2 ] && ok "--order $ord: $n laps recorded" || bad "--order $ord: $n laps recorded (log: $(grep -E 'Lap |skipped' "$D2/log.txt" 2>/dev/null | tail -3))"
done

echo "3/5 analysis in English"
out=$(cd "$W" && mono ACCoach.exe analyze --no-open --lang en --data "$DATA" 2>&1)
rep=$(ls -t "$DATA"/*/reports/raport_*.txt | head -1)
echo "$out" | grep -q "WHEELS AND TYRES" && ok "report has the wheels section" || bad "no WHEELS AND TYRES section"
grep -q "front lock-up" "$rep" && ok "the lock-up of the simulator is found" || bad "simulated front lock-up not found"
LC_ALL=C.UTF-8 grep -q '[ąćęłńśźżĄĆĘŁŃŚŹŻ]' "$rep" && bad "Polish text in the English report: $(LC_ALL=C.UTF-8 grep '[ąćęłńśźż]' "$rep" | head -2)" || ok "no Polish text left in the English report"
mapEn=$(ls -t "$DATA"/*/reports/mapa_*.html | head -1)

echo "4/5 analysis in Polish"
sleep 61                                   # reports are named by the minute
out=$(cd "$W" && mono ACCoach.exe analyze --no-open --lang pl --data "$DATA" 2>&1)
echo "$out" | grep -q "KOŁA I OPONY" && echo "$out" | grep -q "blokada przodu" && ok "Polish report" || bad "Polish report incomplete"

echo "5/5 track map in a browser"
if command -v node >/dev/null; then
  node tools/test/check_map.js "$mapEn" && ok "map loads without script errors" || bad "map has script errors"
else echo "   skipped: no node"; fi

[ $fail -eq 0 ] && echo "ALL OK" || echo "SOME TESTS FAILED"
exit $fail
