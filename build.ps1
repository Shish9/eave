# Builds Eave locally into dist\ (needs the .NET 8 SDK).
# Note: on PCs with Smart App Control turned on, unsigned local builds are blocked from running.
# Signed builds come from GitHub Actions + SignPath (.github/workflows/release.yml).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Remove-Item dist -Recurse -Force -ErrorAction SilentlyContinue

# 1. hook relay (small, no Node needed)
dotnet publish ClaudeRelay -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishTrimmed=true -o ClaudeRelay\bin\publish
if ($LASTEXITCODE) { throw 'relay build failed' }

# 2. the island
dotnet publish ClaudeIsland -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
if ($LASTEXITCODE) { throw 'island build failed' }
Copy-Item ClaudeRelay\bin\publish\relay.exe dist\relay.exe -Force
Remove-Item dist\*.pdb -ErrorAction SilentlyContinue

Get-ChildItem dist | Select-Object Name, @{n='MB'; e={[math]::Round($_.Length/1MB,1)}}
