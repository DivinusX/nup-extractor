#!/usr/bin/env bash
# Build Native NUP Extractor (Linux x64) — equivalent of build_native.bat
set -euo pipefail
cd "$(dirname "$0")"

echo "========================================================"
echo "  RUNNING AUTOMATED TEST SUITE (TDD / CI)"
echo "========================================================"
dotnet test tests_native -c Release

echo ""
echo "========================================================"
echo "  ALL TESTS PASSED! COMPILING STANDALONE EXECUTABLE"
echo "========================================================"
dotnet publish src_linux -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o dist_linux

cp -f dist_linux/NUP_Extractor NUP_Extractor
chmod +x NUP_Extractor
rm -rf dist_linux

echo ""
echo "========================================================"
echo "  BUILD SUCCESSFUL! Verified and updated ./NUP_Extractor"
echo "========================================================"
