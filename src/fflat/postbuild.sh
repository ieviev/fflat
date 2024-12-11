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

# copy the fflat assemblies to the output directory
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/ref" "$OutputPath"
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/lib" "$OutputPath"
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/lib64" "$OutputPath"
cp -r "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/bin" "$OutputPath"

# # 
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/libobjwriter.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/libjitinterface_x64.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/libclrjit_win_x64_x64.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/libclrjit_unix_x64_x64.so" "$OutputPath"
cp "$__SOURCE_DIRECTORY__/../../bflat/src/bflat/bin/$Configuration/$BflatFramework/libclrjit_universal_arm64_x64.so" "$OutputPath"
cp -v "$__SOURCE_DIRECTORY__/../../bflat/layouts/windows-x64/WindowsAPIs.txt" "$OutputPath"
# cp -v "$__SOURCE_DIRECTORY__/../../bflat/layouts/linux-glibc-x64/lib/linux/zerolib.dll" "$OutputPath/lib/"
# cp -v "$__SOURCE_DIRECTORY__/../../bflat/layouts/linux-glibc-x64/lib/linux/x64/zerolib.dll" "$OutputPath/lib/"
