param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

# Windows PowerShell 5.1 -File appends a literal quote when OutputPath ends with a backslash.
$OutputPath = $OutputPath.TrimEnd([char]'"')

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$layoutDir = Join-Path $scriptDir "..\..\bflat\layouts\windows-x64"
$layoutDir = (Resolve-Path $layoutDir).Path

Write-Host "Copying bflat dependencies from $layoutDir"
Write-Host "OutputPath: $OutputPath"

foreach ($dir in @("ref", "lib", "bin")) {
    $src = Join-Path $layoutDir $dir
    $dest = Join-Path $OutputPath $dir
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item -Recurse -Force (Join-Path $src "*") $dest
}

# Only the files fflat loads at runtime; not bflat.exe or the notices.
Get-ChildItem -Path $layoutDir -File |
    Where-Object {
        $_.Name -like "clrjit_*.dll" -or
        $_.Name -eq "jitinterface_x64.dll" -or
        $_.Name -eq "WindowsAPIs.txt"
    } |
    ForEach-Object { Copy-Item -Force $_.FullName $OutputPath }
