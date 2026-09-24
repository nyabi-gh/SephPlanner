$ErrorActionPreference = "Stop"
# 콘솔이 한글을 깨뜨리지 않게 한다. 실패 이유를 읽을 수 없으면 검사가 반쪽이 된다.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path $PSScriptRoot -Parent
$solution = Join-Path $root "SephPlanner.slnx"
$artifacts = Join-Path $root "artifacts"
$stage = Join-Path $artifacts "stage"
$zipRoot = Join-Path $stage "SephPlanner"
$pluginProject = Join-Path $root "src/SephPlanner.Plugin/SephPlanner.Plugin.csproj"
$testProject = Join-Path $root "tests/SephPlanner.Tests/SephPlanner.Tests.csproj"
$thirdParty = Join-Path $root "third-party"
$overlay = Join-Path $zipRoot "게임 폴더에 복사"

# 로더를 함께 담는다. 사용자가 직접 받다가 엉뚱한 파일을 고르는 일이 잦았고(x86/x64, Mono/IL2CPP,
# 6 베타), 그것이 설치 실패의 가장 흔한 이유였다. 라이선스는 third-party/NOTICE.txt 에
# 적혀 있다 - BepInEx 는 MIT, Unity Doorstop은 LGPL v2.1 이다.
$bepinexVersion = "5.4.23.5"
$bepinexAsset = if ($IsMacOS) { "BepInEx_macos_universal_$bepinexVersion.zip" } else { "BepInEx_win_x64_$bepinexVersion.zip" }
$bepinexUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$bepinexVersion/$bepinexAsset"
$bepinexSha256 = if ($IsMacOS) { "01c2ae782eb016dfd6c345a18dbd2dcafffb3d9d318449d6486689f426b4a323" } else { "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4" }
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1

function Invoke-DotNet([string[]]$Arguments, [string]$Failure) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}

# 받아 둔 것이든 방금 받은 것이든 해시를 매번 확인한다. 남의 코드를 사용자 게임 폴더에 넣어
# 주는 일이라 "예전에 맞았다"로는 부족하다.
function Get-BepInEx {
    $cache = Join-Path $artifacts "third-party"
    New-Item -ItemType Directory -Force $cache | Out-Null
    $path = Join-Path $cache $bepinexAsset
    if (-not (Test-Path $path)) {
        Write-Host "BepInEx $bepinexVersion 내려받는 중..."
        Invoke-WebRequest -Uri $bepinexUrl -OutFile $path -UseBasicParsing
    }
    $hash = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $bepinexSha256) {
        throw "BepInEx zip 의 SHA-256 이 다릅니다: $hash (기대 $bepinexSha256). $path 를 지우고 다시 실행하세요."
    }
    return $path
}

if (-not $version) { throw "배포 버전을 읽지 못했습니다." }
if (& git -C $root status --porcelain) { throw "작업 트리가 깨끗하지 않습니다. 커밋한 뒤 다시 실행하세요." }

# 버전은 네 곳에 손으로 적힌다. 어긋나도 빌드와 테스트는 통과하므로 여기서 붙잡는다.
# BepInEx 로그와 F10 덤프 첫 줄에 찍히는 것이 플러그인 쪽 값이라, 어긋나면 제보를 받고도
# 어느 빌드인지 되짚을 수 없다. STATUS 는 그 자체가 정본이라고 규정된 문서인데 갱신을
# 강제하는 것이 없어 두 판이 밀린 적이 있다.
$pluginSource = Get-Content (Join-Path $root "src/SephPlanner.Plugin/Plugin.cs") -Raw
if ($pluginSource -notmatch [regex]::Escape("[BepInPlugin(PluginGuid, ""SephPlanner"", ""$version"")]")) {
    throw "[BepInPlugin] 의 버전이 $version 이 아닙니다. Plugin.cs 를 맞추세요."
}
$status = Get-Content (Join-Path $root "docs/STATUS.md") -Raw
if ($status -notmatch [regex]::Escape("**이번 판: $version.**")) {
    throw "STATUS.md 의 '배포 상태' 가 이번 판을 $version 이라고 말하지 않습니다."
}
$changelog = Get-Content (Join-Path $root "docs/CHANGELOG.md")
if ($changelog -notcontains "## $version") {
    throw "CHANGELOG.md 에 '## $version' 절이 없습니다."
}

# 릴리스 본문은 CHANGELOG 의 해당 절을 그대로 뽑는다. 손으로 옮겨 적으면 두 벌이 되고,
# 한쪽만 고친 채 나가면 배포된 본문과 저장소가 갈라진다.
$start = $changelog.IndexOf("## $version") + 1
$end = $start
while ($end -lt $changelog.Count -and $changelog[$end] -notlike "## *") { $end++ }
# $start -eq $end 면 아래 범위가 거꾸로 돌아 엉뚱한 줄을 집는다. 먼저 붙잡는다.
if ($end -le $start) { throw "CHANGELOG.md 의 '## $version' 절이 비어 있습니다." }
$notes = ($changelog[$start..($end - 1)] -join "`n").Trim()
if (-not $notes) { throw "CHANGELOG.md 의 '## $version' 절이 비어 있습니다." }

# manifest 에 적는 커밋이 곧 태그가 가리켜야 할 커밋이다. 태그를 나중에 달면 zip 을 만든
# 커밋과 태그가 갈라져, 어느 소스에서 나온 zip 인지 되짚을 수 없다.
if ((& git -C $root tag --points-at HEAD) -notcontains "v$version") {
    throw "HEAD 에 v$version 태그가 없습니다. 태그를 먼저 달고 다시 실행하세요."
}

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $overlay | Out-Null

try {
    Invoke-DotNet @("restore", $solution, "--locked-mode") "복원 실패"
    Invoke-DotNet @("format", $solution, "--verify-no-changes", "--no-restore") "포맷 검사 실패"
    Invoke-DotNet @("test", $testProject, "-c", "Release", "--no-restore") "테스트 실패"
    Invoke-DotNet @("build", $pluginProject, "-c", "Release", "--no-restore", "-p:DeployToGame=false") "플러그인 빌드 실패"

    # 게임 폴더에 그대로 부을 한 벌을 먼저 짓는다. 받는 사람이 할 일은 한 폴더의 내용을 옮기는 것뿐이다.
    if ($IsMacOS) {
        & bash (Join-Path $PSScriptRoot "build-macos-loader.sh")
        if ($LASTEXITCODE -ne 0) { throw "macOS 로더 빌드 실패" }
        Copy-Item (Join-Path $artifacts "macos-loader/*") $overlay -Recurse -Force
    }
    else {
        Expand-Archive (Get-BepInEx) $overlay
    }
    $plugins = Join-Path $overlay "BepInEx/plugins"
    New-Item -ItemType Directory -Force $plugins | Out-Null

    $pluginOut = Join-Path $root "src/SephPlanner.Plugin/bin/Release"
    Copy-Item (Join-Path $pluginOut "SephPlanner.Plugin.dll") $plugins
    Copy-Item (Join-Path $pluginOut "SephPlanner.Core.dll") $plugins
    $installGuide = if ($IsMacOS) { "docs/INSTALL-macos.txt" } else { "docs/INSTALL.txt" }
    Copy-Item (Join-Path $root $installGuide) (Join-Path $zipRoot "설치안내.txt")
    Copy-Item (Join-Path $root "LICENSE") (Join-Path $zipRoot "LICENSE.txt")

    # 남의 것을 담았으므로 그쪽 라이선스 원문과 소스 위치도 함께 나간다.
    $notices = Join-Path $zipRoot "제3자-라이선스"
    New-Item -ItemType Directory -Force $notices | Out-Null
    Copy-Item (Join-Path $thirdParty "*.txt") $notices
    if ($IsMacOS) {
        Copy-Item (Join-Path $PSScriptRoot "build-macos-loader.sh") (Join-Path $notices "build-macos-loader.sh")
        Copy-Item (Join-Path $PSScriptRoot "patches/macos-plthook.patch") (Join-Path $notices "macos-plthook.patch")
    }

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
        bundled = [ordered]@{
            bepinex = [ordered]@{
                version = [string]$bepinexVersion
                asset = [string]$bepinexAsset
                sha256 = [string]$bepinexSha256
                sourceCommit = if ($IsMacOS) { "f4c1b1103a32884b7440d9681c60cc5d619f284f" } else { $null }
            }
            doorstopSourceCommit = if ($IsMacOS) { "8e66ca0b189d4c443ba9e7c4f5aac8105582a91c" } else { $null }
            plthookSourceCommit = if ($IsMacOS) { "24c69df003310fb91f9c2950b5a659ff70e9dfb9" } else { $null }
        }
        gameAssembly = [ordered]@{
            fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($gameAssembly).FileVersion
            sha256 = (Get-FileHash $gameAssembly -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        files = @($files)
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $zipRoot "manifest.json") -Encoding UTF8

    $assetName = if ($IsMacOS) { "SephPlanner-macos-v$version.zip" } else { "SephPlanner-v$version.zip" }
    $zip = Join-Path $artifacts $assetName
    if (Test-Path $zip) { Remove-Item $zip }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $zipRoot, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true,
        [System.Text.Encoding]::UTF8)

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $required = @(
            "SephPlanner/게임 폴더에 복사/BepInEx/plugins/SephPlanner.Plugin.dll",
            "SephPlanner/게임 폴더에 복사/BepInEx/plugins/SephPlanner.Core.dll",
            "SephPlanner/게임 폴더에 복사/BepInEx/core/BepInEx.dll",
            "SephPlanner/설치안내.txt",
            "SephPlanner/LICENSE.txt",
            "SephPlanner/제3자-라이선스/NOTICE.txt",
            "SephPlanner/제3자-라이선스/BepInEx-LICENSE.txt",
            "SephPlanner/제3자-라이선스/Doorstop-LICENSE.txt",
            "SephPlanner/manifest.json")
        if ($IsMacOS) {
            $required += @(
                "SephPlanner/게임 폴더에 복사/run_bepinex.sh",
                "SephPlanner/게임 폴더에 복사/libdoorstop.dylib",
                "SephPlanner/제3자-라이선스/Plthook-LICENSE.txt",
                "SephPlanner/제3자-라이선스/build-macos-loader.sh",
                "SephPlanner/제3자-라이선스/macos-plthook.patch")
        }
        else {
            $required += @(
                "SephPlanner/게임 폴더에 복사/winhttp.dll",
                "SephPlanner/게임 폴더에 복사/doorstop_config.ini")
        }
        foreach ($entry in $required) {
            if (-not ($archive.Entries | Where-Object { $_.FullName.Replace("\", "/") -eq $entry })) {
                throw "릴리스 파일 누락: $entry"
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $notesPath = Join-Path $artifacts "release-notes-v$version.md"
    Set-Content -Path $notesPath -Value $notes -Encoding utf8NoBOM

    Write-Host "완성: $zip"
    Write-Host "릴리스 본문: $notesPath"
    # 공개는 두 ZIP 이 한 드래프트에 모인 뒤에만 한다. macOS ZIP 이 빠진 채 Latest 가 되면 Mac 업데이트가 404 다.
    Write-Host "드래프트가 아직 없으면 먼저 만든다:"
    Write-Host "  gh release create v$version `"$zip`" -R nyabi-gh/SephPlanner --draft --title `"SephPlanner $version`" --notes-file `"$notesPath`""
    Write-Host "다른 운영체제에서 드래프트를 이미 만들었으면 ZIP 만 더한다:"
    Write-Host "  gh release upload v$version `"$zip`" -R nyabi-gh/SephPlanner"
    Write-Host "두 ZIP 이 모두 올라간 뒤 공개한다(없으면 거절한다):"
    Write-Host "  scripts/publish-release.ps1"
}
finally {
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
}
