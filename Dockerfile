FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update && apt-get install -y \
    libc++-dev \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet tool install --global fflat --version 2.1.1

ENTRYPOINT [ "/root/.dotnet/tools/fflat" ]

# first build the container:
# docker build -t fflat .

# then try compiling the hello world example
# docker run --rm -ti -v ".:/app" fflat '/app/samples/helloworld.fsx'
