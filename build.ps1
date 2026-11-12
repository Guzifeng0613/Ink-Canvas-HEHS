<#
.SYNOPSIS
    Build / test / run Ink Canvas (WinUI 3).

.DESCRIPTION
    Wraps the three things that are easy to get wrong on a clean machine:
      * the package cache is kept off the system drive,
      * the platform must be x64 (the solution declares only x64),
      * the app is unpackaged, so it runs straight from the build output.

.EXAMPLE
    .\build.ps1 build
    .\build.ps1 test
    .\build.ps1 run
    .\build.ps1 selftest
#>
param(
    [ValidateSet('build', 'test', 'run', 'selftest', 'publish')]
    [string]$Task = 'build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Keep the NuGet cache off C: — it is the tightest drive on the dev machine.
    [string]$PackageCache = 'D:\dev-cache\nuget'
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$env:NUGET_PACKAGES = $PackageCache
New-Item -ItemType Directory -Force -Path $PackageCache | Out-Null

$sln = Join-Path $repo 'InkCanvas.slnx'
$appProject = Join-Path $repo 'src\InkCanvas.App\InkCanvas.App.csproj'
$testProject = Join-Path $repo 'tests\InkCanvas.Core.Tests\InkCanvas.Core.Tests.csproj'

function Invoke-Dotnet([string[]]$arguments) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

switch ($Task) {
    'build' {
        Invoke-Dotnet @('build', $sln, '-c', $Configuration, '-p:Platform=x64', '--nologo')
    }
    'test' {
        Invoke-Dotnet @('test', $testProject, '-c', $Configuration, '-p:Platform=x64', '--nologo')
    }
    'publish' {
        Invoke-Dotnet @('publish', $appProject, '-c', $Configuration, '-p:Platform=x64',
            '-o', (Join-Path $repo 'artifacts\app'), '--nologo')
    }
    'run' {
        Invoke-Dotnet @('build', $appProject, '-c', $Configuration, '-p:Platform=x64', '--nologo')
        $exe = Join-Path $repo "src\InkCanvas.App\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\InkCanvas.exe"
        Start-Process -FilePath $exe | Out-Null
        Write-Host "Started $exe"
    }
    'selftest' {
        Invoke-Dotnet @('build', $appProject, '-c', $Configuration, '-p:Platform=x64', '--nologo')
        $exe = Join-Path $repo "src\InkCanvas.App\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\InkCanvas.exe"
        $out = Join-Path $env:TEMP 'inkcanvas-selftest'
        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $p = Start-Process -FilePath $exe -ArgumentList "--selftest=$out" -PassThru
        Start-Sleep -Seconds 8
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        Get-Content (Join-Path $out 'selftest.json') -Raw
    }
}