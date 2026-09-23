#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
artifacts="$root/artifacts"
cache="$artifacts/third-party"
output="$artifacts/macos-loader"
bepinex_commit=f4c1b1103a32884b7440d9681c60cc5d619f284f
doorstop_commit=8e66ca0b189d4c443ba9e7c4f5aac8105582a91c
plthook_commit=24c69df003310fb91f9c2950b5a659ff70e9dfb9
bepinex_asset=BepInEx_macos_universal_5.4.23.5.zip
bepinex_hash=01c2ae782eb016dfd6c345a18dbd2dcafffb3d9d318449d6486689f426b4a323

mkdir -p "$cache"
archive="$cache/$bepinex_asset"
if [[ ! -f "$archive" ]]; then
    curl -fL --retry 3 -o "$archive" "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/$bepinex_asset"
fi
actual_hash="$(shasum -a 256 "$archive" | cut -d ' ' -f 1)"
if [[ "$actual_hash" != "$bepinex_hash" ]]; then
    echo "BepInEx ZIP 해시가 다릅니다: $archive" >&2
    exit 1
fi

work="$(mktemp -d "$artifacts/macos-loader-build.XXXXXX")"
trap 'rm -rf "$work"' EXIT

fetch_source() {
    local url="$1" commit="$2" destination="$3"
    git init -q "$destination"
    git -C "$destination" remote add origin "$url"
    git -C "$destination" fetch -q --depth 1 origin "$commit"
    git -C "$destination" checkout -q FETCH_HEAD
}

fetch_source https://github.com/BepInEx/BepInEx.git "$bepinex_commit" "$work/bepinex"
git -C "$work/bepinex" submodule update --init --depth 1 -q
fetch_source https://github.com/NeighTools/UnityDoorstop.git "$doorstop_commit" "$work/doorstop"
fetch_source https://github.com/kubo/plthook.git "$plthook_commit" "$work/plthook"

cp "$work/plthook/plthook_osx.c" "$work/plthook/plthook.h" "$work/doorstop/src/nix/plthook/"
git -C "$work/doorstop" apply "$root/scripts/patches/macos-plthook.patch"

dotnet_bin="$(command -v dotnet || true)"
if [[ -z "$dotnet_bin" ]]; then dotnet_bin="$artifacts/dotnet/dotnet"; fi
"$dotnet_bin" build "$work/bepinex/BepInEx.Preloader/BepInEx.Preloader.csproj" -c Release -v:q

sdk="${MACOS_SDK:-$(xcrun --sdk macosx --show-sdk-path)}"
sources=(
    "$work/doorstop/src/bootstrap.c"
    "$work/doorstop/src/config/common.c"
    "$work/doorstop/src/runtimes/globals.c"
    "$work/doorstop/src/util/paths.c"
    "$work/doorstop/src/nix/entrypoint.c"
    "$work/doorstop/src/nix/util.c"
    "$work/doorstop/src/nix/jit_memcpy.c"
    "$work/doorstop/src/nix/config.c"
    "$work/doorstop/src/nix/plthook/plthook_osx.c"
)
for arch in arm64 x86_64; do
    xcrun clang -isysroot "$sdk" -arch "$arch" -mmacosx-version-min=11.0 -dynamiclib -fPIC \
        -o "$work/libdoorstop-$arch.dylib" "${sources[@]}"
done
xcrun lipo -create "$work/libdoorstop-arm64.dylib" "$work/libdoorstop-x86_64.dylib" \
    -output "$work/libdoorstop.dylib"

mkdir -p "$work/output"
unzip -q "$archive" -d "$work/output"
cp "$work/bepinex/BepInEx.Preloader/bin/Release/net35/"*.dll "$work/output/BepInEx/core/"
cp "$work/libdoorstop.dylib" "$work/output/libdoorstop.dylib"
sed 's@doorstop_name="libdoorstop.${lib_extension}"@doorstop_name="${doorstop_directory}libdoorstop.${lib_extension}"@' \
    "$work/output/run_bepinex.sh" > "$work/run_bepinex.sh"
mv "$work/run_bepinex.sh" "$work/output/run_bepinex.sh"
chmod +x "$work/output/run_bepinex.sh"
rm -rf "$output"
mv "$work/output" "$output"
echo "macOS 로더 빌드 완료: $output"
