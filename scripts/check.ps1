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

# CI 가 하는 것과 같은 검사에, CI 가 할 수 없는 것 하나를 더한다.
Invoke-DotNet @("restore", $solution, "--locked-mode") "복원 실패"
Invoke-DotNet @("format", $solution, "--verify-no-changes", "--no-restore") "포맷 검사 실패"
Invoke-DotNet @("test", $testProject, "-c", "Release", "--no-restore") "테스트 실패"
Invoke-DotNet @("build", (Join-Path $root "src/SephPlanner.DataTool/SephPlanner.DataTool.csproj"), "-c", "Release", "--no-restore") "진단 도구 빌드 실패"

# 여기가 CI 의 사각지대다. 플러그인은 게임 어셈블리를 참조하는데 그것을 CI 에 둘 수 없어서
# (docs/LEGAL.md - 게임 저작물 미배포), 플러그인만 깨지는 변경은 CI 를 초록으로 통과한다.
# 밀기 전에 여기서 한 번 세운다.
Invoke-DotNet @("build", $pluginProject, "-c", "Release", "--no-restore", "-p:DeployToGame=false") "플러그인 빌드 실패"

Write-Host ""
Write-Host "검사 통과 - 포맷, 테스트, 그리고 CI 가 못 보는 플러그인 빌드까지."
