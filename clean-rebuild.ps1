<#
.SYNOPSIS
    Clean and rebuild VisionMaven.

.DESCRIPTION
    Build output is redirected to <repo root>\bin and <repo root>\obj by
    Directory.Build.props, so the source tree stays clean. This script:

      1. Stops running VisionMaven processes (they lock the output folders).
      2. Removes <repo root>\bin\* and <repo root>\obj\* (per project folder).
      3. Removes any legacy in-tree bin/obj still present under src/ or tests/.
      4. dotnet restore.
      5. dotnet build (Debug by default).

    Any failure aborts with a non-zero exit code.

    NOTE: This file is intentionally ASCII-only so that it parses correctly
    in Windows PowerShell 5.1 regardless of the active code page.

.PARAMETER Configuration
    Build configuration: Debug (default) or Release.

.PARAMETER Gpu
    Build ONNX Runtime against the GPU package (CUDA / TensorRT execution providers).

.PARAMETER FastRebuild
    Fast path: keep obj/ (and therefore the NuGet assets), delete only bin/,
    and build with --no-restore. Use it when packages did not change.

.PARAMETER CleanOnly
    Only remove build output, do not build.

.PARAMETER Run
    Launch VisionMaven after a successful Debug build.

.EXAMPLE
    .\clean-rebuild.ps1

.EXAMPLE
    .\clean-rebuild.ps1 -Configuration Release

.EXAMPLE
    .\clean-rebuild.ps1 -FastRebuild

.EXAMPLE
    .\clean-rebuild.ps1 -CleanOnly

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\clean-rebuild.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Gpu,

    [switch]$FastRebuild,

    [switch]$CleanOnly,

    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Repository root = folder containing this script, so the script works from any cwd.
$root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }

$solution = Join-Path $root 'VisionMaven.sln'
if (-not (Test-Path -LiteralPath $solution)) {
    Write-Host "[ERROR] VisionMaven.sln not found in $root" -ForegroundColor Red
    exit 1
}

# Output folders produced by Directory.Build.props
$outputRoot = Join-Path $root 'bin'
$intermediateRoot = Join-Path $root 'obj'

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Write-Ok([string]$Text) {
    Write-Host "    $Text" -ForegroundColor Green
}

function Write-Warn([string]$Text) {
    Write-Host "    $Text" -ForegroundColor Yellow
}

function Remove-Children([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Ok "$Label not present"
        return @()
    }

    $children = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue)
    if ($children.Count -eq 0) {
        Write-Ok "$Label already empty"
        return @()
    }

    $failed = @()
    foreach ($child in $children) {
        try {
            Remove-Item -LiteralPath $child.FullName -Recurse -Force -ErrorAction Stop
        }
        catch {
            $failed += $child.FullName
        }
    }

    $removed = $children.Count - $failed.Count
    Write-Ok "$Label : removed $removed of $($children.Count) entries"
    return $failed
}

# ------------------------------------------------------------------ 1. stop processes
Write-Step 'Stop running VisionMaven processes'

$running = @(Get-Process -Name 'VisionMaven' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    foreach ($process in $running) {
        try {
            $process.Kill()
            Write-Warn "Killed PID $($process.Id)"
        }
        catch {
            Write-Warn "Failed to kill PID $($process.Id): $($_.Exception.Message)"
        }
    }

    Start-Sleep -Milliseconds 600
}
else {
    Write-Ok 'No running process'
}

# ------------------------------------------------------------------ 2. clean output roots
Write-Step 'Remove build output (bin / obj at repository root)'

$failed = @()
$failed += Remove-Children -Path $outputRoot -Label 'bin'

if ($FastRebuild) {
    Write-Warn 'obj kept (-FastRebuild): NuGet assets are preserved'
}
else {
    $failed += Remove-Children -Path $intermediateRoot -Label 'obj'
}

# ------------------------------------------------------------------ 3. clean legacy in-tree dirs
Write-Step 'Remove any legacy in-tree bin / obj under src and tests'

$legacyRoots = @(
    (Join-Path $root 'src'),
    (Join-Path $root 'tests')
) | Where-Object { Test-Path -LiteralPath $_ }

$legacy = foreach ($searchRoot in $legacyRoots) {
    Get-ChildItem -LiteralPath $searchRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' }
}

$legacyCount = 0
foreach ($target in $legacy) {
    try {
        Remove-Item -LiteralPath $target.FullName -Recurse -Force -ErrorAction Stop
        $legacyCount++
    }
    catch {
        $failed += $target.FullName
    }
}

Write-Ok "Removed $legacyCount legacy directories"

if ($failed.Count -gt 0) {
    Write-Warn 'The following paths could not be removed (locked by a process or antivirus):'
    foreach ($item in $failed) { Write-Warn "  $item" }
    Write-Warn 'Close the IDE / restart the terminal and retry.'
}

if ($CleanOnly) {
    Write-Host ''
    Write-Host 'Clean finished (-CleanOnly, build skipped)' -ForegroundColor Green
    exit 0
}

# ------------------------------------------------------------------ 4. restore
if ($FastRebuild) {
    Write-Step 'Skip restore (-FastRebuild)'
}
else {
    Write-Step 'dotnet restore'
    & dotnet restore $solution --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host "[FAILED] restore exited with $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Ok 'Restore finished'
}

# ------------------------------------------------------------------ 5. build
Write-Step "dotnet build -c $Configuration"

$buildArgs = @('build', $solution, '-c', $Configuration, '--nologo', '--no-restore')
if ($Gpu) {
    $buildArgs += '-p:VisionMavenGpu=true'
    Write-Warn 'GPU build enabled (Microsoft.ML.OnnxRuntime.Gpu)'
}

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
& dotnet @buildArgs
$exitCode = $LASTEXITCODE
$stopwatch.Stop()
$seconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 1)

Write-Host ''
if ($exitCode -ne 0) {
    Write-Host "[FAILED] build exited with $exitCode after $seconds s" -ForegroundColor Red
    exit $exitCode
}

Write-Host "[OK] $Configuration build succeeded in $seconds s" -ForegroundColor Green

$exe = Join-Path $outputRoot "VisionMaven.App\$Configuration\net8.0-windows\VisionMaven.exe"
if (Test-Path -LiteralPath $exe) {
    Write-Host "     $exe" -ForegroundColor DarkGray
}

# ------------------------------------------------------------------ 6. optional run
if ($Run) {
    if ($Configuration -ne 'Debug') {
        Write-Warn '-Run is only supported for Debug; skipped'
    }
    elseif (-not (Test-Path -LiteralPath $exe)) {
        Write-Warn "Executable not found: $exe"
    }
    else {
        Write-Step 'Launch VisionMaven'
        Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
    }
}

exit 0
