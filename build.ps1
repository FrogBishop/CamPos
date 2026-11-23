param(
    [switch]$SelfContained,
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

if ([string]::IsNullOrWhiteSpace($Output)) {
    if ($SelfContained) { $Output = "dist\CamPosSelfContained" } else { $Output = "dist\CamPos" }
}
$sc = if ($SelfContained) { "true" } else { "false" }

$publishArgs = @(
    "publish", "src\CamPos.App\CamPos.App.csproj",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $sc,
    "-p:PublishSingleFile=true",
    "-o", $Output
)
dotnet @publishArgs

$injectorArgs = @(
    "publish", "src\CamPos.Injector\CamPos.Injector.csproj",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $sc,
    "-p:PublishSingleFile=true",
    "-o", $Output
)
dotnet @injectorArgs

Copy-Item "native\CamPosHook.dll" $Output -Force
Copy-Item "README.md" $Output -Force
Copy-Item "LICENSE" $Output -Force
Copy-Item "CamPos.Injector.json" $Output -Force
Copy-Item "CamPos.Injector.example.json" $Output -Force
Copy-Item "docs" $Output -Recurse -Force
Get-ChildItem $Output -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host ""
Write-Host "Published to $Output"
