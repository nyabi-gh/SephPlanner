$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path $PSScriptRoot -Parent
$repository = "nyabi-gh/SephPlanner"
$version = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
$tag = "v$version"

# 이름은 make-release.ps1 과 UpdateClient 의 에셋 이름과 같아야 한다.
$required = @("SephPlanner-v$version.zip", "SephPlanner-macos-v$version.zip")

$release = & gh release view $tag -R $repository --json isDraft,assets | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw "릴리스 $tag 를 읽지 못했습니다." }
if (-not $release.isDraft) { throw "$tag 는 드래프트가 아닙니다. 이미 공개됐는지 확인하세요." }

$names = @($release.assets | ForEach-Object { $_.name })
$missing = @($required | Where-Object { $names -notcontains $_ })
if ($missing.Count -gt 0) { throw "공개하지 않습니다. 빠진 ZIP: $($missing -join ', ')" }

& gh release edit $tag -R $repository --draft=false
if ($LASTEXITCODE -ne 0) { throw "$tag 를 공개하지 못했습니다." }
Write-Host "$tag 를 공개했습니다 - $($required -join ', ')"
