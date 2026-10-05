# PowerShell script to build the DisplayFX Windows Setup Installer.
[CmdletBinding()]
param(
    [string] $InnoCompiler,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [string] $DotNetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$projectPath = Join-Path $repoDirectory 'DisplayFX\DisplayFX.csproj'
$installerScript = Join-Path $repoDirectory 'DisplayFX.iss'
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)

# Keep the installer release number consistent with the app metadata.
[xml] $projectDefinition = Get-Content -LiteralPath $projectPath -Raw
$appVersion = [string] $projectDefinition.Project.PropertyGroup.Version
$installerDefinition = Get-Content -LiteralPath $installerScript -Raw
$installerVersionMatch = [regex]::Match($installerDefinition, '(?m)^#define MyAppVersion "([^"]+)"')
if (-not $appVersion -or -not $installerVersionMatch.Success -or $installerVersionMatch.Groups[1].Value -ne $appVersion) {
    throw 'The app and installer version numbers must match before publishing.'
}

if (-not $InnoCompiler) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $compilerCandidates = @(
        $(if ($compilerCommand) { $compilerCommand.Source }),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $InnoCompiler = $compilerCandidates |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Select-Object -First 1
}

if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Inno Setup Compiler (ISCC.exe) not found. Install Inno Setup 6 or pass -InnoCompiler.'
}
if (-not (Get-Command $DotNetPath -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK (dotnet) was not found.'
}

# A new staging directory prevents stale DLLs and local settings from entering a release.
$stagingParent = [IO.Path]::GetFullPath((Join-Path $repoDirectory 'obj'))
$stagingDirectory = Join-Path $stagingParent ('installer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
try {
    Write-Host 'Building DisplayFX Application...' -ForegroundColor Cyan
    & $DotNetPath publish $projectPath -c Release -r win-x64 --self-contained true --output $stagingDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Application publish failed with exit code $LASTEXITCODE."
    }

    Write-Host 'Compiling Installer...' -ForegroundColor Cyan
    & $InnoCompiler "/DMyAppSourceDir=$stagingDirectory" "/O$outputPath" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Installer compilation failed with exit code $LASTEXITCODE."
    }

    Write-Host "Installer compiled successfully! Output: $(Join-Path $outputPath 'DisplayFX_Setup.exe')" -ForegroundColor Green
}
finally {
    # Resolve and verify the generated target before deleting it recursively.
    $resolvedStaging = [IO.Path]::GetFullPath($stagingDirectory)
    if ([IO.Path]::GetDirectoryName($resolvedStaging) -ne $stagingParent -or
        [IO.Path]::GetFileName($resolvedStaging) -notmatch '^installer-[0-9a-f]{32}$') {
        throw 'Refusing to remove an unexpected staging directory.'
    }
    Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
}
