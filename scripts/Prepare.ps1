# Windows PowerShell 5.1; downloads only. Never launches the player.
[CmdletBinding()]
param(
    [ValidateSet('All', 'Player', 'Scores')][string]$Only = 'All',
    [string[]]$SongId = @(),
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$kitRoot = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $kitRoot 'catalog/upstream.lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$catalog = Get-Content -LiteralPath (Join-Path $kitRoot 'catalog/songs.json') -Raw -Encoding UTF8 | ConvertFrom-Json
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Assert-FileName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name -ne [IO.Path]::GetFileName($Name) -or $Name.Contains('..') -or $Name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw "Invalid filename in catalogue: $Name"
    }
}

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}

function Assert-Integrity([string]$Path, [long]$Size, [string]$Sha256, [string]$GitBlob) {
    if ((Get-Item -LiteralPath $Path).Length -ne $Size) { throw "Size mismatch: $Path" }
    if ($Sha256) {
        if ((Get-Sha256 $Path) -ne $Sha256) { throw "SHA256 mismatch: $Path" }
    } elseif ($GitBlob) {
        $bytes = [IO.File]::ReadAllBytes($Path)
        $prefix = [Text.Encoding]::UTF8.GetBytes("blob $($bytes.Length)" + [char]0)
        $sha = [Security.Cryptography.SHA1]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash([byte[]]($prefix + $bytes))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
        if ($actual -ne $GitBlob) { throw "Git blob hash mismatch: $Path" }
    } else { throw 'Expected digest is missing.' }
}

function Get-PinnedFile([string]$Url, [string]$Path, [long]$Size, [string]$Sha256, [string]$GitBlob = '') {
    $uri = [Uri]$Url
    if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('github.com', 'raw.githubusercontent.com')) { throw 'Unexpected download host.' }
    if (Test-Path -LiteralPath $Path) {
        # Never overwrite an existing edited or damaged file.
        Assert-Integrity $Path $Size $Sha256 $GitBlob
        Write-Host "VERIFIED: $([IO.Path]::GetFileName($Path))"
        return
    }
    if ($VerifyOnly) { throw "Missing file: $Path" }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $partial = $Path + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
    try {
        Invoke-WebRequest -Uri $uri.AbsoluteUri -OutFile $partial -UseBasicParsing -TimeoutSec 180 -MaximumRedirection 5
        Assert-Integrity $partial $Size $Sha256 $GitBlob
        Move-Item -LiteralPath $partial -Destination $Path
        Write-Host "DOWNLOADED + VERIFIED: $([IO.Path]::GetFileName($Path))"
    } finally {
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial }
    }
}

function Expand-VerifiedPlayer([string]$ArchivePath, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $base = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
        $seen = @{}
        foreach ($entry in $zip.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $Destination $entry.FullName))
            if (-not $target.StartsWith($base, [StringComparison]::OrdinalIgnoreCase) -or $entry.FullName.Contains(':')) { throw 'Unsafe archive entry.' }
            if ($seen.ContainsKey($target)) { throw 'Duplicate archive entry.' }
            $seen[$target] = $true
        }
        if (-not (Test-Path -LiteralPath $Destination)) {
            if ($VerifyOnly) { throw "Missing extracted player: $Destination" }
            [IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $Destination)
        }
        # On repeat runs, check every extracted byte; never silently overwrite.
        foreach ($entry in $zip.Entries) {
            if (-not $entry.Name) { continue }
            $target = Join-Path $Destination $entry.FullName
            if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Missing extracted file: $target" }
            if ((Get-Item -LiteralPath $target).Length -ne $entry.Length) { throw "Changed extracted file: $target" }
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $expected = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
            if ((Get-Sha256 $target) -ne $expected) { throw "Changed extracted file: $target" }
        }
    } finally { $zip.Dispose() }
    Write-Host "EXTRACTED FILES VERIFIED: $Destination"
}

try {
    $selected = @($catalog.songs)
    if ($SongId.Count -gt 0) {
        foreach ($id in $SongId) { if ($id -notin $catalog.songs.id) { throw "Unknown song id: $id" } }
        $selected = @($catalog.songs | Where-Object { $_.id -in $SongId })
        if (@($selected | Where-Object { $_.PSObject.Properties['legacy_compatible'] -and $_.legacy_compatible -eq $false }).Count -gt 0) {
            throw 'Selected community songs require the desktop app, which downloads and converts them locally. No files were downloaded.'
        }
    }
    $selected = @($selected | Where-Object { -not $_.PSObject.Properties['legacy_compatible'] -or $_.legacy_compatible -ne $false })
    if ($Only -ne 'Scores') {
        Assert-FileName $lock.asset.name
        $archive = Join-Path $kitRoot ('local/downloads/' + $lock.asset.name)
        Get-PinnedFile $lock.asset.url $archive $lock.asset.size $lock.asset.sha256
        Expand-VerifiedPlayer $archive (Join-Path $kitRoot ('local/player/' + $lock.tag))
    }
    if ($Only -ne 'Player') {
        foreach ($song in $selected) {
            Assert-FileName $song.filename
            $path = Join-Path $kitRoot ('local/scores/' + $song.filename)
            Get-PinnedFile $song.download_url $path $song.size $song.sha256 $song.git_blob_sha1
        }
    }
    Write-Host ''
    Write-Host 'READY FOR OFFLINE CHECKS. The player was NOT started.'
    Write-Host 'Read README.md. Keep log-only mode enabled when later testing with the game closed.'
} catch {
    Write-Host ('FAILED: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'No player was started. Do not disable security settings; manual download links are in the catalogue.'
    exit 1
}
