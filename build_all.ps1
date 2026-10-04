# build_all.ps1 - Automated build and native single-file compilation

param(
    [string[]]$Platforms = @("win-x64"),
    [string]$Configuration = "Release",
    [string]$OutputDir = "./bin",
    [string]$Version = "",
    [switch]$SkipTests = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== Stream Helper Build Pipeline ===" -ForegroundColor Cyan

# Locate dotnet with SDK
$dotnet = "dotnet"
$hasSdk = $false
try {
    $sdks = & dotnet --list-sdks 2>$null
    if ($sdks) { $hasSdk = $true }
} catch {}

if (-not $hasSdk) {
    $userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path $userDotnet) {
        $dotnet = $userDotnet
        $env:DOTNET_ROOT = Split-Path $userDotnet
        $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    } else {
        throw ".NET SDK not found. Please run dev_prepare.ps1 first."
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

# Clean any stale temporary directory if present
$staleTmp = Join-Path $scriptDir ".tmp"
if (Test-Path $staleTmp) {
    try { [System.IO.Directory]::Delete($staleTmp, $true) } catch {}
}

# Ensure workspace-safe environment variables
if (-not $env:DOTNET_CLI_HOME) {
    $env:DOTNET_CLI_HOME = Join-Path $scriptDir ".dotnet_home"
}
if (-not $env:APPDATA) {
    $env:APPDATA = Join-Path $env:DOTNET_CLI_HOME "appdata"
}
if (-not $env:NUGET_PACKAGES) {
    $userPackages = Join-Path $env:USERPROFILE ".nuget\packages"
    if (Test-Path $userPackages) {
        $env:NUGET_PACKAGES = $userPackages
    } else {
        $env:NUGET_PACKAGES = Join-Path $env:DOTNET_CLI_HOME ".nuget\packages"
    }
}


if (-not $Version) {
    if ($env:APP_VERSION) {
        $Version = $env:APP_VERSION
    } else {
        try {
            $tag = & git describe --tags --abbrev=0 2>$null
            if ($tag) { $Version = $tag.Trim().TrimStart('v') }
        } catch {}
    }
}
if (-not $Version) {
    $Version = "0.1.0"
}
$cleanAssemblyVer = ($Version -split '-')[0]
Write-Host "Target Version: $Version (AssemblyVersion: $cleanAssemblyVer)" -ForegroundColor Cyan

$absOutputDir = [System.IO.Path]::GetFullPath((Join-Path $scriptDir $OutputDir))
if (-not (Test-Path $absOutputDir)) {
    New-Item -ItemType Directory -Path $absOutputDir -Force | Out-Null
}

Write-Host "Restoring solution dependencies..." -ForegroundColor Cyan
& $dotnet restore StreamHelper.sln -m:1
if ($LASTEXITCODE -ne 0) {
    throw "Restore failed with exit code $LASTEXITCODE"
}

# Disable telemetry prompts during automated builds
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

if (-not $SkipTests) {
    Write-Host "Running tests (x64)..." -ForegroundColor Cyan
    $testProj = Join-Path $scriptDir "tests/StreamHelper.Tests/StreamHelper.Tests.csproj"
    
    Write-Host "Building test project..." -ForegroundColor Cyan
    & $dotnet build $testProj -c $Configuration --no-restore -m:1 -p:BuildInParallel=false
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build tests with exit code $LASTEXITCODE"
    }

    $testDll = Join-Path $scriptDir "tests/StreamHelper.Tests/bin/$Configuration/net10.0-windows/StreamHelper.Tests.dll"
    if (-not (Test-Path $testDll)) {
        $testDll = Get-ChildItem -Path (Join-Path $scriptDir "tests/StreamHelper.Tests/bin/$Configuration") -Filter "StreamHelper.Tests.dll" -Recurse |
            Where-Object { $_.FullName -notlike "*TestResults*" } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }

    $resultsDir = Join-Path $scriptDir "TestResults"
    if (-not (Test-Path $resultsDir)) {
        New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
    }

    $testTmp = Join-Path $scriptDir "tests/StreamHelper.Tests/bin/$Configuration/net10.0-windows/.test_tmp"
    if (-not (Test-Path $testTmp)) {
        New-Item -ItemType Directory -Path $testTmp -Force | Out-Null
    }
    $env:STREAMHELPER_TEST_TEMP = $testTmp

    $runSettingsPath = Join-Path $scriptDir ".runsettings"
    $testArgs = @("--results-directory", $resultsDir)
    if (Test-Path $runSettingsPath) {
        $testArgs += @("--settings", $runSettingsPath)
    }

    # Safely clean legacy bin TestResults if present from older test runs
    $legacyResults = Join-Path $scriptDir "tests/StreamHelper.Tests/bin/$Configuration/net10.0-windows/TestResults"
    if (Test-Path $legacyResults) {
        try {
            [System.IO.Directory]::Delete($legacyResults, $true)
        } catch {
            # Silently ignore if locked or pending deletion
        }
    }

    Write-Host "Executing tests via native MSTest runner..." -ForegroundColor Cyan
    if ($testDll -and (Test-Path $testDll)) {
        & $dotnet $testDll @testArgs
    } else {
        & $dotnet run --project $testProj -c $Configuration --no-build -- @testArgs
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed with exit code $LASTEXITCODE"
    }
} else {
    Write-Host "Skipping tests (-SkipTests specified)..." -ForegroundColor Yellow
}

$serverProj = Join-Path $scriptDir "src/StreamHelper.Server/StreamHelper.Server.csproj"
$clientProj = Join-Path $scriptDir "src/StreamHelper.Client/StreamHelper.Client.csproj"

foreach ($rid in $Platforms) {
    Write-Host "Publishing Server and Client for $rid ($Configuration)..." -ForegroundColor Cyan
    
    $platformOutDir = if ($Platforms.Count -gt 1) { Join-Path $absOutputDir $rid } else { $absOutputDir }
    if (-not (Test-Path $platformOutDir)) {
        New-Item -ItemType Directory -Path $platformOutDir -Force | Out-Null
    }

    Write-Host "Publishing Server -> $platformOutDir" -ForegroundColor Yellow
    & $dotnet publish $serverProj -c $Configuration -r $rid --self-contained true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishSingleFile=true -m:1 -o $platformOutDir `
        -p:Version=$Version -p:AssemblyVersion=$cleanAssemblyVer -p:FileVersion=$cleanAssemblyVer -p:InformationalVersion=$Version
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish Server for $rid"
    }

    Write-Host "Publishing Client -> $platformOutDir" -ForegroundColor Yellow
    & $dotnet publish $clientProj -c $Configuration -r $rid --self-contained true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishSingleFile=true -m:1 -o $platformOutDir `
        -p:Version=$Version -p:AssemblyVersion=$cleanAssemblyVer -p:FileVersion=$cleanAssemblyVer -p:InformationalVersion=$Version
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish Client for $rid"
    }
}

Write-Host "Unblocking built executables..." -ForegroundColor Cyan
Get-ChildItem -Path $absOutputDir -Filter "*.exe" -Recurse | ForEach-Object {
    Unblock-File $_.FullName -ErrorAction SilentlyContinue
}

Write-Host "=== Build Complete ===" -ForegroundColor Green
Get-ChildItem -Path $absOutputDir -Filter "*.exe" -Recurse | ForEach-Object {
    Write-Host "  -> $($_.FullName) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Green
}
