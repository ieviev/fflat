#!/usr/bin/env bash

dotnet publish \
  src/fflat/fflat.fsproj \
  -c Release \
  --framework net7.0 \
  -r linux-x64 \
  --self-contained \
  -o /mnt/g/proj/fflat/publish
  
#  -p:PublishSingleFile=true \  