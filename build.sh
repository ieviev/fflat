#!/usr/bin/env bash

set -euo pipefail
__SOURCE_DIRECTORY__=$( cd -- "$( dirname -- "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )

if test -e "bflat/src/bflat/bflat.csproj"; then
    echo "bflat proj exists ok"
else
    echo "bflat submodule does not exist"
    echo "load the submodule with: git submodule update --init"
    echo "then set up the nuget key according to https://github.com/bflattened/bflat/blob/master/BUILDING.md"
    exit 1
fi


if grep -q '<InternalsVisibleTo Include="fflat"/>' "bflat/src/bflat/bflat.csproj"; then
    echo "bflat internals visible ok"
else
    echo "bflat internals not visible to fflat, adding entry to bflat/src/bflat/bflat.csproj"
    cat bflat/src/bflat/bflat.csproj \
        | sed -E 's|(<Project>)|\1<ItemGroup><InternalsVisibleTo Include="fflat"/></ItemGroup>|g' \
        | tee bflat/src/bflat/bflat.csproj > /dev/null
fi

echo "building project"

dotnet build -c Debug


# cat ./dist/index.html | sed -E 's/<!-- (.*?) -->/\1/g' | tee ./dist/index.html