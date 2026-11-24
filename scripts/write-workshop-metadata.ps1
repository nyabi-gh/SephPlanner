#Requires -Version 7
# 창작마당 패키지의 metadata.json 을 짓는다. SephPlanner.Plugin.csproj 의 PackWorkshop 이 부른다.
#
# 게임은 올릴 때 info.description 의 default 를 Steam 글 설명으로 쓰고, 게임 안 모드 창에는 게임 언어의 항목을 쓰며 없으면
# default 로 물러선다(1.1.4 WorkshopUploadPreparer·WorkshopLocalizedText.Resolve). 그래서 default 에 BBCode 설명글을 넣고,
# 우리가 쓰지 않은 게임 언어에는 영어 짧은 설명을 채워 모드 창에 BBCode 가 드러나지 않게 한다.
param(
    [Parameter(Mandatory)] [string]$Source,
    [Parameter(Mandatory)] [string]$Description,
    [Parameter(Mandatory)] [string]$Destination
)
$ErrorActionPreference = "Stop"

# 1.1.4 의 StreamingAssets/Localization 에 있는 언어다.
$gameLanguages = "de-DE", "en-US", "es-ES", "fr-FR", "it-IT", "ja-JP", "ko-KR", "pl-PL", "pt-BR", "ru-RU", "sv-SE", "th-TH",
    "tr-TR", "zh-CN", "zh-TW"
# 게임이 이보다 길면 올리기를 거절한다(WorkshopUploadPreparer).
$maximumLength = 7999

$metadata = Get-Content $Source -Raw | ConvertFrom-Json -AsHashtable
$descriptions = $metadata.info.description
foreach ($language in $gameLanguages) {
    if (-not $descriptions.Contains($language)) { $descriptions[$language] = $descriptions["en-US"] }
}
$text = (Get-Content $Description -Raw).Replace("`r`n", "`n").Trim()
if ($text.Length -gt $maximumLength) {
    throw "창작마당 설명글이 $maximumLength 자를 넘습니다($($text.Length)자): $Description"
}
$descriptions["default"] = $text

New-Item -ItemType Directory -Force (Split-Path $Destination -Parent) | Out-Null
$json = $metadata | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText($Destination, $json + "`n", [System.Text.UTF8Encoding]::new($false))
