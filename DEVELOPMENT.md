## development

### initial setup

first init the bflat repository:

```bash
git submodule update --init
```

then set up the nuget key according to https://github.com/bflattened/bflat/blob/master/BUILDING.md
e.g. through command-line

````bash
dotnet nuget add source "https://nuget.pkg.github.com/bflattened/index.json" --name "bflat" --username <github_username> --password <github_api_key> --store-password-in-clear-text
````

then make sure bflat builds and compile layouts (takes a long time)

```bash
dotnet build bflat/src/bflat/bflat.csproj -t:BuildLayouts
```

then run the build script

```bash
`./build.sh`
```

after this the project should compile and run

