#!/bin/bash
# Cloud sessions: tools to build and test AC Coach on Linux (see ACCoach/zrodla/build.sh and test.sh).
#   mono-devel – C# compiler and runtime, nsis – the installer, zip – the portable package,
#   xvfb – a screen for the window in tests. Chromium + Playwright for the map test come with the image.
set -euo pipefail
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi
need=()
command -v mcs >/dev/null || need+=(mono-devel)
command -v makensis >/dev/null || need+=(nsis)
command -v zip >/dev/null || need+=(zip)
command -v Xvfb >/dev/null || need+=(xvfb)
if [ ${#need[@]} -gt 0 ]; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq || true
  apt-get install -y -qq "${need[@]}" >/dev/null
fi
echo "AC Coach build tools ready: $(mcs --version 2>/dev/null | head -1); $(makensis -VERSION 2>/dev/null)"
