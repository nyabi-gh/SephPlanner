#Requires -Version 7
$ErrorActionPreference = "Stop"
# 콘솔이 한글을 깨뜨리지 않게 한다. 실패 이유를 읽을 수 없으면 검사가 반쪽이 된다.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root "SephPlanner.slnx"
$pluginProject = Join-Path $root "src/SephPlanner.Plugin/SephPlanner.Plugin.csproj"
$testProject = Join-Path $root "tests/SephPlanner.Tests/SephPlanner.Tests.csproj"

function Invoke-DotNet([string[]]$Arguments, [string]$Failure) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}

# 버전은 네 곳에 손으로 적힌다. 어긋나도 빌드와 테스트는 통과하므로 여기서 붙잡는다.
# BepInEx 로그와 F10 덤프 첫 줄에 찍히는 것이 플러그인 쪽 값이라, 어긋나면 제보를 받고도
# 어느 빌드인지 되짚을 수 없다. STATUS 는 그 자체가 정본이라고 규정된 문서인데 갱신을
# 강제하는 것이 없어 두 판이 밀린 적이 있다.
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "배포 버전을 읽지 못했습니다." }
$pluginSource = Get-Content (Join-Path $root "src/SephPlanner.Plugin/Plugin.cs") -Raw
if ($pluginSource -notmatch [regex]::Escape("[BepInPlugin(PluginGuid, ""SephPlanner"", ""$version"")]")) {
    throw "[BepInPlugin] 의 버전이 $version 이 아닙니다. Plugin.cs 를 맞추세요."
}
$status = Get-Content (Join-Path $root "docs/STATUS.md") -Raw
if ($status -notmatch [regex]::Escape("**이번 판: $version.**")) {
    throw "STATUS.md 의 '배포 상태' 가 이번 판을 $version 이라고 말하지 않습니다."
}
if ((Get-Content (Join-Path $root "docs/CHANGELOG.md")) -notcontains "## $version") {
    throw "CHANGELOG.md 에 '## $version' 절이 없습니다."
}

# CI 가 하는 것과 같은 검사에, CI 가 할 수 없는 것 하나를 더한다. 경고는 실패로 센다 - 지금 0 이고,
# 쌓인 뒤에는 켤 수 없다.
Invoke-DotNet @("restore", $solution, "--locked-mode") "복원 실패"
Invoke-DotNet @("format", $solution, "--verify-no-changes", "--no-restore") "포맷 검사 실패"
Invoke-DotNet @("test", $testProject, "-c", "Release", "--no-restore", "-p:TreatWarningsAsErrors=true") "테스트 실패"
Invoke-DotNet @("build", (Join-Path $root "src/SephPlanner.DataTool/SephPlanner.DataTool.csproj"), "-c", "Release", "--no-restore", "-p:TreatWarningsAsErrors=true") "진단 도구 빌드 실패"

# 여기가 CI 의 사각지대다. 플러그인은 게임 어셈블리를 참조하는데 그것을 CI 에 둘 수 없어서
# (docs/LEGAL.md - 게임 저작물 미배포), 플러그인만 깨지는 변경은 CI 를 초록으로 통과한다.
# 밀기 전에 여기서 한 번 세운다.
Invoke-DotNet @("build", $pluginProject, "-c", "Release", "--no-restore", "-p:DeployToGame=false", "-p:TreatWarningsAsErrors=true") "플러그인 빌드 실패"

Write-Host ""
Write-Host "검사 통과 - 포맷, 테스트, 그리고 CI 가 못 보는 플러그인 빌드까지."
