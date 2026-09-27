#!/bin/sh
# Builds Paperdoll to run without .NET installed: one self-contained program per system, packed
# into publish/. Needs python3 for the Windows zip.
# Usage: tools/publish.sh [runtime...]   (default: linux-x64 win-x64 osx-arm64 osx-x64)
set -e
cd "$(dirname "$0")/.."
runtimes=${*:-linux-x64 win-x64 osx-arm64 osx-x64}
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
skia=$(sed -n 's:.*"SkiaSharp" Version="\(.*\)".*:\1:p' Directory.Packages.props)
packages=${NUGET_PACKAGES:-$HOME/.nuget/packages}

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
    # The notices of what the build carries along (see THIRD-PARTY-NOTICES.md): the .NET runtime
    # this publish used, SkiaSharp with HarfBuzzSharp (one set of notices for both), and ANGLE on
    # Windows.
    runtime=$(python3 -c "import json; d = json.load(open('src/Paperdoll.App/obj/project.assets.json')); print(next(x['version'].strip('[]').split(',')[0] for f in d['project']['frameworks'].values() for x in f.get('downloadDependencies', []) if x['name'] == 'Microsoft.NETCore.App.Runtime.$rid'))")
    case "$rid" in win-*) native=win32 ;; osx-*) native=macos ;; *) native=linux ;; esac
    mkdir "$out/licenses"
    cp "$packages/microsoft.netcore.app.runtime.$rid/$runtime/LICENSE.TXT" "$out/licenses/dotnet-LICENSE.txt"
    cp "$packages/microsoft.netcore.app.runtime.$rid/$runtime/THIRD-PARTY-NOTICES.TXT" "$out/licenses/dotnet-THIRD-PARTY-NOTICES.txt"
    cp "$packages/skiasharp.nativeassets.$native/$skia/LICENSE.txt" "$out/licenses/SkiaSharp-HarfBuzzSharp-LICENSE.txt"
    cp "$packages/skiasharp.nativeassets.$native/$skia/THIRD-PARTY-NOTICES.txt" "$out/licenses/SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt"
    case "$rid" in
        win-*)
            angle=$(python3 -c "import json; d = json.load(open('src/Paperdoll.App/obj/project.assets.json')); print(next(k.split('/')[1] for k in d['libraries'] if k.startswith('Avalonia.Angle.Windows.Natives/')))")
            cp "$packages/avalonia.angle.windows.natives/$angle/LICENSE" "$out/licenses/ANGLE-LICENSE.txt" ;;
    esac
    # Some packages mark their text files as programs.
    chmod 644 "$out"/licenses/*
    case "$rid" in
        win-*) (cd publish && python3 -m zipfile -c "$name.zip" "$name") ;;
        *) tar -czf "$out.tar.gz" -C publish "$name" ;;
    esac
    echo "$out ($(du -sh "$out" | cut -f1) unpacked)"
done
