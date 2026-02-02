#!/usr/bin/env bash

set -euo pipefail
__SOURCE_DIRECTORY__=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )

OutputPath=$1
Configuration=$2
TargetFramework=$3
BflatFramework=$4

echo "Copying bflat dependencies from $__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/"
echo "OutputPath: $OutputPath"
echo "Configuration: $Configuration"
echo "TargetFramework: $TargetFramework"
echo "Bflat framework: $BflatFramework"

#exit 0
LAYOUT_DIR="$__SOURCE_DIRECTORY__/../../bflat/layouts/linux-glibc-x64"

# copy the fflat assemblies to the output directory
cp -r "$LAYOUT_DIR/ref" "$OutputPath"
cp -r "$LAYOUT_DIR/lib" "$OutputPath"
cp -r "$LAYOUT_DIR/lib64" "$OutputPath"
cp -r "$LAYOUT_DIR/bin" "$OutputPath"

# # 
cp "$LAYOUT_DIR/libobjwriter.so" "$OutputPath"
cp "$LAYOUT_DIR/libjitinterface_x64.so" "$OutputPath"
cp "$LAYOUT_DIR/libclrjit_win_x64_x64.so" "$OutputPath"
cp "$LAYOUT_DIR/libclrjit_unix_x64_x64.so" "$OutputPath"
cp "$LAYOUT_DIR/libclrjit_universal_arm64_x64.so" "$OutputPath"
cp "$LAYOUT_DIR/WindowsAPIs.txt" "$OutputPath"
