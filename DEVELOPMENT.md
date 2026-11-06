## development

### initial setup

first init the bflat repository:

```bash
git submodule update --init
```

then set up the nuget key according to https://github.com/bflattened/bflat/blob/master/BUILDING.md
e.g. through command-line

```bash
dotnet nuget add source "https://nuget.pkg.github.com/bflattened/index.json" --name "bflat" --username <github_username> --password <github_api_key> --store-password-in-clear-text
```

on Linux/WSL, install the native link dependencies:

```bash
sudo apt-get install -y libc++-dev libbrotli-dev
```

then run the patch script (`build.sh` on Linux, `build.ps1` on Windows):

```bash
./build.sh
```

then make sure bflat builds and compile layouts (takes a long time)

```bash
dotnet build bflat/src/bflat/bflat.csproj -t:BuildLayouts
```

after this the project should compile and run (native dependencies are copied automatically during build)

```bash
dotnet build src/fflat/fflat.fsproj
```

to install locally as a global tool:

```bash
dotnet pack src/fflat/fflat.fsproj -c Release
dotnet tool uninstall --global fflat
dotnet tool install --global fflat --add-source src/fflat/nupkg
```
