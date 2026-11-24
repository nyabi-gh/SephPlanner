#Requires -Version 7
$ErrorActionPreference = "Stop"
# 콘솔이 한글을 깨뜨리지 않게 한다. 실패 이유를 읽을 수 없으면 검사가 반쪽이 된다.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root "artifacts"
$pluginProject = Join-Path $root "src/SephPlanner.Plugin/SephPlanner.Plugin.csproj"
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

if (-not $version) { throw "배포 버전을 읽지 못했습니다." }
if (& git -C $root status --porcelain) { throw "작업 트리가 깨끗하지 않습니다. 커밋한 뒤 다시 실행하세요." }

# 올린 DLL 이 어느 소스에서 나왔는지 되짚으려면 태그가 지금 커밋을 가리켜야 한다.
if ((& git -C $root tag --points-at HEAD) -notcontains "v$version") {
    throw "태그 v$version 이 지금 커밋을 가리키지 않습니다. 커밋에 태그를 단 뒤 다시 실행하세요."
}

# 창작마당 변경 사항 글은 CHANGELOG 의 해당 절을 그대로 뽑는다. 손으로 옮겨 적으면 두 벌이 되고,
# 한쪽만 고친 채 나가면 올린 글과 저장소가 갈라진다.
# 제목 줄이 정확히 같아야 찾는다. 못 찾으면 IndexOf 가 -1 이라 문서 첫머리를 본문으로 집는다.
$changelog = Get-Content (Join-Path $root "docs/CHANGELOG.md")
$heading = $changelog.IndexOf("## $version")
if ($heading -lt 0) { throw "CHANGELOG.md 에 '## $version' 절이 없습니다." }
$start = $heading + 1
$end = $start
while ($end -lt $changelog.Count -and $changelog[$end] -notlike "## *") { $end++ }
# $start -eq $end 면 아래 범위가 거꾸로 돌아 엉뚱한 줄을 집는다. 먼저 붙잡는다.
if ($end -le $start) { throw "CHANGELOG.md 의 '## $version' 절이 비어 있습니다." }
$notes = ($changelog[$start..($end - 1)] -join "`n").Trim()
if (-not $notes) { throw "CHANGELOG.md 의 '## $version' 절이 비어 있습니다." }

& (Join-Path $PSScriptRoot "check.ps1")
if ($LASTEXITCODE -ne 0) { throw "검사 실패" }

# 게임의 창작마당 창은 WorkshopLocal 의 모드를 올리므로 거기까지 놓는다. 올리는 것은 게임 안에서 한다.
& dotnet build $pluginProject -c Release --no-restore -p:TreatWarningsAsErrors=true -p:PackWorkshop=true -p:DeployToWorkshopLocal=true
if ($LASTEXITCODE -ne 0) { throw "창작마당 패키지 빌드 실패" }

New-Item -ItemType Directory -Force $artifacts | Out-Null
$notesPath = Join-Path $artifacts "workshop-notes-v$version.md"
[System.IO.File]::WriteAllText($notesPath, $notes + "`n", [System.Text.UTF8Encoding]::new($false))

Write-Host ""
Write-Host "창작마당 패키지를 WorkshopLocal/sephplanner 에 놓았습니다. 게임의 창작마당 창에서 올립니다."
Write-Host "변경 사항 글: $notesPath"
