$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
$bundle = Join-Path $projectRoot ("artifacts/diagnostics-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Path $bundle -ErrorAction Stop | Out-Null
& dotnet publish (Join-Path $projectRoot "src/SephPlanner.Diagnostics/SephPlanner.Diagnostics.csproj") -c Release --no-restore -p:UseAppHost=false -o (Join-Path $bundle "publish")
if ($LASTEXITCODE -ne 0) { throw "진단 서버 게시 빌드 실패" }
$templates = Join-Path $projectRoot "deploy/diagnostics"
if (-not (Test-Path -LiteralPath $templates)) {
    throw "배포 템플릿이 없다: $templates. 서버 주소가 적혀 있어 저장소에 넣지 않으므로 배포 담당자 사본에서 가져온다."
}
foreach ($name in @("Dockerfile", ".dockerignore", "compose.yaml", "sephplanner.caddy", "sephplanner.service", "README.md")) {
    Copy-Item -LiteralPath (Join-Path $templates $name) -Destination $bundle
}
$archive = $bundle + ".zip"
Compress-Archive -Path (Join-Path $bundle "*") -DestinationPath $archive
Write-Host "진단 서버 배포 준비 파일: $archive"
Get-FileHash -LiteralPath $archive -Algorithm SHA256
