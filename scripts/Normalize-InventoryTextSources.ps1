$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$localRoot = [IO.Path]::GetFullPath((Join-Path $repo 'local'))
$localPrefix = $localRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$output = [IO.Path]::GetFullPath((Join-Path $localRoot 'calibration-221\inventory\text-normalization-2026-09-30'))
$outputPrefix = $localPrefix
$utf8Strict = [Text.UTF8Encoding]::new($false, $true)
$utf8NoBom = [Text.UTF8Encoding]::new($false)

if (-not $output.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) {
    throw "Output must be a new directory below repository local: $output"
}

function Get-Sha256([byte[]] $bytes) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Resolve-RepoFile([string] $relativePath) {
    if ([string]::IsNullOrWhiteSpace($relativePath) -or [IO.Path]::IsPathRooted($relativePath)) {
        throw "Expected a non-empty relative repository path: $relativePath"
    }
    $nativePath = $relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath((Join-Path $repo $nativePath))
    $repoPrefix = $repo.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Source path escapes repository: $relativePath"
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Source file is missing: $relativePath"
    }
    $fullPath
}

function Get-RelativeRepoPath([string] $fullPath) {
    ([IO.Path]::GetRelativePath($repo, $fullPath)).Replace([IO.Path]::DirectorySeparatorChar, '/')
}

function Write-JsonUtf8([string] $path, [object] $value) {
    $json = ConvertTo-Json -InputObject $value -Depth 100
    [IO.File]::WriteAllText($path, $json + "`n", $utf8NoBom)
}

$inventoryRelative = 'local/calibration-221/inventory/batch-manifest.json'
$inventoryPath = Resolve-RepoFile $inventoryRelative
$inventoryBytes = [IO.File]::ReadAllBytes($inventoryPath)
$inventoryHash = Get-Sha256 $inventoryBytes
$inventory = $utf8Strict.GetString($inventoryBytes) | ConvertFrom-Json

function Get-InventoryItem([string] $variantId) {
    $matches = @($inventory.items | Where-Object { $_.variant_id -ceq $variantId })
    if ($matches.Count -ne 1) { throw "Expected one inventory item for $variantId; found $($matches.Count)." }
    $matches[0]
}

function Verify-InventorySource($item) {
    $path = Resolve-RepoFile ([string]$item.path)
    $hash = Get-Sha256 ([IO.File]::ReadAllBytes($path))
    if ($hash -cne ([string]$item.expected_sha256).ToLowerInvariant()) {
        throw "Original source SHA-256 changed for $($item.variant_id): $hash"
    }
    [pscustomobject]@{ path = $path; sha256 = $hash }
}

$socialId = 'social-49048dcf44c101d3'
$social = Get-InventoryItem $socialId
$socialSource = Verify-InventorySource $social
$socialText = $utf8Strict.GetString([IO.File]::ReadAllBytes($socialSource.path))
if (-not $socialText.StartsWith("用琴：scx48`r`n电话谱：", [StringComparison]::Ordinal) -and
    -not $socialText.StartsWith("用琴：scx48`n电话谱：", [StringComparison]::Ordinal)) {
    throw 'The pitch-only source header changed; stop and review before conversion.'
}

$headerLines = [regex]::Split($socialText, '\r\n|\n|\r')
if ($headerLines.Count -lt 3 -or $headerLines[0] -cne '用琴：scx48' -or $headerLines[1] -cne '电话谱：') {
    throw 'Unexpected pitch-only source header rows.'
}
$newlineLength = if ($socialText.StartsWith("用琴：scx48`r`n", [StringComparison]::Ordinal)) { 2 } else { 1 }
$secondNewline = $socialText.IndexOfAny([char[]]@("`r", "`n"), ('用琴：scx48'.Length + $newlineLength))
if ($secondNewline -lt 0) { throw 'The second pitch-only header line has no line break.' }
$bodyStart = $secondNewline + 1
if ($socialText[$secondNewline] -eq "`r" -and $bodyStart -lt $socialText.Length -and $socialText[$bodyStart] -eq "`n") {
    $bodyStart++
}
$socialBody = $socialText.Substring($bodyStart)
$normalizedBody = $socialBody.Replace('[', '【').Replace(']', '】')

# Independently inventory pitch tokens while leaving all source timing unset.
$pitchEvents = [System.Collections.Generic.List[object]]::new()
$semitones = @(0, 2, 4, 5, 7, 9, 11)
$octaveStack = [System.Collections.Generic.Stack[int]]::new()
$octave = 0
$sharp = $false
$lineNumber = 3
for ($index = 0; $index -lt $socialBody.Length; $index++) {
    $character = $socialBody[$index]
    if ($character -eq "`r" -or $character -eq "`n") {
        if ($character -eq "`r" -and $index + 1 -lt $socialBody.Length -and $socialBody[$index + 1] -eq "`n") { $index++ }
        $lineNumber++
        continue
    }
    if ([char]::IsWhiteSpace($character) -or $character -eq '|') { continue }
    if ($character -eq '[' -or $character -eq '(') {
        $octaveStack.Push($octave)
        $octave = if ($character -eq '[') { 1 } else { -1 }
        continue
    }
    if ($character -eq ']' -or $character -eq ')') {
        if ($octaveStack.Count -eq 0) { throw "Unmatched octave bracket at source line $lineNumber." }
        $octave = $octaveStack.Pop()
        continue
    }
    if ($character -eq '#') {
        if ($index + 1 -ge $socialBody.Length -or $socialBody[$index + 1] -lt '1' -or $socialBody[$index + 1] -gt '7') {
            throw "Unsupported accidental at source line $lineNumber."
        }
        $sharp = $true
        $index++
        $degree = [int]([string]$socialBody[$index])
        $rawToken = '#' + [string]$socialBody[$index]
    }
    elseif ($character -ge '1' -and $character -le '7') {
        $sharp = $false
        $degree = [int]([string]$character)
        $rawToken = [string]$character
    }
    else {
        throw "Unsupported source character '$character' at line $lineNumber."
    }
    $midi = 60 + (12 * $octave) + $semitones[$degree - 1] + $(if ($sharp) { 1 } else { 0 })
    $octaveContext = if ($octave -gt 0) { 'high-octave-group' } elseif ($octave -lt 0) { 'low-octave-group' } else { 'base-octave' }
    $pitchEvents.Add([ordered]@{
        event_number = $pitchEvents.Count + 1
        source_line = $lineNumber
        source_token = $rawToken
        octave_context = $octaveContext
        midi = $midi
        duration_beats = $null
        start_beats = $null
        end_beats = $null
        start_seconds = $null
        end_seconds = $null
        timing_status = 'not-provided-by-source'
    })
}
if ($octaveStack.Count -ne 0) { throw 'Unclosed source octave group.' }
if ($pitchEvents.Count -eq 0) { throw 'No pitch events were extracted from the pitch-only source.' }

$alternativeGitIds = @(
    '4872a0a3ea2efef043cbb002a36436224155cade',
    '3a9f16335d28258ade20983c5879fb36f10af5ea'
)
$recordsPath = Resolve-RepoFile 'local/alternative-repositories-2026-09-25/parsing-audit/source-records.proposed.json'
$recordsBytes = [IO.File]::ReadAllBytes($recordsPath)
$recordsHash = Get-Sha256 $recordsBytes
$recordsDoc = $utf8Strict.GetString($recordsBytes) | ConvertFrom-Json
$alternativeItems = [System.Collections.Generic.List[object]]::new()
foreach ($gitId in $alternativeGitIds) {
    $recordMatches = @($recordsDoc.records | Where-Object { $_.source.git_blob_sha1 -ceq $gitId })
    if ($recordMatches.Count -ne 1) { throw "Expected one pinned-parser record for $gitId; found $($recordMatches.Count)." }
    $record = $recordMatches[0]
    $itemMatches = @($inventory.items | Where-Object { $_.expected_sha256 -ceq $record.source.sha256 })
    if ($itemMatches.Count -ne 1) { throw "Expected one inventory source matching $gitId; found $($itemMatches.Count)." }
    $item = $itemMatches[0]
    $source = Verify-InventorySource $item
    if ($record.source.sha256 -cne $source.sha256) { throw "Pinned parser record hash differs from inventory for $($item.variant_id)." }
    if ($record.parser_kind -cne 'chizhou-dfh-tab' -or $record.parse_status -cne 'blocked' -or $record.candidate_event_count -ne 0) {
        throw "Pinned parser state changed for $($item.variant_id); stop and review."
    }
    $eventPath = Resolve-RepoFile ([string]$record.events_path)
    $eventBytes = [IO.File]::ReadAllBytes($eventPath)
    $eventHash = Get-Sha256 $eventBytes
    $eventDoc = $utf8Strict.GetString($eventBytes) | ConvertFrom-Json
    $noteRows = @($record.source_notes_with_timing)
    $parsedEvents = @($eventDoc.source_events)
    if ($noteRows.Count -ne $record.source_note_count -or $parsedEvents.Count -ne $noteRows.Count) {
        throw "The pinned parser note counts disagree for $($item.variant_id)."
    }
    $bpm = [double]$record.source_bpm
    $normalizedNotes = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $noteRows.Count; $index++) {
        $note = $noteRows[$index]
        $parsed = $parsedEvents[$index]
        if ([int]$note.midi -ne [int]$parsed.midi -or
            [Math]::Abs(([double]$note.duration_beats) - ([double]$parsed.beats)) -gt 0.000000001 -or
            [Math]::Abs(([double]$note.start_beats) - ([double]$parsed.start_beats)) -gt 0.000000001) {
            throw "Detailed source record and event export disagree at event $($index + 1) for $($item.variant_id)."
        }
        $start = [double]$note.start_beats
        $duration = [double]$note.duration_beats
        $end = [double]$note.end_beats
        $normalizedNotes.Add([ordered]@{
            event_number = $index + 1
            kind = 'note'
            midi = [int]$note.midi
            source_pitch_token = [string]$note.source_pitch_token
            source_key_token = [string]$note.source_key_token
            source_rhythm_token = [string]$note.source_rhythm_token
            measure = [int]$note.measure
            start_beats = $start
            duration_beats = $duration
            end_beats = $end
            start_seconds = [Math]::Round($start * 60.0 / $bpm, 12)
            duration_seconds = [Math]::Round($duration * 60.0 / $bpm, 12)
            end_seconds = [Math]::Round($end * 60.0 / $bpm, 12)
            timing_basis = 'pinned-source-parser-bar-grid-and-explicit-bpm'
        })
    }
    $overlapCount = 0
    if ([string]$record.issue -match '([0-9]+) overlapping note interval') { $overlapCount = [int]$Matches[1] }
    if ($overlapCount -le 0) { throw "Expected a sequential-conversion blocker for $($item.variant_id)." }
    $headers = @([regex]::Split($utf8Strict.GetString([IO.File]::ReadAllBytes($source.path)), '\r\n|\n|\r') | Select-Object -First 3)
    $measureAudit = @($record.measure_audit)
    $emptyBars = @($measureAudit | Where-Object empty_measure).Count
    $tailBars = @($measureAudit | Where-Object { $_.tail_rest_beats -gt 0 }).Count
    $overflowBars = @($measureAudit | Where-Object { $_.overflow_beats -gt 0 }).Count
    $alternativeItems.Add([ordered]@{
        variant_id = [string]$item.variant_id
        title = [string]$item.title
        source_path = [string]$item.path
        source_sha256 = $source.sha256
        source_header_lines = $headers
        parser = [string]$record.parser
        parser_script_path = 'local/alternative-repositories-2026-09-25/parsing-audit/build_audit.py'
        parser_script_sha256 = Get-Sha256 ([IO.File]::ReadAllBytes((Resolve-RepoFile 'local/alternative-repositories-2026-09-25/parsing-audit/build_audit.py')))
        parser_record_path = 'local/alternative-repositories-2026-09-25/parsing-audit/source-records.proposed.json'
        parser_record_sha256 = $recordsHash
        parser_event_export_path = [string]$record.events_path
        parser_event_export_sha256 = $eventHash
        source_bpm = [int]$record.source_bpm
        source_bpm_explicit = [bool]$record.source_bpm_is_explicit
        source_transposition_header = $record.source_transposition_header
        source_note_count = [int]$record.source_note_count
        declared_note_count = $record.declared_note_count
        measure_count = [int]$record.measure_count
        total_beats = [double]$record.source_total_beats
        duration_seconds_derived_from_bpm = [Math]::Round(([double]$record.source_total_beats) * 60.0 / $bpm, 12)
        overlap_intervals = $overlapCount
        sequential_scorecodec_candidate = 'blocked-to-preserve-absolute-note-timing'
        candidate_blocker = [string]$record.issue
        empty_measure_count = $emptyBars
        underfilled_measure_count = $tailBars
        overflow_measure_count = $overflowBars
        measure_audit = $measureAudit
        events = $normalizedNotes.ToArray()
        rest_events_synthesized = $false
        notes_or_rhythm_tokens_dropped = $false
        limitations = @($record.limitations)
        normalization_rules = @(
            'Printed 简谱 note rows provide source pitch tokens; every row is cross-checked against its 键位 row.',
            '节奏 tokens are interpreted with the pinned parser; b-suffixed values are explicit beat counts and numeric denominators use quarter-note beats.',
            'Measure starts advance by the declared meter; note overruns remain at their source-derived absolute positions, including overlaps.',
            'No sequential HarmonicaPlayer text candidate is emitted because the mono parser would change overlapping note timing.',
            'Empty-bar and tail-rest metadata is retained in measure_audit; no unlisted rests are synthesized.'
        )
    })
}

$convertedRelative = 'local/calibration-221/inventory/text-normalization-2026-09-30/converted/social-49048dcf44c101d3.txt'
$convertedNativeRelative = $convertedRelative.Replace('/', [IO.Path]::DirectorySeparatorChar)
$convertedFull = [IO.Path]::GetFullPath((Join-Path $repo $convertedNativeRelative))
$copyHeaders = @(
    '@format=HarmonicaPlayer/1',
    '@title=夜航星',
    '@bpm=100',
    '@gap=20',
    "@source-original-path=$($social.path)",
    "@source-original-sha256=$($socialSource.sha256)",
    '@source-bpm-status=unknown; 100 is a required diagnostic placeholder only',
    "@source-header-line-1=$($headerLines[0])",
    "@source-header-line-2=$($headerLines[1])",
    '@source-rhythm-status=not-provided; one beat per note is parser default and is not verified source timing',
    '@source-octave-rule=source square brackets map to high-octave groups; source parentheses remain low-octave groups'
)
$convertedText = ($copyHeaders -join "`n") + "`n`n" + $normalizedBody
$convertedBytes = $utf8NoBom.GetBytes($convertedText)
$convertedHash = Get-Sha256 $convertedBytes

$socialReference = [ordered]@{
    type = 'pitch-only-diagnostic-text-conversion'
    source_original_path = [string]$social.path
    source_original_sha256 = $socialSource.sha256
    source_header_lines_preserved = @($headerLines[0], $headerLines[1])
    source_bpm_status = 'unknown'
    source_rhythm_status = 'not-provided'
    conversion_rules = @(
        'Only the two non-musical source header rows are moved into @source-header-* fields; their text is preserved exactly.',
        'Square octave groups are mapped to HarmonicaPlayer high-octave groups; parenthesized groups remain low-octave groups.',
        'Pitch tokens and accidentals are retained in their original order.',
        'The copy requires @bpm=100 and defaults each note to one beat for parser validation only; neither duration nor tempo is source-verified.',
        'bpm_known=false prevents playback claims; normalized source event export below leaves all timing fields null.'
    )
    parser_copy_bpm = 100
    parser_copy_duration_beats_per_note = 1
    timing_claim = $false
}

$auditManifest = [ordered]@{
    schema_version = 1
    purpose = 'Diagnostic parser validation of a pitch-only text conversion; not a calibrated score.'
    items = @([ordered]@{
        variant_id = $socialId
        title = [string]$social.title
        path = $convertedRelative
        expected_sha256 = $convertedHash
        bpm_known = $false
        bpm_basis = 'diagnostic-only-no-source-tempo'
        accepted_timeline_path = $null
        source_original_path = [string]$social.path
        source_original_sha256 = $socialSource.sha256
        conversion_status = 'verified-diagnostic-copy'
        source_files = @(
            [ordered]@{ path = [string]$social.path; role = 'original'; sha256 = $socialSource.sha256 },
            [ordered]@{ path = $convertedRelative; role = 'diagnostic-copy'; sha256 = $convertedHash }
        )
        reference = $socialReference
    })
}

$socialNormalized = [ordered]@{
    variant_id = $socialId
    title = [string]$social.title
    source_path = [string]$social.path
    source_sha256 = $socialSource.sha256
    source_header_lines = @($headerLines[0], $headerLines[1])
    conversion_copy_path = $convertedRelative
    conversion_copy_sha256 = $convertedHash
    conversion_copy_bpm = 100
    conversion_copy_timing_claim = $false
    pitch_event_count = $pitchEvents.Count
    events = $pitchEvents.ToArray()
    discarded_content = @()
    ambiguity = @('The original supplies pitch tokens but no BPM or rhythm symbols. The normalized event export preserves pitch only; duration and absolute time remain null.')
    normalization_rules = @($socialReference.conversion_rules)
}

$normalizedDocument = [ordered]@{
    schema_version = 1
    generated_utc = [DateTimeOffset]::UtcNow
    source_inventory_manifest_path = $inventoryRelative
    source_inventory_manifest_sha256 = $inventoryHash
    scope = 'Three TXT sources that the raw batch auditor could not parse.'
    items = @($socialNormalized) + $alternativeItems.ToArray()
    no_source_or_catalog_modified = $true
    no_audio_or_network_used = $true
}

New-Item -ItemType Directory -Path (Split-Path -Parent $convertedFull) -Force | Out-Null
[IO.File]::WriteAllBytes($convertedFull, $convertedBytes)
Write-JsonUtf8 (Join-Path $output 'batch-manifest.json') $auditManifest
Write-JsonUtf8 (Join-Path $output 'normalized-source-events.json') $normalizedDocument
Write-JsonUtf8 (Join-Path $output 'normalization-report.json') ([ordered]@{
    schema_version = 1
    source_inventory_manifest_path = $inventoryRelative
    source_inventory_manifest_sha256 = $inventoryHash
    normalized_source_events_path = 'local/calibration-221/inventory/text-normalization-2026-09-30/normalized-source-events.json'
    generated_conversion_count = 1
    blocked_sequential_conversion_count = $alternativeItems.Count
    pitch_only_source_event_count = $pitchEvents.Count
    alternative_note_counts = @($alternativeItems | ForEach-Object { [ordered]@{ variant_id = $_.variant_id; source_note_count = $_.source_note_count; overlap_intervals = $_.overlap_intervals; conversion_status = $_.sequential_scorecodec_candidate } })
    conversion_sha256 = $convertedHash
    no_source_or_catalog_modified = $true
})

"Generated diagnostic pitch-only copy with $($pitchEvents.Count) extracted pitches; retained two overlapping-source timelines ($($alternativeItems[0].source_note_count), $($alternativeItems[1].source_note_count) note events) without sequential conversion."
