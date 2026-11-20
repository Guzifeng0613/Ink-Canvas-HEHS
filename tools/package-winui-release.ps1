param(
    [string]$Version = '3.5.0',
    [string]$OutputDirectory = 'artifacts\release-3.5.0'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory)) }
$app = Join-Path $outputRoot 'InkCanvas'
if (Test-Path -LiteralPath $app) { throw 'Use a fresh release output directory so older build files cannot enter the release.' }
New-Item -ItemType Directory -Path $app -Force | Out-Null
& dotnet publish (Join-Path $repoRoot 'src\InkCanvas.App\InkCanvas.App.csproj') -c Release -p:Platform=x64 -o $app --nologo
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
$modules = @('InkCanvas.Modules.AnalyticGeometry', 'InkCanvas.Modules.Pdf', 'InkCanvas.Modules.ScreenTracking')
foreach ($module in $modules) {
    & (Join-Path $PSScriptRoot 'pack-extension.ps1') -Project (Join-Path $repoRoot ('src\' + $module + '\' + $module + '.csproj')) -Kind Module -OutputDirectory $outputRoot
    if ($LASTEXITCODE -ne 0) { throw ('Module publish failed ' + $module) }
}
& (Join-Path $PSScriptRoot 'pack-extension.ps1') -Project (Join-Path $repoRoot 'samples\InkCanvas.SamplePlugin\InkCanvas.SamplePlugin.csproj') -Kind Plugin -OutputDirectory $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Sample plugin publish failed.' }
& (Join-Path $PSScriptRoot 'pack-extension.ps1') -Project (Join-Path $repoRoot 'src\InkCanvas.Plugins.LegacyImport\InkCanvas.Plugins.LegacyImport.csproj') -Kind Plugin -OutputDirectory $outputRoot
if ($LASTEXITCODE -ne 0) { throw 'Legacy import plugin publish failed.' }
$docs = Join-Path $app 'Docs'
New-Item -ItemType Directory -Path $docs -Force | Out-Null
foreach ($name in @('ANALYTIC-GEOMETRY.md', 'PDF.md', 'EXTENSIONS.md')) { Copy-Item -LiteralPath (Join-Path $repoRoot ('docs\' + $name)) -Destination $docs }
$archive = Join-Path $outputRoot ('InkCanvas-WinUI3-' + $Version + '-win-x64.zip')
Compress-Archive -LiteralPath $app -DestinationPath $archive
$lines = Get-ChildItem -LiteralPath $outputRoot -Filter '*.zip' -File | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name }
[IO.File]::WriteAllLines((Join-Path $outputRoot 'SHA256SUMS.txt'), $lines, [Text.UTF8Encoding]::new($false))
Write-Output $archive
