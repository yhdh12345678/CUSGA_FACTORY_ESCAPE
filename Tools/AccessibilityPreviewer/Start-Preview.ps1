$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectFile = Join-Path $PSScriptRoot 'AccessibilityPreviewer.csproj'
$previewerPath = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\AccessibilityPreviewer.dll'
$systemDotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

$sourceTime = (Get-ChildItem -LiteralPath $PSScriptRoot -File |
    Where-Object { $_.Extension -in '.cs', '.csproj' } |
    Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum
$needsBuild = -not (Test-Path -LiteralPath $previewerPath) -or
    (Get-Item -LiteralPath $previewerPath).LastWriteTimeUtc -lt $sourceTime
if ($needsBuild) {
    & $systemDotnet build $projectFile --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw '无障碍流程预览器构建失败。'
    }
}

$previewDirectory = Join-Path $projectRoot 'Temp\AccessibilityPreview'
New-Item -ItemType Directory -Path $previewDirectory -Force | Out-Null

$clientPidPath = Join-Path $previewDirectory 'client.pid'
$clientRunning = $false
if (Test-Path -LiteralPath $clientPidPath) {
    $clientPid = Get-Content -LiteralPath $clientPidPath -ErrorAction SilentlyContinue
    $clientRunning = $null -ne (Get-Process -Id $clientPid -ErrorAction SilentlyContinue)
}
if (-not $clientRunning) {
    Start-Process -FilePath $systemDotnet -ArgumentList @($previewerPath, '--project-root', $projectRoot)
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $clientPidPath)) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path -LiteralPath $clientPidPath)) {
        throw '无障碍流程预览器未能启动。'
    }
}

$runningEditor = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.Contains($projectRoot, [StringComparison]::OrdinalIgnoreCase) } |
    Select-Object -First 1
if (-not $runningEditor) {
    $versionLine = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') |
        Where-Object { $_ -like 'm_EditorVersion:*' } |
        Select-Object -First 1
    $version = ($versionLine -split ':', 2)[1].Trim()

    $unityPath = $null
    $installRootFile = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
    if (Test-Path -LiteralPath $installRootFile) {
        $installRoot = Get-Content -LiteralPath $installRootFile -Raw -Encoding utf8 | ConvertFrom-Json
        $candidate = Join-Path $installRoot "$version\Editor\Unity.exe"
        if (Test-Path -LiteralPath $candidate) {
            $unityPath = $candidate
        }
    }

    if (-not $unityPath) {
        $unityCommand = Get-Command Unity.exe -ErrorAction SilentlyContinue
        if ($unityCommand) {
            $unityPath = $unityCommand.Source
        }
    }

    if (-not $unityPath) {
        throw "找不到项目所需的 Unity $version。"
    }

    Start-Process -FilePath $unityPath -ArgumentList @('-projectPath', $projectRoot)
}
