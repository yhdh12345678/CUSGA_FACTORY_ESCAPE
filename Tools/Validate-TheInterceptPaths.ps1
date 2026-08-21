param(
    [string]$StoryPath,
    [int]$MaximumAttempts = 20000,
    [int]$MaximumChoicesPerAttempt = 500,
    [int]$RandomSeed = 1942
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($StoryPath)) {
    $StoryPath = Join-Path $projectRoot 'Assets\GameContent\TheIntercept\Translation\TheIntercept.zh-CN.ink.txt'
}

$inkAssembly = Join-Path $projectRoot 'Library\ScriptAssemblies\Ink-Libraries.dll'
if (!(Test-Path -LiteralPath $inkAssembly)) {
    throw "找不到固定 Ink 运行时程序集：$inkAssembly"
}

Add-Type -Path $inkAssembly
$source = Get-Content -Raw -Encoding UTF8 -LiteralPath $StoryPath
$compileErrors = [System.Collections.Generic.List[string]]::new()
$options = [Ink.Compiler+Options]::new()
$options.errorHandler = {
    param($message, $type)
    if ($type -eq [Ink.ErrorType]::Error) {
        $compileErrors.Add($message)
    }
}
$compiledStory = ([Ink.Compiler]::new($source, $options)).Compile()
if ($null -eq $compiledStory -or $compileErrors.Count -gt 0) {
    $compileErrors | ForEach-Object { Write-Error $_ }
    exit 1
}
$compiledJson = $compiledStory.ToJson()

$expectedTitles = @(
    '结局：亡命天涯',
    '结局：重获自由',
    '结局：骗局败露',
    '结局：等待裁决'
)
$endings = @{}
$unexpectedEndings = [System.Collections.Generic.HashSet[string]]::new()
$random = [System.Random]::new($RandomSeed)
$story = [Ink.Runtime.Story]::new($compiledJson)
$initialState = $story.state.ToJson()
$attemptsRun = 0
$choicesTaken = 0

for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
    $attemptsRun = $attempt
    $story.state.LoadJson($initialState)
    $currentTitle = ''
    $path = [System.Collections.Generic.List[string]]::new()

    for ($step = 0; $step -le $MaximumChoicesPerAttempt; $step++) {
        while ($story.canContinue) {
            $null = $story.Continue()
            foreach ($tag in $story.currentTags) {
                if ($tag.StartsWith('title:', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $currentTitle = $tag.Substring(6).Trim()
                }
            }
        }

        if ($story.currentChoices.Count -eq 0) {
            if ($currentTitle -in $expectedTitles) {
                if (!$endings.ContainsKey($currentTitle)) {
                    $endings[$currentTitle] = [pscustomobject]@{
                        Attempt = $attempt
                        ExamplePath = $path.ToArray()
                    }
                }
            } else {
                $null = $unexpectedEndings.Add($currentTitle)
            }
            break
        }

        if ($step -eq $MaximumChoicesPerAttempt) {
            throw "第 $attempt 次路径超过 $MaximumChoicesPerAttempt 个选择，可能存在未收敛循环。"
        }

        $choiceIndex = $random.Next($story.currentChoices.Count)
        $path.Add($story.currentChoices[$choiceIndex].text.Trim())
        $story.ChooseChoiceIndex($choiceIndex)
        $choicesTaken++
    }

    if ($endings.Count -eq $expectedTitles.Count) {
        break
    }
}

$missingTitles = @($expectedTitles | Where-Object { !$endings.ContainsKey($_) })
if ($missingTitles.Count -gt 0 -or $unexpectedEndings.Count -gt 0) {
    if ($missingTitles.Count -gt 0) {
        Write-Error "未到达预期结局：$($missingTitles -join '、')"
    }
    if ($unexpectedEndings.Count -gt 0) {
        Write-Error "发现无结局标题或意外终点：$($unexpectedEndings -join '、')"
    }
    exit 1
}

[pscustomobject]@{
    RandomSeed = $RandomSeed
    AttemptsRun = $attemptsRun
    ChoicesTaken = $choicesTaken
    ReachableEndingTypes = $endings.Count
} | Format-List

foreach ($title in $expectedTitles) {
    $ending = $endings[$title]
    [pscustomobject]@{
        Ending = $title
        FoundOnAttempt = $ending.Attempt
        ExampleChoices = $ending.ExamplePath -join ' → '
    } | Format-List
}
