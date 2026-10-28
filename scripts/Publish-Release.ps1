[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^[0-9a-f]{40}$')] [string] $Commit,
    [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')] [string] $Repository
)
$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_TOKEN) { throw 'The release workflow must provide GITHUB_TOKEN.' }
$releaseRoot = Split-Path $PSScriptRoot -Parent
[xml] $project = Get-Content (Join-Path $releaseRoot 'DisplayFX/DisplayFX.csproj') -Raw
$version = [string] $project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
$tag = 'V' + $version
$api = "https://api.github.com/repos/$Repository"
$headers = @{ Authorization = 'Bearer ' + $env:GITHUB_TOKEN; Accept = 'application/vnd.github+json'; 'User-Agent' = 'DisplayFX-release'; 'X-GitHub-Api-Version' = '2022-11-28' }
try {
    $release = $null
    for ($page = 1; ; $page++) {
        $entries = @(Invoke-RestMethod "$api/releases?per_page=100&page=$page" -Headers $headers)
        $release = $entries | Where-Object tag_name -eq $tag | Select-Object -First 1
        if ($release -or $entries.Count -lt 100) { break }
    }
    if ($release -and -not $release.draft) { Write-Output "$tag is already published; it will not be overwritten."; exit 0 }
    $installer = Join-Path $releaseRoot 'artifacts/DisplayFX_Setup.exe'
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Build the installer before publishing.' }
    $hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($installer + '.sha256', "$hash  DisplayFX_Setup.exe`n", [Text.UTF8Encoding]::new($false))
    if (-not $release) {
        $body = @{
            tag_name = $tag; target_commitish = $Commit; name = "DisplayFX $version"
            body = Get-Content (Join-Path $releaseRoot "docs/releases/$version.md") -Raw
            draft = $true; prerelease = $false
        } | ConvertTo-Json
        $release = Invoke-RestMethod "$api/releases" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
    }
    if ($release.target_commitish -ne $Commit) { throw 'The existing draft targets a different commit.' }
    foreach ($name in @('DisplayFX_Setup.exe', 'DisplayFX_Setup.exe.sha256')) {
        $path = Join-Path $releaseRoot "artifacts/$name"
        $expected = 'sha256:' + (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $asset = $release.assets | Where-Object name -eq $name | Select-Object -First 1
        if (-not $asset) {
            $upload = "https://uploads.github.com/repos/$Repository/releases/$($release.id)/assets?name=" + [Uri]::EscapeDataString($name)
            $asset = Invoke-RestMethod $upload -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile $path
        }
        if ($asset.state -ne 'uploaded' -or $asset.size -ne (Get-Item $path).Length -or $asset.digest -ne $expected) {
            throw "Release asset verification failed: $name"
        }
        Write-Output "Verified $name"
    }
    $published = Invoke-RestMethod "$api/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json' -Body (@{ draft = $false; make_latest = 'true' } | ConvertTo-Json)
    if ($published.draft -or $published.prerelease -or $published.tag_name -ne $tag) { throw 'Release publication was not confirmed.' }
    $target = Invoke-RestMethod "$api/commits/$tag" -Headers $headers
    if ($target.sha -ne $Commit) { throw 'The published tag does not match the verified source commit.' }
    Write-Output "Published $($published.html_url)"
}
finally { $headers.Clear() }
