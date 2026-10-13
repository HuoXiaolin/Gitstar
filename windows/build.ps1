param([string]$Dotnet='D:\Agant\toolchain\dotnet\dotnet.exe',[string]$Inno=(Join-Path $PSScriptRoot '..\build-tools\InnoSetup\ISCC.exe'))
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
[xml]$versionProps=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props')
$version=$versionProps.Project.PropertyGroup.Version
$publishPath=Join-Path $projectRoot 'artifacts\singlefile-publish'
$outputRoot=Join-Path $projectRoot 'outputs'
$releasePath=Join-Path $outputRoot 'Gitstar-win-x64'
$buildPath=Join-Path $projectRoot 'artifacts\release-build\'
& $Dotnet build (Join-Path $PSScriptRoot 'Gitstar.Desktop\Gitstar.Desktop.csproj') -c Release "-p:BaseOutputPath=$buildPath" --ignore-failed-sources
if($LASTEXITCODE -ne 0){throw 'Build failed'}
& $Dotnet publish (Join-Path $PSScriptRoot 'Gitstar.Desktop\Gitstar.Desktop.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableSingleFileAnalyzer=false -p:DebugType=None -p:DebugSymbols=false "-p:BaseOutputPath=$buildPath" -o $publishPath
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\Gitstar使用说明.md') -Destination (Join-Path $publishPath '使用说明.md')
New-Item -ItemType Directory -Force -Path $releasePath | Out-Null
$releaseNames=@('Gitstar.exe','使用说明.md')
foreach($releaseName in $releaseNames){Copy-Item -LiteralPath (Join-Path $publishPath $releaseName) -Destination (Join-Path $releasePath $releaseName) -Force}
Compress-Archive -LiteralPath ($releaseNames | ForEach-Object {Join-Path $releasePath $_}) -DestinationPath (Join-Path $outputRoot 'Gitstar-win-x64.zip') -Force
 $installerBuilt=$false
if(Test-Path -LiteralPath $Inno){& $Inno (Join-Path $PSScriptRoot 'installer\Gitstar.iss');if($LASTEXITCODE -ne 0){throw 'Installer build failed'};$installerBuilt=$true}
else {Write-Warning 'ISCC unavailable; ZIP is built, installer script retained.'}
$outputBoundary=[IO.Path]::GetFullPath($outputRoot).TrimEnd('\')+'\'
foreach($oldRelease in Get-ChildItem -LiteralPath $outputRoot){
  if($oldRelease.Name -match '^Gitstar-\d+\.\d+\.\d+-win-x64(?:\.zip|-setup\.exe)?$'){
    $oldPath=[IO.Path]::GetFullPath($oldRelease.FullName)
    if(-not $oldPath.StartsWith($outputBoundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Release cleanup path escaped outputs'}
    if($oldRelease.PSIsContainer){Remove-Item -LiteralPath $oldPath -Recurse -Force}else{Remove-Item -LiteralPath $oldPath -Force}
  }
}
$hashFiles=@((Join-Path $outputRoot 'Gitstar-win-x64.zip'))
if($installerBuilt){$hashFiles+=(Join-Path $outputRoot 'Gitstar-setup.exe')}
Get-Item -LiteralPath $hashFiles | ForEach-Object {$releaseHash=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; "$($releaseHash.Hash)  $($_.Name)"} | Set-Content (Join-Path $outputRoot 'SHA256SUMS.txt')
Get-ChildItem -LiteralPath $outputRoot -File | Select-Object Name,Length
