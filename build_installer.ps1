# PowerShell script to build the DisplayFX Windows Setup Installer.
[CmdletBinding()]
param(
    [string] $InnoCompiler,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [string] $DotNetPath = 'dotnet',
    [string] $SigningCertificateThumbprint,
    [ValidateSet('CurrentUser', 'LocalMachine')] [string] $CertificateStore = 'CurrentUser',
    [string] $TimestampServer = 'http://timestamp.digicert.com',
    [switch] $RequireSignature
)

$ErrorActionPreference = 'Stop'
$repoDirectory = [IO.Path]::GetFullPath($PSScriptRoot)
$signingCertificate = $null
if ($RequireSignature -and -not $SigningCertificateThumbprint) { throw 'A code-signing certificate is required for a signed release. Local test builds can omit -RequireSignature.' }
if ($SigningCertificateThumbprint) {
    if ($SigningCertificateThumbprint -notmatch '^[0-9a-fA-F]{40,64}$') { throw 'Specify the thumbprint of an existing code-signing certificate; do not pass a key or password.' }
    $signingCertificate = Get-Item -LiteralPath "Cert:\$CertificateStore\My\$SigningCertificateThumbprint"
    $signingUsages = @($signingCertificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | ForEach-Object { $_.EnhancedKeyUsages } | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' })
    if (-not $signingCertificate.HasPrivateKey -or $signingCertificate.NotAfter -le (Get-Date) -or
        $signingUsages.Count -eq 0) {
        throw 'The selected certificate must have an accessible private key, be valid, and allow code signing.'
    }
}
function Sign-DisplayFXFile([string] $Path) {
    if (-not $signingCertificate) { return }
    $signature = Set-AuthenticodeSignature -LiteralPath $Path -Certificate $signingCertificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer
    if ($signature.Status -ne 'Valid') { throw "Authenticode signing failed for $Path`: $($signature.Status)" }
}
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
    & $DotNetPath publish $projectPath -c Release -r win-x64 --self-contained true --output $stagingDirectory -m:1 /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        throw "Application publish failed with exit code $LASTEXITCODE."
    }
    & $DotNetPath publish (Join-Path $repoDirectory 'DisplayFX.Cli\DisplayFX.Cli.csproj') -c Release -r win-x64 --self-contained true --output $stagingDirectory -m:1 /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "CLI publish failed with exit code $LASTEXITCODE." }
    foreach ($localState in @('Data', 'Settings', 'Logs', 'History', 'portable.flag', 'Runtime.json')) {
        if (Test-Path -LiteralPath (Join-Path $stagingDirectory $localState)) { throw "Local state must not be packaged: $localState" }
    }
    foreach ($file in @('DisplayFX.exe','DisplayFX.dll','WindowsDisplayAPI.dll','DisplayFX.Cli.exe','DisplayFX.Cli.dll')) { Sign-DisplayFXFile (Join-Path $stagingDirectory $file) }

    Write-Host 'Compiling Installer...' -ForegroundColor Cyan
    & $InnoCompiler "/DMyAppSourceDir=$stagingDirectory" "/O$outputPath" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Installer compilation failed with exit code $LASTEXITCODE."
    }
    $builtInstaller = Join-Path $outputPath 'DisplayFX_Setup.exe'
    Sign-DisplayFXFile $builtInstaller
    if (-not $signingCertificate) { Write-Warning 'Unsigned local/test build. Configure a publisher certificate and -RequireSignature before distributing a signed release.' }
    $installerHash = (Get-FileHash -LiteralPath $builtInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($builtInstaller + '.sha256', "$installerHash  DisplayFX_Setup.exe`n", [Text.UTF8Encoding]::new($false))

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
