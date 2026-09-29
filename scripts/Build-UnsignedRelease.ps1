param(
    [string]$Version = '1.1.0-dev.20260929.3',
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Use a version such as 1.1.0-dev.20260929.1.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
$publishDir = Join-Path $repoRoot "artifacts/publish/$Version"
$releaseDir = Join-Path $repoRoot "artifacts/release/$Version"
if (Test-Path -LiteralPath $publishDir) { throw "Publish directory already exists: $publishDir. Use a new build version or move the previous output first." }
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Push-Location $repoRoot
try {
    dotnet run --project Echopad.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
    dotnet publish Echopad.App/Echopad.App.csproj -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:SkipInstaller=true -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $publishDir
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination (Join-Path $publishDir 'docs') -Recurse
    Copy-Item -LiteralPath (Join-Path $repoRoot 'graphics') -Destination (Join-Path $publishDir 'graphics') -Recurse
    $zip = Join-Path $releaseDir "EchoPad-$Version-win-x64-unsigned.zip"
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
    if (!$SkipInstaller) {
        $compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
        if (!(Test-Path -LiteralPath $compiler)) { throw 'Install Inno Setup 6 or use -SkipInstaller to produce only the ZIP.' }
        & $compiler "/DAppBuildDir=$publishDir" "/DMyAppVersion=$Version" "/DInstallerOutputDir=$releaseDir" (Join-Path $repoRoot 'installer/Echopad.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    }
    Get-ChildItem -LiteralPath $releaseDir -File | Where-Object Extension -in '.zip','.exe' | Get-FileHash -Algorithm SHA256 |
        ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)" } |
        Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Unsigned release ready: $releaseDir"
}
finally { Pop-Location }
