$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src/SephPlanner.Plugin/SephPlanner.Plugin.csproj"

dotnet build $project -c Release -p:DeployToGame=true
if ($LASTEXITCODE -ne 0) { throw "플러그인 빌드 또는 배치 실패" }
