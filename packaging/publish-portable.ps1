$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\win-x64"
Write-Host "Publishing a self-contained Windows x64 folder to $out"
dotnet publish (Join-Path $root "src\TabMan.App\TabMan.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $out
Write-Host "Launch: $out\TabMan.exe"
