param(
    [string]$SourcePath,
    [string]$TranslationPath,
    [switch]$RequireNoEnglish
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $projectRoot 'Assets\GameContent\TheIntercept\Translation\TheIntercept.en.ink.txt'
}
if ([string]::IsNullOrWhiteSpace($TranslationPath)) {
    $TranslationPath = Join-Path $projectRoot 'Assets\GameContent\TheIntercept\Translation\TheIntercept.zh-CN.ink.txt'
}

$source = Get-Content -Raw -Encoding UTF8 -LiteralPath $SourcePath
$translation = Get-Content -Raw -Encoding UTF8 -LiteralPath $TranslationPath
$failures = [System.Collections.Generic.List[string]]::new()

function Get-Captures([string]$text, [string]$pattern, [int]$group = 1) {
    return @([regex]::Matches($text, $pattern) | ForEach-Object { $_.Groups[$group].Value })
}

function Assert-Sequence([string]$name, [object[]]$expected, [object[]]$actual) {
    if ($expected.Count -ne $actual.Count) {
        $script:failures.Add("$name 数量不同：英文 $($expected.Count)，中文 $($actual.Count)。")
        return
    }

    for ($index = 0; $index -lt $expected.Count; $index++) {
        if ([string]$expected[$index] -cne [string]$actual[$index]) {
            $script:failures.Add("$name 第 $($index + 1) 项不同：英文[$($expected[$index])]，中文[$($actual[$index])]。")
            return
        }
    }
}

$patterns = [ordered]@{
    'VAR 声明' = '(?m)^\s*(VAR\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*[^\r\n]+)'
    'CONST 声明' = '(?m)^\s*(CONST\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*[^\r\n]+)'
    'knot/function' = '(?m)^\s*(===\s+(?:function\s+)?[A-Za-z_][A-Za-z0-9_]*)'
    'stitch' = '(?m)^\s*(=\s+[A-Za-z_][A-Za-z0-9_]*)'
    '赋值语句' = '(?m)^\s*(~\s*[^\r\n]+)'
    'divert 目标' = '->\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)'
    '命名选择或汇合点' = '(?m)^\s*[\*\-]+(?:\s+[\*\-]+)*\s*\(([A-Za-z_][A-Za-z0-9_]*)\)'
}

foreach ($entry in $patterns.GetEnumerator()) {
    Assert-Sequence $entry.Key `
        (Get-Captures $source $entry.Value) `
        (Get-Captures $translation $entry.Value)
}

$sourceChoiceShape = Get-Captures $source '(?m)^\s*((?:\*\s*)+)' | ForEach-Object { ($_ -replace '\s', '').Length }
$translationChoiceShape = Get-Captures $translation '(?m)^\s*((?:\*\s*)+)' | ForEach-Object { ($_ -replace '\s', '').Length }
Assert-Sequence '选择层级' $sourceChoiceShape $translationChoiceShape

$sourceGatherShape = Get-Captures $source '(?m)^\s*((?:-\s*)+)' | ForEach-Object { ($_ -replace '\s', '').Length }
$translationGatherShape = Get-Captures $translation '(?m)^\s*((?:-\s*)+)' | ForEach-Object { ($_ -replace '\s', '').Length }
Assert-Sequence '汇合层级' $sourceGatherShape $translationGatherShape

foreach ($token in @('->', '->->', '[', ']', '{', '}', '|', '<>')) {
    $sourceCount = [regex]::Matches($source, [regex]::Escape($token)).Count
    $translationCount = [regex]::Matches($translation, [regex]::Escape($token)).Count
    if ($sourceCount -ne $translationCount) {
        $failures.Add("标记 $token 数量不同：英文 $sourceCount，中文 $translationCount。")
    }
}

$englishResidue = @((Get-Content -Encoding UTF8 -LiteralPath $TranslationPath) |
    Where-Object {
        $_ -notmatch '^\s*(//|VAR\s|CONST\s|~\s|===?\s)' -and
        $_ -match '[A-Za-z]{2,}(?:[\s—''-]+[A-Za-z]{2,}){2,}'
    })
if ($RequireNoEnglish -and $englishResidue.Count -gt 0) {
    $failures.Add("仍有 $($englishResidue.Count) 行疑似未翻译英文。")
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

$inkAssembly = Join-Path $projectRoot 'Library\ScriptAssemblies\Ink-Libraries.dll'
if (!(Test-Path -LiteralPath $inkAssembly)) {
    throw "找不到固定 Ink 编译器程序集：$inkAssembly"
}

Add-Type -Path $inkAssembly
$compileErrors = [System.Collections.Generic.List[string]]::new()
$options = [Ink.Compiler+Options]::new()
$options.errorHandler = {
    param($message, $type)
    if ($type -eq [Ink.ErrorType]::Error) {
        $compileErrors.Add($message)
    }
}
$compiler = [Ink.Compiler]::new($translation, $options)
$story = $compiler.Compile()
if ($null -eq $story -or $compileErrors.Count -gt 0) {
    $compileErrors | ForEach-Object { Write-Error $_ }
    exit 1
}

$compiledJson = $story.ToJson()
$inkVersion = [regex]::Match($compiledJson, '"inkVersion":(\d+)').Groups[1].Value
[pscustomobject]@{
    Structure = '通过'
    InkCompile = '通过'
    InkVersion = $inkVersion
    CompiledJsonCharacters = $compiledJson.Length
    SuspectedEnglishLines = $englishResidue.Count
    SourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $SourcePath).Hash
    TranslationSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $TranslationPath).Hash
} | Format-List
