$ErrorActionPreference = "Stop"
# 콘솔이 한글을 깨뜨리지 않게 한다. 실패 이유를 읽을 수 없으면 검사가 반쪽이 된다.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src/SephPlanner.Plugin/SephPlanner.Plugin.csproj"

dotnet build $project -c Release -p:DeployToGame=true
if ($LASTEXITCODE -ne 0) { throw "플러그인 빌드 또는 배치 실패" }

# 배치는 조건이 안 맞으면 조용히 건너뛸 수 있는 일이었고 그때도 exit 0 이었다. 새 DLL 을 넣은
# 줄 알고 옛 빌드로 시험하는 일이 없도록, 방금 빌드한 것과 같은 파일이 들어갔는지 확인한다.
$properties = (dotnet msbuild $project -getProperty:SephiriaDir -getProperty:TargetDir -p:Configuration=Release -nologo | ConvertFrom-Json).Properties
if ($LASTEXITCODE -ne 0) { throw "배치 경로를 읽지 못했습니다" }
$plugins = Join-Path $properties.SephiriaDir "BepInEx/plugins"

foreach ($name in @("SephPlanner.Plugin.dll", "SephPlanner.Core.dll")) {
    $target = Join-Path $plugins $name
    if (-not (Test-Path $target)) { throw "$name 이 배치되지 않았습니다: $target" }
    $built = (Get-FileHash (Join-Path $properties.TargetDir $name)).Hash
    if ((Get-FileHash $target).Hash -ne $built) {
        throw "$name 이 방금 빌드한 것과 다릅니다. 게임이 켜져 있어 덮어쓰지 못했을 수 있습니다: $target"
    }
}

Write-Host ""
Write-Host "배치 완료 - $plugins"
