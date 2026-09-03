$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root "SephPlanner.slnx"
$artifacts = Join-Path $root "artifacts"
$stage = Join-Path $artifacts "stage"
$zipRoot = Join-Path $stage "SephPlanner"
$pluginProject = Join-Path $root "src/SephPlanner.Plugin/SephPlanner.Plugin.csproj"
$testProject = Join-Path $root "tests/SephPlanner.Tests/SephPlanner.Tests.csproj"
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

function Invoke-DotNet([string[]]$Arguments, [string]$Failure) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}

if (-not $version) { throw "배포 버전을 읽지 못했습니다." }
if (& git -C $root status --porcelain) { throw "작업 트리가 깨끗하지 않습니다. 커밋한 뒤 다시 실행하세요." }

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force (Join-Path $zipRoot "BepInEx-plugins") | Out-Null

try {
    Invoke-DotNet @("restore", $solution, "--locked-mode") "복원 실패"
    Invoke-DotNet @("format", $solution, "--verify-no-changes", "--no-restore") "포맷 검사 실패"
    Invoke-DotNet @("test", $testProject, "-c", "Release", "--no-restore") "테스트 실패"
    Invoke-DotNet @("build", $pluginProject, "-c", "Release", "--no-restore", "-p:DeployToGame=false") "플러그인 빌드 실패"

    $pluginOut = Join-Path $root "src/SephPlanner.Plugin/bin/Release"
    Copy-Item (Join-Path $pluginOut "SephPlanner.Plugin.dll") (Join-Path $zipRoot "BepInEx-plugins")
    Copy-Item (Join-Path $pluginOut "SephPlanner.Core.dll") (Join-Path $zipRoot "BepInEx-plugins")
    Copy-Item (Join-Path $root "docs/INSTALL.md") (Join-Path $zipRoot "설치안내.txt")
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $zipRoot "LICENSE.txt")

    $commit = (& git -C $root rev-parse HEAD).Trim()
    $managedDir = (& dotnet msbuild $pluginProject -nologo -getProperty:SephiriaManagedDir).Trim()
    if ($LASTEXITCODE -ne 0) { throw "게임 어셈블리 경로 확인 실패" }
    $gameAssembly = Join-Path $managedDir "Assembly-CSharp.dll"
    if (-not (Test-Path $gameAssembly)) { throw "게임 어셈블리를 찾지 못했습니다: $gameAssembly" }
    $files = Get-ChildItem $zipRoot -Recurse -File | ForEach-Object {
        [ordered]@{
            path = $_.FullName.Substring($zipRoot.Length + 1).Replace("\", "/")
            sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    [ordered]@{
        version = [string]$version
        commit = $commit
        createdUtc = [DateTime]::UtcNow.ToString("o")
        gameAssembly = [ordered]@{
            fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($gameAssembly).FileVersion
            sha256 = (Get-FileHash $gameAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        files = @($files)
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $zipRoot "manifest.json") -Encoding UTF8

    $zip = Join-Path $artifacts "SephPlanner-v$version.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $zipRoot, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true,
        [System.Text.Encoding]::UTF8)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $required = @(
            "SephPlanner/BepInEx-plugins/SephPlanner.Plugin.dll",
            "SephPlanner/BepInEx-plugins/SephPlanner.Core.dll",
            "SephPlanner/설치안내.txt",
            "SephPlanner/LICENSE.txt",
            "SephPlanner/manifest.json")
        foreach ($entry in $required) {
            if (-not ($archive.Entries | Where-Object { $_.FullName.Replace("\", "/") -eq $entry })) {
                throw "릴리스 파일 누락: $entry"
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Host "완성: $zip"
}
finally {
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
}
