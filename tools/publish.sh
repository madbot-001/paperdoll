#!/bin/sh
# Builds Paperdoll to run without .NET installed: one self-contained program per system, packed
# into publish/. Needs python3 for the Windows zip.
# Usage: tools/publish.sh [runtime...]   (default: linux-x64 win-x64 osx-arm64 osx-x64)
set -e
cd "$(dirname "$0")/.."
runtimes=${*:-linux-x64 win-x64 osx-arm64 osx-x64}
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)

for rid in $runtimes; do
    name="Paperdoll-$version-$rid"
    out="publish/$name"
    rm -rf "$out" "$out.zip" "$out.tar.gz"
    dotnet publish src/Paperdoll.App -c Release -r "$rid" --self-contained \
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true -p:DebugType=none \
        -o "$out" > /dev/null
    # The native drawing libraries bring debug symbols along; nobody running it needs them.
    rm -f "$out"/*.pdb
    cp LICENSE THIRD-PARTY-NOTICES.md README.md "$out/"
    case "$rid" in
        win-*) (cd publish && python3 -m zipfile -c "$name.zip" "$name") ;;
        *) tar -czf "$out.tar.gz" -C publish "$name" ;;
    esac
    echo "$out ($(du -sh "$out" | cut -f1) unpacked)"
done
