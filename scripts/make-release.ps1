# 커뮤니티 배포용 zip 을 만든다. 결과물: artifacts/SephPlanner-v{버전}.zip
#
# 포함하는 것: 우리가 만든 것(오버레이 exe, 플러그인 DLL, 안내문)과 재배포가 허용된
# Galmuri 폰트 라이선스 사본뿐이다. BepInEx 와 게임 파일은 절대 넣지 않는다 (docs/LEGAL.md).

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root "artifacts"
$stage = Join-Path $artifacts "stage"

$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

Write-Host "SephPlanner v$version 릴리스 빌드"

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

# 오버레이: .NET 런타임 없이 실행되도록 자체 포함 단일 파일로 낸다.
dotnet publish (Join-Path $root "src/SephPlanner.Overlay/SephPlanner.Overlay.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $stage "overlay")
if ($LASTEXITCODE -ne 0) { throw "오버레이 빌드 실패" }

# 플러그인: 게임 DLL 은 <Private>false</Private> 라 출력에 섞이지 않는다.
dotnet build (Join-Path $root "src/SephPlanner.Plugin/SephPlanner.Plugin.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "플러그인 빌드 실패" }

$zipRoot = Join-Path $stage "SephPlanner"
New-Item -ItemType Directory -Force (Join-Path $zipRoot "BepInEx-plugins") | Out-Null

Copy-Item (Join-Path $stage "overlay/SephPlanner.Overlay.exe") $zipRoot
$pluginOut = Join-Path $root "src/SephPlanner.Plugin/bin/Release"
Copy-Item (Join-Path $pluginOut "SephPlanner.Plugin.dll") (Join-Path $zipRoot "BepInEx-plugins")
Copy-Item (Join-Path $pluginOut "SephPlanner.Core.dll") (Join-Path $zipRoot "BepInEx-plugins")
Copy-Item (Join-Path $root "docs/INSTALL.md") (Join-Path $zipRoot "설치안내.md")
Copy-Item (Join-Path $root "LICENSE") (Join-Path $zipRoot "LICENSE.txt")
Copy-Item (Join-Path $root "src/SephPlanner.Overlay/Fonts/LICENSE.txt") (Join-Path $zipRoot "LICENSE-Galmuri.txt")

$zip = Join-Path $artifacts "SephPlanner-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $zipRoot -DestinationPath $zip

Remove-Item -Recurse -Force $stage
Write-Host "완성: $zip"
