FROM mcr.microsoft.com/dotnet/sdk:10.0
# make linker find system brotli
ENV LDFLAGS="-L/lib/x86_64-linux-gnu"
ENV LIBRARY_PATH="/lib/x86_64-linux-gnu"
ENV LD_LIBRARY_PATH="/lib/x86_64-linux-gnu"
RUN apt-get update && apt-get install -y \
    libbrotli-dev \
    libc++-dev \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet tool install --global fflat --version 2.1.3
WORKDIR /app
ENV PATH=/root/.dotnet/tools:$PATH
ENTRYPOINT [ "/root/.dotnet/tools/fflat"]

# first build the container:
# > docker build -t fflat .

# then try compiling the hello world example
# > docker run --rm -ti -v ".:/app" fflat '/app/samples/helloworld.fsx'
# now samples/helloworld should work!
