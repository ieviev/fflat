#!/usr/bin/env bash

set -euo pipefail
__SOURCE_DIRECTORY__=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )

OutputPath=$1
Configuration=$2
TargetFramework=$3

echo "Copying bflat dependencies from $__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/"
echo "OutputPath: $OutputPath"
echo "Configuration: $Configuration"
echo "TargetFramework: $TargetFramework"

#exit 0

# copy the fflat assemblies to the output directory
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/ref" "$OutputPath"
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/lib" "$OutputPath"
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/lib64" "$OutputPath"
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/bin" "$OutputPath"

# # 
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/libobjwriter.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/libjitinterface_x64.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/libclrjit_win_x64_x64.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/libclrjit_unix_x64_x64.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$TargetFramework/libclrjit_universal_arm64_x64.so" "$OutputPath"
