param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source
$ffprobe = (Get-Command ffprobe -ErrorAction Stop).Source
$soundRoot = Join-Path $ProjectRoot 'Assets\Resources\Sound'
$backupRoot = Join-Path $ProjectRoot 'Backups\AudioBeforeOgg'
$projectFullPath = [System.IO.Path]::GetFullPath($ProjectRoot)
$soundFullPath = [System.IO.Path]::GetFullPath($soundRoot)
$backupFullPath = [System.IO.Path]::GetFullPath($backupRoot)

if (-not $soundFullPath.StartsWith($projectFullPath, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $backupFullPath.StartsWith($projectFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Audio conversion paths escaped the Unity project.'
}

$inputs = @(Get-ChildItem -LiteralPath $soundRoot -Recurse -File |
    Where-Object { $_.Extension -in '.wav', '.mp3' } |
    Sort-Object FullName)
if ($inputs.Count -ne 14) {
    throw "Expected 14 WAV/MP3 inputs, found $($inputs.Count)."
}

$results = @()
foreach ($input in $inputs) {
    $relative = [System.IO.Path]::GetRelativePath($soundRoot, $input.FullName)
    $category = $relative.Split([System.IO.Path]::DirectorySeparatorChar)[0]
    $quality = if ($category -eq 'Music') { '5' } else { '2' }
    $output = [System.IO.Path]::ChangeExtension($input.FullName, '.ogg')
    $temporary = $output + '.converting.ogg'
    $oldMeta = $input.FullName + '.meta'
    $newMeta = $output + '.meta'
    if (Test-Path -LiteralPath $output) {
        throw "Output already exists: $output"
    }
    if (-not (Test-Path -LiteralPath $oldMeta)) {
        throw "Unity metadata is missing: $oldMeta"
    }

    $before = & $ffprobe -v error -show_entries format=duration -show_entries stream=sample_rate,channels -of json -- $input.FullName |
        ConvertFrom-Json
    & $ffmpeg -hide_banner -loglevel error -y -i $input.FullName -vn -c:a libvorbis -q:a $quality $temporary
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temporary)) {
        throw "FFmpeg failed: $($input.FullName)"
    }

    $after = & $ffprobe -v error -show_entries format=duration -show_entries stream=codec_name,sample_rate,channels -of json -- $temporary |
        ConvertFrom-Json
    $durationDelta = [math]::Abs([double]$before.format.duration - [double]$after.format.duration)
    if ($after.streams[0].codec_name -ne 'vorbis' -or
        [int]$before.streams[0].sample_rate -ne [int]$after.streams[0].sample_rate -or
        [int]$before.streams[0].channels -ne [int]$after.streams[0].channels -or
        $durationDelta -gt 0.1) {
        Remove-Item -LiteralPath $temporary -Force
        throw "Converted audio validation failed: $($input.FullName)"
    }

    $backupFile = Join-Path $backupRoot $relative
    $backupDirectory = Split-Path -Parent $backupFile
    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    Copy-Item -LiteralPath $input.FullName -Destination $backupFile
    Copy-Item -LiteralPath $oldMeta -Destination ($backupFile + '.meta')

    Move-Item -LiteralPath $temporary -Destination $output
    Move-Item -LiteralPath $oldMeta -Destination $newMeta
    Remove-Item -LiteralPath $input.FullName

    $results += [pscustomobject]@{
        Category = $category
        Source = $relative
        Output = [System.IO.Path]::GetRelativePath($soundRoot, $output)
        BeforeMiB = [math]::Round($input.Length / 1MB, 3)
        AfterMiB = [math]::Round((Get-Item -LiteralPath $output).Length / 1MB, 3)
        DurationDelta = [math]::Round($durationDelta, 4)
    }
}

$results | ConvertTo-Json -Depth 3
