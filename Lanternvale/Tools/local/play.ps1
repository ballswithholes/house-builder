# Build and launch Lanternvale on YOUR Windows computer with your Unity Hub install and license.
#
#   powershell -ExecutionPolicy Bypass -File Lanternvale\Tools\local\play.ps1            build and launch the game
#   powershell -ExecutionPolicy Bypass -File Lanternvale\Tools\local\play.ps1 -Editor    open the project in the Unity editor
#   powershell -ExecutionPolicy Bypass -File Lanternvale\Tools\local\play.ps1 -Tour      launch with the autopilot tour
#
# Finds the newest Unity 6 (or 2022.3) editor installed by Unity Hub, creates a project (Tools\.cache\local\
# LanternvaleProject; an existing one is reused and switched to the built-in render pipeline by the build step),
# copies Assets\Lanternvale into it, builds a Windows player and starts it.
# Override the editor with $env:UNITY_EDITOR = "C:\...\Unity.exe".
param([switch]$Editor, [switch]$Tour)
$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Work = Join-Path $Root "Tools\.cache\local"
$Project = if ($env:LV_PROJECT) { $env:LV_PROJECT } else { Join-Path $Work "LanternvaleProject" }
function Log($m) { Write-Host "[lanternvale] $m" -ForegroundColor Yellow }
function Die($m) { Write-Host "[lanternvale] $m" -ForegroundColor Red; exit 1 }

function Find-Editor {
    if ($env:UNITY_EDITOR) { return $env:UNITY_EDITOR }
    $roots = @("$env:ProgramFiles\Unity\Hub\Editor", "${env:ProgramFiles(x86)}\Unity\Hub\Editor", "$env:LOCALAPPDATA\Unity\Hub\Editor")
    $found = foreach ($r in $roots) {
        if (Test-Path $r) {
            Get-ChildItem $r -Directory | ForEach-Object {
                $exe = Join-Path $_.FullName "Editor\Unity.exe"
                if (Test-Path $exe) {
                    $prio = if ($_.Name -like "6000.*") { 2 } elseif ($_.Name -like "2022.3.*") { 1 } else { 0 }
                    [pscustomobject]@{ Exe = $exe; Prio = $prio; Ver = $_.Name }
                }
            }
        }
    }
    $best = $found | Sort-Object -Property @{Expression = "Prio"; Descending = $true }, @{Expression = { [version]($_.Ver -replace '[a-z].*$', '') }; Descending = $true } | Select-Object -First 1
    if ($best) { return $best.Exe }
    return $null
}

$UnityExe = Find-Editor
if (-not $UnityExe) { Die "No Unity editor found. Install Unity 6 with Unity Hub (or set `$env:UNITY_EDITOR)." }
Log "Unity editor: $UnityExe"
New-Item -ItemType Directory -Force -Path $Work | Out-Null

if (-not (Test-Path (Join-Path $Project "ProjectSettings\ProjectVersion.txt"))) {
    Log "Creating the Unity project (one time)"
    $createArgs = @("-batchmode", "-quit", "-createProject", "`"$Project`"", "-logFile", "`"$(Join-Path $Work 'create.log')`"")
    $p = Start-Process -FilePath $UnityExe -ArgumentList $createArgs -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) { Die "Project creation failed, see $Work\create.log" }
}

Log "Copying Assets\Lanternvale into the project"
$dest = Join-Path $Project "Assets\Lanternvale"
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item (Join-Path $Root "Assets\Lanternvale") $dest -Recurse

if ($Editor) {
    Log "Opening the Unity editor - the game scene opens by itself, then press Play"
    Start-Process -FilePath $UnityExe -ArgumentList @("-projectPath", "`"$Project`"", "-executeMethod", "Lanternvale.EditorTools.LanternvaleMenu.OpenGameScene")
    exit 0
}

$Player = Join-Path $Project "Build\Windows\Lanternvale.exe"
Log "Building the game (the first build imports all the art and takes a while)"
$buildLog = Join-Path $Work "build.log"
$p = Start-Process -FilePath $UnityExe -Wait -PassThru -NoNewWindow -ArgumentList @(
    "-batchmode", "-quit", "-projectPath", "`"$Project`"",
    "-executeMethod", "Lanternvale.EditorTools.LanternvaleBuild.BuildWindowsPlayer",
    "-buildPath", "`"$Player`"", "-logFile", "`"$buildLog`"")
if ($p.ExitCode -ne 0 -or -not (Test-Path $Player)) {
    Select-String -Path $buildLog -Pattern "error|\[Lanternvale\]" | Select-Object -Last 30 | ForEach-Object { $_.Line }
    Die "Build failed, full log: $buildLog"
}

$playerArgs = @()
if ($Tour) { $playerArgs = @("-lv-autopilot", "-lv-shots", "`"$(Join-Path $Work 'shots')`""); Log "Autopilot tour; screenshots -> $Work\shots" }
Log "Launching Lanternvale"
if ($playerArgs.Count -gt 0) { Start-Process -FilePath $Player -ArgumentList $playerArgs } else { Start-Process -FilePath $Player }
