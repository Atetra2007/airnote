$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$local = Join-Path $repo 'local'
$runId = 'audit-catalog-batch-test-' + [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $local $runId
$inputRoot = Join-Path $testRoot 'input'
$output = Join-Path $testRoot 'output'
$manifestPath = Join-Path $testRoot 'manifest.json'
$testRootFull = [IO.Path]::GetFullPath($testRoot)
$localFull = [IO.Path]::GetFullPath($local).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $testRootFull.StartsWith($localFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to create test files outside repository local: $testRootFull"
}
if (Test-Path -LiteralPath $testRootFull) { throw "Unexpected existing test directory: $testRootFull" }

function Get-Sha256([string] $path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Assert-True([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}

try {
    New-Item -ItemType Directory -Path $inputRoot -Force | Out-Null
    $utf8 = [Text.UTF8Encoding]::new($false)
    $knownPath = Join-Path $inputRoot 'known.txt'
    $knownText = "@format=HarmonicaPlayer/1`n@title=Batch audit fixed test`n@bpm=120`n@gap=20`n`n1 2 0`n"
    [IO.File]::WriteAllText($knownPath, $knownText, $utf8)
    $shortPath = Join-Path $inputRoot 'short.txt'
    $shortText = "@format=HarmonicaPlayer/1`n@title=Short note fixed test`n@bpm=300`n@gap=10`n`n1:0.01 2:0.01 3:0.01`n"
    [IO.File]::WriteAllText($shortPath, $shortText, $utf8)
    $timelinePath = Join-Path $inputRoot 'accepted-timeline.json'
    $timeline = @{
        schema_version = 1
        events = @(
            @{ midi = 60; beats = 1; start_seconds = 0; end_seconds = 0.5 }
            @{ midi = 62; beats = 1; start_seconds = 0.5; end_seconds = 1.0 }
            @{ midi = $null; beats = 1; start_seconds = 1.0; end_seconds = 1.5 }
        )
    } | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($timelinePath, $timeline, $utf8)

    $relativeBase = "local/$runId/input"
    $knownRelative = "$relativeBase/known.txt"
    $shortRelative = "$relativeBase/short.txt"
    $timelineRelative = "$relativeBase/accepted-timeline.json"
    $knownHash = Get-Sha256 $knownPath
    $shortHash = Get-Sha256 $shortPath
    $timelineHash = Get-Sha256 $timelinePath
    $manifest = @{
        schema_version = 1
        items = @(
            @{ variant_id = 'fixed-known'; title = 'Fixed known BPM'; path = $knownRelative; expected_sha256 = $knownHash; bpm_known = $true; accepted_timeline_path = $timelineRelative; reference = 'synthetic test fixture' }
            @{ variant_id = 'fixed-unknown'; title = 'Fixed unknown BPM'; path = $knownRelative; expected_sha256 = $knownHash; bpm_known = $false; reference = 'synthetic test fixture' }
            @{ variant_id = 'fixed-hash-mismatch'; title = 'Fixed hash mismatch'; path = $knownRelative; expected_sha256 = ('0' * 64); bpm_known = $true }
            @{ variant_id = 'fixed-short-notes'; title = 'Fixed short notes'; path = $shortRelative; expected_sha256 = $shortHash; bpm_known = $true }
            @{ variant_id = 'fixed-missing-path'; title = 'Fixed missing path'; path = ''; bpm_known = $true; format = 'missing' }
            @{ variant_id = 'fixed-unsupported-format'; title = 'Fixed unsupported format'; path = $timelineRelative; expected_sha256 = $timelineHash; bpm_known = $true; format = 'json' }
        )
    } | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($manifestPath, $manifest, $utf8)

    $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
    $auditScript = Join-Path $repo 'scripts/Audit-CatalogBatch.cs'
    if (-not (Test-Path -LiteralPath $dotnet)) { throw "Required x64 .NET SDK executable was not found: $dotnet" }
    $runOutput = & $dotnet run $auditScript -- $repo $manifestPath $output 2>&1
    $auditExitCode = $LASTEXITCODE
    $auditJson = Join-Path $output 'audit.json'
    Assert-True (Test-Path -LiteralPath $auditJson) "Audit report was not created. Output: $($runOutput -join [Environment]::NewLine)"
    Assert-True ($auditExitCode -eq 1) "Expected audit exit code 1 for deliberate fixture failures; got $auditExitCode."

    $report = Get-Content -LiteralPath $auditJson -Raw | ConvertFrom-Json
    Assert-True ($report.item_count -eq 6) 'The auditor did not retain every manifest item.'
    Assert-True ($report.manifest_sha256 -match '^[0-9a-f]{64}$' -and $report.catalog_sha256 -match '^[0-9a-f]{64}$') 'Manifest/catalog SHA-256 values are missing.'
    Assert-True ($report.human_listening -eq 'pending' -and $report.real_game -eq 'not-tested') 'Human listening and real-game boundaries were not preserved.'

    $known = $report.items | Where-Object variant_id -eq 'fixed-known'
    Assert-True ($known.status -eq 'technical-checks-passed') 'Known-BPM control score did not pass technical checks.'
    $knownDetail = Get-Content (Join-Path $output $known.detail_file) -Raw | ConvertFrom-Json
    Assert-True ($knownDetail.accepted_timeline.status -eq 'matched') 'Accepted event timeline did not match.'
    Assert-True ($knownDetail.events.Count -eq 3 -and $null -eq $knownDetail.events[2].midi) 'The export omitted a rest event or MIDI null.'
    Assert-True (($knownDetail.game_gap_checks | Where-Object { $_.status -ne 'software-virtual-playback-passed' }).Count -eq 0) 'Known-BPM full-score simulation did not cover all gaps.'
    Assert-True ($knownDetail.is_calibrated -eq $false -and $knownDetail.restart_hash_validation -eq 'passed-hash-stable-unreviewed') 'Isolated restart did not retain the hash without calibration.'

    $unknown = $report.items | Where-Object variant_id -eq 'fixed-unknown'
    Assert-True ($unknown.status -eq 'diagnostic-only-unknown-bpm' -and $unknown.audit_bpm -eq 100) 'Unknown BPM did not use diagnostic BPM 100.'
    $unknownDetail = Get-Content (Join-Path $output $unknown.detail_file) -Raw | ConvertFrom-Json
    Assert-True (($unknownDetail.game_gap_checks | Where-Object { $_.status -ne 'diagnostic-only-no-playback' }).Count -eq 0) 'Unknown BPM incorrectly ran a playback pass.'
    Assert-True (($unknownDetail.game_gap_checks | Where-Object { $_.diagnostic_bpm -ne 100 }).Count -eq 0) 'Unknown BPM diagnostic basis was not explicit.'

    $short = $report.items | Where-Object variant_id -eq 'fixed-short-notes'
    $shortDetail = Get-Content (Join-Path $output $short.detail_file) -Raw | ConvertFrom-Json
    Assert-True ($short.status -eq 'failed' -and $shortDetail.production_parse -eq 'rejected') 'Production short-note rejection was not retained.'
    Assert-True ($shortDetail.events.Count -eq 3) 'Diagnostic recovery did not export all short-note events.'
    Assert-True (($shortDetail.game_gap_checks | Where-Object { $_.issues.Count -ne 3 }).Count -eq 0) 'Short-note issues were not fully reported at every gap.'

    $missing = $report.items | Where-Object variant_id -eq 'fixed-missing-path'
    Assert-True ($missing.status -eq 'missing-input' -and $missing.errors.Count -gt 0) 'A missing input path did not retain its missing-input status.'
    $mismatch = $report.items | Where-Object variant_id -eq 'fixed-hash-mismatch'
    Assert-True ($mismatch.status -eq 'failed' -and $mismatch.imported_isolated -eq 'skipped-hash-mismatch') 'Hash mismatch was imported or not isolated.'
    $unsupported = $report.items | Where-Object variant_id -eq 'fixed-unsupported-format'
    Assert-True ($unsupported.status -eq 'unsupported-input') 'Unsupported input format was not retained as unsupported.'
    Assert-True ($report.failed_count -ge 2 -and $report.missing_input_count -eq 1 -and $report.unsupported_input_count -eq 1) 'Per-item failure/missing/unsupported counts were not accumulated.'

    "PASS: batch auditor fixed fixtures; all 6 items retained, known/unknown BPM boundaries, event/timeline export, complete short-note issues, hash mismatch, missing/unsupported statuses and continuation validated."
}
finally {
    if (Test-Path -LiteralPath $testRootFull) {
        $resolved = [IO.Path]::GetFullPath($testRootFull)
        if (-not $resolved.StartsWith($localFull, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove a test directory outside repository local: $resolved"
        }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
