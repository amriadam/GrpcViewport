# Builds both standalone packages into .\artifacts\
# Usage (from the repo root):  .\build.ps1
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out  = Join-Path $root "artifacts"
$rid  = "win-x64"

function Assert-Ok($what) {
    # External programs (npm, dotnet, msbuild) don't throw on failure: check the exit code.
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
New-Item $out -ItemType Directory | Out-Null

# ---------------------------------------------------------------- 1. page --
Push-Location "$root\web"
npm ci;         Assert-Ok "npm ci"          # exact versions from package-lock.json (+ buf gen)
npm run build;  Assert-Ok "npm run build"   # -> web\dist
Pop-Location

# ------------------------------------------------------------ 2. WPF app --
$wpfOut = "$out\GrpcViewport-Wpf"
dotnet publish "$root\dotnet\GrpcViewport.Wpf\GrpcViewport.Wpf.csproj" `
    -c Release -r $rid --self-contained true -o $wpfOut
Assert-Ok "WPF publish"

# ---------------------------------------------------- 3. WinForms + Host --
# .NET Framework + packages.config needs Visual Studio's MSBuild (dotnet build can't restore it).
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild `
    -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild not found (install Visual Studio or Build Tools)" }

& $msbuild "$root\dotnet\GrpcViewport.WinForms\GrpcViewport.WinForms.csproj" `
    -restore -t:Rebuild -v:minimal `
    -p:Configuration=Release `
    -p:RestorePackagesConfig=true `
    -p:SolutionDir="$root\dotnet\\"
Assert-Ok "WinForms build"

$wfOut = "$out\GrpcViewport-WinForms"
New-Item $wfOut -ItemType Directory | Out-Null
Copy-Item "$root\dotnet\GrpcViewport.WinForms\bin\Release\*" $wfOut -Recurse

# The Host goes into the WinForms folder as host\ (HostLauncher looks there first).
dotnet publish "$root\dotnet\GrpcViewport.Host\GrpcViewport.Host.csproj" `
    -c Release -r $rid --self-contained true -o "$wfOut\host"
Assert-Ok "Host publish"

# ------------------------------------------------------------------ 4. zip --
Compress-Archive "$wpfOut\*" "$out\GrpcViewport-Wpf.zip"
Compress-Archive "$wfOut\*"  "$out\GrpcViewport-WinForms.zip"

Write-Host "`nDone:" -ForegroundColor Green
Get-ChildItem "$out\*.zip" | ForEach-Object { "{0,-30} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }