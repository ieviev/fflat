$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "bflat\src\bflat\bflat.csproj"

if (-not (Test-Path $proj)) {
    Write-Host "bflat submodule does not exist"
    Write-Host "load the submodule with: git submodule update --init"
    Write-Host "then set up the nuget key according to https://github.com/bflattened/bflat/blob/master/BUILDING.md"
    exit 1
}

$content = Get-Content -Raw -Path $proj

if ($content -match 'InternalsVisibleTo Include="fflat"') {
    Write-Host "bflat internals visible ok"
} else {
    Write-Host "bflat internals not visible to fflat, adding entry to $proj"
    $content = $content -replace "`r`n", "`n"
    $content = $content -replace '<Project>', '<Project><ItemGroup><InternalsVisibleTo Include="fflat"/></ItemGroup>'
    [System.IO.File]::WriteAllText($proj, $content, (New-Object System.Text.UTF8Encoding($false)))
}
