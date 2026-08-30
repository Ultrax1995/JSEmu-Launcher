<#
.SYNOPSIS
    Builds the asset pack manifest (feed.json) that the launcher downloads and verifies.

.DESCRIPTION
    Walks a staging folder whose layout mirrors the game directory, computes a SHA-256
    for every file and writes the JSON the launcher expects.

    Staging layout example:

        staging\dxgi.dll                        -> game root
        staging\dinput8.dll                     -> game root
        staging\Locale\Locales.zip              -> <game>\Locale, extracted
        staging\Resources\Assets\Assets_000.pack -> <game>\Resources\Assets

    Files ending in .zip are marked for extraction unless listed in -NoExtract.
    Upload the staging folder to the CDN so its layout matches -BaseUrl.

.EXAMPLE
    .\New-AssetManifest.ps1 -StagingPath .\staging -BaseUrl https://assets.h1emukrakow.eu/Assets/ -OutFile .\feed.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $StagingPath,
    [Parameter(Mandatory)] [string] $BaseUrl,
    [string] $OutFile = "feed.json",
    [string] $Version = (Get-Date -Format "yyyy.MM.dd"),
    [string[]] $NoExtract = @()
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path $StagingPath).Path.TrimEnd('\')
if (-not (Test-Path $root)) { throw "Staging folder not found: $StagingPath" }

$assets = New-Object System.Collections.Generic.List[object]

foreach ($file in Get-ChildItem -Path $root -Recurse -File) {
    $relative = $file.FullName.Substring($root.Length + 1)
    $folder = Split-Path $relative -Parent

    # Resources\Assets is the launcher's default target, so leave path empty there
    # to stay compatible with manifests that predate the path field
    $path = if ($folder -ieq "Resources\Assets") { "" } else { $folder }

    $extract = $file.Extension -ieq ".zip" -and $NoExtract -notcontains $file.Name

    $urlPath = $relative.Replace('\', '/')
    $url = $BaseUrl.TrimEnd('/') + '/' + $urlPath

    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLower()

    $assets.Add([ordered]@{
        version  = $Version
        filename = $file.Name
        url      = $url
        hash     = "sha256:$hash"
        path     = $path
        extract  = $extract
    })

    "{0,-34} {1,12:N0} B  {2}" -f $relative, $file.Length, $(if ($extract) { "[extract]" } else { "" }) | Write-Host
}

if ($assets.Count -eq 0) { throw "No files found under $root" }

[ordered]@{ assets = $assets } |
    ConvertTo-Json -Depth 5 |
    Out-File -FilePath $OutFile -Encoding utf8

Write-Host ""
Write-Host "$($assets.Count) assets written to $OutFile"
