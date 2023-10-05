#!/usr/bin/env bash

set -euo pipefail
__SOURCE_DIRECTORY__=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )

OutputPath=$1
Configuration=$2
TargetFramework=$3

echo "OutputPath: $OutputPath"
echo "Configuration: $Configuration"
echo "TargetFramework: $TargetFramework"

# copy the fflat assemblies to the output directory
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/net7.0/ref" "$OutputPath"
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/net7.0/lib" "$OutputPath"
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/net7.0/lib64" "$OutputPath"
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/net7.0/bin" "$OutputPath"

# # 
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/net7.0/libobjwriter.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/net7.0/libjitinterface_x64.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/net7.0/libclrjit_win_x64_x64.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/net7.0/libclrjit_unix_x64_x64.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/net7.0/libclrjit_universal_arm64_x64.so" "$OutputPath"
