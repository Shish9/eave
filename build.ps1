# Builds Eave locally into dist\: Eave.exe, relay.exe and, with Inno Setup installed, Eave-Setup-<version>.exe
# (needs the .NET 8 SDK).
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

# 3. the installer, dist\Eave-Setup-<version>.exe (needs Inno Setup 6: https://jrsoftware.org/isdl.php)
$version = @(([xml](Get-Content ClaudeIsland\ClaudeIsland.csproj)).Project.PropertyGroup.Version | Where-Object { $_ })[0]
$iscc = @(
  (Get-Command iscc -ErrorAction SilentlyContinue).Source,
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if ($iscc) {
  & $iscc "/DAppVersion=$version" installer\Eave.iss
  if ($LASTEXITCODE) { throw 'installer build failed' }
} else {
  Write-Host 'Inno Setup not found: skipping the installer. Install it to build dist\Eave-Setup-*.exe.'
}

Get-ChildItem dist | Select-Object Name, @{n='MB'; e={[math]::Round($_.Length/1MB,1)}}
