#!/bin/bash
# Builds AC Coach on Linux with Mono and NSIS (Windows: build.bat).
#   ./build.sh          ACCoach.exe + test tools
#   ./build.sh release  + installer and portable ZIP in <repo>/dist
# Needs: mono-devel (mcs), nsis (makensis), zip – see .claude/hooks/session-start.sh
set -euo pipefail
cd "$(dirname "$0")"                       # ACCoach/zrodla
B=tools/test/bin
mkdir -p "$B"
API=/usr/lib/mono/4.8-api

# System.Speech for the compiler only (see tools/test/SpeechStub.cs)
if [ ! -f "$B/System.Speech.dll" ] || [ tools/test/SpeechStub.cs -nt "$B/System.Speech.dll" ]; then
  mcs -nologo -out:"$B/KeyOf.exe" tools/test/KeyOf.cs
  mono "$B/KeyOf.exe" "$API/System.Web.Extensions.dll" "$B/ms.pub" >/dev/null
  mcs -nologo -target:library -out:"$B/System.Speech.dll" -keyfile:"$B/ms.pub" -delaysign+ tools/test/SpeechStub.cs
fi

mcs -nologo -codepage:65001 -optimize -target:winexe -sdk:4.8 -out:../ACCoach.exe -win32icon:ACCoach.ico \
  -r:System.Core.dll -r:"$B/System.Speech.dll" -r:System.Web.Extensions.dll \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll \
  -resource:src/map_template.html,ACCoach.map_template.html -resource:src/acapp.py,ACCoach.acapp.py src/*.cs 2>&1 | grep -v "CS0219" || true
[ -f ../ACCoach.exe ] || { echo "build failed"; exit 1; }

# test tools: fake game (AcSim), lap replay (AcReplay), one-process runner (Harness)
mcs -nologo -codepage:65001 -out:"$B/AcSim.exe" -r:System.Core.dll tools/AcSim.cs
mcs -nologo -codepage:65001 -out:"$B/AcReplay.exe" -r:System.Core.dll tools/AcReplay.cs
mcs -nologo -out:"$B/Harness.exe" -r:System.Core.dll tools/test/Harness.cs
echo "built: ACCoach/ACCoach.exe"

if [ "${1:-}" = "release" ]; then
  # file names carry the version (from the installer script), e.g. AC_Coach_Setup_1.7.1.exe
  VER=$(grep -m1 '^!define VERSION' ../instalator/ACCoach.nsi | cut -d'"' -f2)
  makensis -V2 ../instalator/ACCoach.nsi
  DIST=../../dist
  rm -rf "$DIST"; mkdir -p "$DIST"
  mv "../instalator/AC_Coach_Setup_$VER.exe" "$DIST/"
  TMP=$(mktemp -d)
  mkdir -p "$TMP/AC Coach/rywale"
  cp -r ../ACCoach.exe "../0. First run.bat" "../HOW TO USE.txt" "../JAK UZYWAC.txt" ../zrodla ../instalator "$TMP/AC Coach/"
  rm -rf "$TMP/AC Coach/zrodla/tools/test/bin" "$TMP/AC Coach/instalator/"AC_Coach_Setup*.exe "$TMP/AC Coach/instalator/.gitignore"
  (cd "$TMP" && rm -f portable.zip && zip -q -r portable.zip "AC Coach")
  mv "$TMP/portable.zip" "$DIST/AC_Coach_portable_$VER.zip"
  rm -rf "$TMP"
  echo "release: dist/AC_Coach_Setup_$VER.exe, dist/AC_Coach_portable_$VER.zip"
fi
