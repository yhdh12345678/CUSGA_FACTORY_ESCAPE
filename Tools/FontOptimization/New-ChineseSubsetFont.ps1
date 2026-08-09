param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
[Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)

$sourceFontCandidates = @(
    (Join-Path $ProjectRoot 'Assets\Resources\Front\思源黑体\SourceHanSansSC-Regular-2.otf'),
    (Join-Path $ProjectRoot 'Backups\LegacyFontsBeforeChineseSubset\思源黑体\SourceHanSansSC-Regular-2.otf')
)
$sourceFont = $sourceFontCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
$characterFile = Join-Path $ProjectRoot 'Assets\Editor\FontOptimization\FactoryEscapeChineseCharacterSet.txt'
$outputFont = Join-Path $ProjectRoot 'Assets\Resources\Front\ChineseSubset\FactoryEscapeChineseSubset.otf'

if ([string]::IsNullOrEmpty($sourceFont)) {
    throw "找不到源字体：$($sourceFontCandidates -join '；')"
}

$subsetTool = Get-Command pyftsubset -ErrorAction SilentlyContinue
if ($null -eq $subsetTool) {
    throw '找不到 pyftsubset。请先安装 fontTools。'
}

$codePoints = [Collections.Generic.HashSet[int]]::new()

function Add-CodePoint {
    param([int]$CodePoint)

    if ($CodePoint -ge 0x20 -and -not ($CodePoint -ge 0x7F -and $CodePoint -le 0x9F)) {
        [void]$codePoints.Add($CodePoint)
    }
}

function Add-Text {
    param([string]$Value)

    # 项目文件大多是 ASCII 序列化内容；只匹配非 ASCII，避免逐字符解释大文件。
    foreach ($match in [Text.RegularExpressions.Regex]::Matches($Value, '[^\u0000-\u007F]')) {
        $character = $match.Value[0]
        if (-not [char]::IsSurrogate($character)) {
            Add-CodePoint ([int]$character)
        }
    }
}

# 基础拉丁字符，覆盖数字、英文和常见半角标点。
foreach ($codePoint in 0x20..0x7E) {
    Add-CodePoint $codePoint
}

# GB2312 一级汉字：16 至 55 区，共 3755 个最常用汉字。
$gb2312 = [Text.Encoding]::GetEncoding(936)
foreach ($highByte in 0xB0..0xD6) {
    foreach ($lowByte in 0xA1..0xFE) {
        Add-Text $gb2312.GetString([byte[]]@($highByte, $lowByte))
    }
}
foreach ($lowByte in 0xA1..0xF9) {
    Add-Text $gb2312.GetString([byte[]]@(0xD7, $lowByte))
}

# 保留项目中已经出现的非 ASCII 字符，包括剧情、界面、脚本字符串和资源名称。
$assetsRoot = Join-Path $ProjectRoot 'Assets'
$legacyFontRoot = Join-Path $assetsRoot 'Resources\Front'
$textMeshProRoot = Join-Path $assetsRoot 'TextMesh Pro'
$projectScriptsRoot = Join-Path $assetsRoot 'Scripts'
$editorScriptsRoot = Join-Path $assetsRoot 'Editor'
$sourceFiles = Get-ChildItem -LiteralPath $assetsRoot -Recurse -File | Where-Object {
    $extension = $_.Extension
    $isProjectScript = $extension -eq '.cs' -and (
        $_.FullName.StartsWith($projectScriptsRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $_.FullName.StartsWith($editorScriptsRoot, [StringComparison]::OrdinalIgnoreCase)
    )
    $isRelevantType = $extension -in @('.txt', '.json', '.unity', '.prefab') -or $isProjectScript -or
        ($extension -eq '.asset' -and $_.Length -le 2MB)
    $isRelevantType -and
    -not $_.FullName.Equals($characterFile, [StringComparison]::OrdinalIgnoreCase) -and
    -not $_.FullName.StartsWith($legacyFontRoot, [StringComparison]::OrdinalIgnoreCase) -and
    -not $_.FullName.StartsWith($textMeshProRoot, [StringComparison]::OrdinalIgnoreCase)
}

foreach ($file in $sourceFiles) {
    Add-Text ([IO.File]::ReadAllText($file.FullName))
    Add-Text $file.Name
}
Add-Text '，。！？；：“”‘’（）【】《》、—…·￥'

$orderedCodePoints = [int[]]@($codePoints)
[Array]::Sort($orderedCodePoints)
$builder = [Text.StringBuilder]::new()
foreach ($codePoint in $orderedCodePoints) {
    [void]$builder.Append([char]::ConvertFromUtf32($codePoint))
}

[IO.Directory]::CreateDirectory((Split-Path -Parent $characterFile)) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $outputFont)) | Out-Null
[IO.File]::WriteAllText($characterFile, $builder.ToString(), [Text.UTF8Encoding]::new($false))

& $subsetTool.Source $sourceFont `
    "--text-file=$characterFile" `
    "--output-file=$outputFont" `
    '--layout-features=*' `
    '--glyph-names' `
    '--symbol-cmap' `
    '--legacy-cmap' `
    '--notdef-glyph' `
    '--notdef-outline' `
    '--recommended-glyphs' `
    '--name-IDs=*' `
    '--name-legacy' `
    '--name-languages=*' `
    '--drop-tables=DSIG'

if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $outputFont -PathType Leaf)) {
    throw "字体子集生成失败，pyftsubset 退出代码：$LASTEXITCODE"
}

$gb2312FirstLevelCount = 3755
$outputSize = (Get-Item -LiteralPath $outputFont).Length
Write-Output "字符总数：$($orderedCodePoints.Count)"
Write-Output "GB2312 一级汉字：$gb2312FirstLevelCount"
Write-Output "子集字体：$outputFont"
Write-Output "子集字体字节数：$outputSize"
