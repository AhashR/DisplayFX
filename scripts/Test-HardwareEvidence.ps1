[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Path,
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+(?:-preview\.\d+)?$')] [string] $Version
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Hardware qualification evidence is missing: $Path. Record actual results; do not mark untested hardware as passed." }
$evidence = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
if ($evidence.Version -ne $Version -or -not $evidence.TestedAt -or -not $evidence.Operator) { throw 'Hardware evidence must identify this version, test date and operator.' }
$required = @('NVIDIA-SDR-DDC', 'HDR-GAMMA-GUARD', 'MIXED-DPI', 'SLEEP-WAKE', 'DOCK-RECONNECT', 'INTERNAL-PANEL', 'AMD-SDR', 'INTEL-SDR', 'INSTALL-UPGRADE-UNINSTALL')
foreach ($id in $required) {
    $cases = @($evidence.Cases | Where-Object Id -eq $id)
    if ($cases.Count -ne 1 -or $cases[0].Result -ne 'Passed' -or -not $cases[0].Setup -or -not $cases[0].Notes) { throw "Hardware case $id must contain one actual Passed result with setup and observations. Keep incomplete builds in the test channel." }
}
Write-Output "Hardware qualification evidence verified for $Version ($($required.Count) cases)."
