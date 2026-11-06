#!/usr/bin/env bash

set -euo pipefail
__SOURCE_DIRECTORY__=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )

if test -e "$__SOURCE_DIRECTORY__/bflat/src/bflat/bflat.csproj"; then
    echo "bflat proj exists ok"
else
    echo "bflat submodule does not exist"
    echo "load the submodule with: git submodule update --init"
    echo "then set up the nuget key according to https://github.com/bflattened/bflat/blob/master/BUILDING.md"
    exit 1
fi


BF_PROJ="$__SOURCE_DIRECTORY__/bflat/src/bflat/bflat.csproj"

if grep -q '<InternalsVisibleTo Include="fflat"/>' "$BF_PROJ"; then
    echo "bflat internals visible ok"
else
    echo "bflat internals not visible to fflat, adding entry to $BF_PROJ"
    sed -i -E 's|(<Project>)|\1<ItemGroup><InternalsVisibleTo Include="fflat"/></ItemGroup>|' "$BF_PROJ"
fi


