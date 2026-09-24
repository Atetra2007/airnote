[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [switch]$SkipTests,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$desktopRoot = Join-Path $repoRoot 'artifacts\desktop'
$publishRoot = Join-Path $desktopRoot 'publish'
$project = Join-Path $repoRoot 'src\Harmonica.Desktop\Harmonica.Desktop.csproj'
$testProjects = @(
    @{ Path = 'tests\Harmonica.Core.Tests\Harmonica.Core.Tests.csproj'; Kind = 'test' },
    @{ Path = 'tests\Harmonica.Windows.Tests\Harmonica.Windows.Tests.csproj'; Kind = 'run' },
    @{ Path = 'tests\Harmonica.Desktop.Tests\Harmonica.Desktop.Tests.csproj'; Kind = 'run' }
)

function Invoke-Checked {
    param([string]$Executable, [string[]]$NativeArguments)
    & $Executable @NativeArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Executable $($NativeArguments -join ' ')"
    }
}

function Find-X64DotNet {
    $candidates = @()
    foreach ($base in @($env:ProgramW6432, $env:ProgramFiles)) {
        if ($base) { $candidates += (Join-Path $base 'dotnet\dotnet.exe') }
    }
    if ($env:DOTNET_ROOT_X64) { $candidates += (Join-Path $env:DOTNET_ROOT_X64 'dotnet.exe') }
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $sdkList = & $candidate --list-sdks
        if ($LASTEXITCODE -eq 0 -and ($sdkList | Where-Object { $_ -match '^10\.0\.' })) {
            return $candidate
        }
    }
    throw 'The x64 .NET 10 SDK was not found. Install/configure it for development; users receive a self-contained app.'
}

$dotnet = Find-X64DotNet
$pythonCommand = Get-Command python -CommandType Application -ErrorAction Stop | Select-Object -First 1
$python = $pythonCommand.Source
Write-Host "Using x64 SDK: $dotnet"
Invoke-Checked $dotnet @('--version')
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Desktop project is missing: $project" }

Push-Location $repoRoot
try {
    if (-not $SkipTests) {
        foreach ($test in $testProjects) {
            $testProject = Join-Path $repoRoot $test.Path
            if (-not (Test-Path -LiteralPath $testProject -PathType Leaf)) { throw "Test project is missing: $testProject" }
            if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $testProject) 'packages.lock.json') -PathType Leaf)) {
                throw "Test dependency lock is missing: $testProject. Generate and review locks before a release build."
            }
            Invoke-Checked $dotnet @('restore', $testProject, '--locked-mode')
            if ($test.Kind -eq 'test') {
                Invoke-Checked $dotnet @('test', $testProject, '--configuration', 'Release', '--no-restore', '--logger', 'console;verbosity=minimal')
            } else {
                Invoke-Checked $dotnet @('run', '--project', $testProject, '--configuration', 'Release', '--no-restore')
            }
        }
    }
    Invoke-Checked $python @('scripts/check_desktop.py', '--self-test')
    # Tests may restore shared project references without a RID. Restore the
    # deployment graph last so they cannot erase win-x64 publish assets.
    if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $project) 'packages.lock.json') -PathType Leaf)) {
        throw 'Desktop dependency lock is missing. Generate and review it before a release build.'
    }
    Invoke-Checked $dotnet @('restore', $project, '--runtime', 'win-x64', '--locked-mode', '-p:SelfContained=true')

    # A fresh, strictly bounded staging directory prevents previous builds from
    # smuggling obsolete binaries or personal files into a newer installer.
    $expectedPublish = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\desktop\publish'))
    if ([IO.Path]::GetFullPath($publishRoot) -ne $expectedPublish -or -not $expectedPublish.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean a publish directory outside the workspace.'
    }
    foreach ($ancestor in @($repoRoot, (Join-Path $repoRoot 'artifacts'), $desktopRoot)) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Staging ancestor must not be a link: $ancestor"
            }
        }
    }
    if (Test-Path -LiteralPath $publishRoot) {
        $stagingItem = Get-Item -LiteralPath $publishRoot -Force
        if ($stagingItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish directory must not be a link.' }
        $linkedChild = Get-ChildItem -LiteralPath $publishRoot -Force -Recurse | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1
        if ($linkedChild) { throw "Refusing to clean staging containing a link: $($linkedChild.FullName)" }
        Remove-Item -LiteralPath $publishRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    Invoke-Checked $dotnet @('publish', $project, '--configuration', 'Release', '--runtime', 'win-x64',
        '--self-contained', 'true', '--no-restore', '--output', $publishRoot,
        '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-p:DebugSymbols=false', '-p:DebugType=None')

    $manifest = Join-Path $desktopRoot 'publish-manifest.json'
    $filesInclude = Join-Path $desktopRoot 'publish-files.iss'
    Invoke-Checked $python @('scripts/check_desktop.py', '--publish', $publishRoot, '--copy-licenses',
        '--manifest', $manifest, '--inno-include', $filesInclude)
    if ($SkipInstaller) {
        Write-Host "Self-contained payload verified: $publishRoot"
        Write-Host 'Installer deliberately skipped. No installation or desktop shortcut was created.'
        return
    }
    if (-not $IsccPath) {
        $compilerCandidates = @(
            (Join-Path $repoRoot 'local\tools\InnoSetup6\ISCC.exe'),
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        )
        $IsccPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
    if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
        throw 'Inno Setup 6 was not found. Use -SkipInstaller locally, or the desktop GitHub Actions workflow; this script never installs a compiler.'
    }
    $compilerInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($IsccPath)
    if ($compilerInfo.FileMajorPart -ne 6) { throw 'Use Inno Setup 6 to build this installer.' }
    Invoke-Checked $IsccPath @('/Qp', "/DPublishDir=$publishRoot", "/DOutputDir=$desktopRoot",
        "/DFilesInclude=$filesInclude", "/DRepoDir=$repoRoot", (Join-Path $repoRoot 'installer\HarmonicaDesk.iss'))
    $setup = Join-Path $desktopRoot 'HarmonicaDesk-Setup-0.2.0-preview.1-win-x64.exe'
    if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Compiler completed without the expected installer.' }
    $digest = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($setup + '.sha256', "$digest  $([IO.Path]::GetFileName($setup))`n", [Text.Encoding]::ASCII)
    Write-Host "Installer built: $setup"
    Write-Host "SHA256: $digest"
} finally {
    Pop-Location
}
