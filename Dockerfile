# first build the container:
# docker build -t fflat_container_name .

# -v flag mounts current directory to /temp in the container
# so the container can compile './samples/helloworld.fsx' in the current directory
# docker run --rm -ti -v ".:/temp" fflat '/temp/samples/helloworld.fsx'

FROM mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm
RUN apt-get update && apt-get install -y \
    libc++-dev \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet tool install --global fflat --version 2.0.7

ENTRYPOINT [ "/root/.dotnet/tools/fflat" ]
