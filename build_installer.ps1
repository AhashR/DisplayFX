# PowerShell script to build the DisplayFX Windows Setup Installer

Write-Host "Building DisplayFX Application..." -ForegroundColor Cyan
dotnet publish DisplayFX\DisplayFX.csproj -c Release --output app_files

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}

$innoCompiler = "C:\Users\Ahash\AppData\Local\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $innoCompiler)) {
    $innoCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
}

if (-not (Test-Path $innoCompiler)) {
    Write-Host "Inno Setup Compiler (ISCC.exe) not found!" -ForegroundColor Red
    exit 1
}

Write-Host "Compiling Installer..." -ForegroundColor Cyan
& $innoCompiler DisplayFX.iss

if ($LASTEXITCODE -eq 0) {
    Write-Host "Installer compiled successfully! Output: installer_output\DisplayFX_Setup.exe" -ForegroundColor Green
} else {
    Write-Host "Installer compilation failed!" -ForegroundColor Red
}
