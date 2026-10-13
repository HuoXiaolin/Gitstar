param([string]$Dotnet='D:\Agant\toolchain\dotnet\dotnet.exe',[string]$Inno=(Join-Path $PSScriptRoot '..\build-tools\InnoSetup\ISCC.exe'),[string]$OutputRoot='D:\Agant\Github_Releases\Gitstar',[string]$SourceRoot='D:\Agant\Github_Source')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
[xml]$versionProps=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props')
$version=$versionProps.Project.PropertyGroup.Version
$publishPath=Join-Path $projectRoot 'artifacts\singlefile-publish'
$outputRoot=[IO.Path]::GetFullPath($OutputRoot)
$releasePath=$outputRoot
$buildPath=Join-Path $projectRoot 'artifacts\release-build\'
& $Dotnet build (Join-Path $PSScriptRoot 'Gitstar.Desktop\Gitstar.Desktop.csproj') -c Release "-p:BaseOutputPath=$buildPath" --ignore-failed-sources
if($LASTEXITCODE -ne 0){throw 'Build failed'}
& $Dotnet publish (Join-Path $PSScriptRoot 'Gitstar.Desktop\Gitstar.Desktop.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableSingleFileAnalyzer=false -p:DebugType=None -p:DebugSymbols=false "-p:BaseOutputPath=$buildPath" -o $publishPath
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\Gitstar使用说明.md') -Destination (Join-Path $publishPath '使用说明.md')
New-Item -ItemType Directory -Force -Path $releasePath | Out-Null
$releaseNames=@('Gitstar.exe','使用说明.md')
foreach($releaseName in $releaseNames){
  $releaseFile=Join-Path $releasePath $releaseName
  try{Copy-Item -LiteralPath (Join-Path $publishPath $releaseName) -Destination $releaseFile -Force -ErrorAction Stop}
  catch{
    if($releaseName -ne 'Gitstar.exe' -or -not(Test-Path -LiteralPath $releaseFile)){throw}
    $retiredRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\retired-runtime'))
    New-Item -ItemType Directory -Path $retiredRoot -Force | Out-Null
    $retiredFile=[IO.Path]::GetFullPath((Join-Path $retiredRoot ('Gitstar-'+[guid]::NewGuid().ToString('N')+'.exe')))
    if(-not $retiredFile.StartsWith($retiredRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Retired executable escaped build directory'}
    Move-Item -LiteralPath $releaseFile -Destination $retiredFile -ErrorAction Stop
    try{Copy-Item -LiteralPath (Join-Path $publishPath $releaseName) -Destination $releaseFile -ErrorAction Stop}catch{Move-Item -LiteralPath $retiredFile -Destination $releaseFile -ErrorAction Stop;throw}
    Write-Output 'Running app preserved; restart to load the updated executable.'
  }
}
Compress-Archive -LiteralPath ($releaseNames | ForEach-Object {Join-Path $releasePath $_}) -DestinationPath (Join-Path $outputRoot 'Gitstar-win-x64.zip') -Force
 $installerBuilt=$false
if(Test-Path -LiteralPath $Inno){& $Inno "/DReleaseRoot=$outputRoot" "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer\Gitstar.iss');if($LASTEXITCODE -ne 0){throw 'Installer build failed'};$installerBuilt=$true}
else {Write-Warning 'ISCC unavailable; ZIP is built, installer script retained.'}
$outputBoundary=[IO.Path]::GetFullPath($outputRoot).TrimEnd('\')+'\'
$legacyFolder=[IO.Path]::GetFullPath((Join-Path $outputRoot 'Gitstar-win-x64'))
if(Test-Path -LiteralPath $legacyFolder){
  if(-not $legacyFolder.StartsWith($outputBoundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Legacy folder escaped output directory'}
  if((Get-Item -LiteralPath $legacyFolder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Legacy release folder is a link'}
  $legacyItems=@(Get-ChildItem -LiteralPath $legacyFolder -Force)
  if(@($legacyItems | Where-Object {$_.PSIsContainer -or $_.Name -notin $releaseNames -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)}).Count){throw 'Legacy release folder contains extra files; cannot remove automatically'}
  foreach($legacyItem in $legacyItems){
    try{Remove-Item -LiteralPath $legacyItem.FullName -Force -ErrorAction Stop}
    catch{
      if($legacyItem.Name -ne 'Gitstar.exe'){throw}
      $retiredFolder=Join-Path $projectRoot 'artifacts\retired-runtime'
      New-Item -ItemType Directory -Path $retiredFolder -Force | Out-Null
      Move-Item -LiteralPath $legacyItem.FullName -Destination (Join-Path $retiredFolder ('Gitstar-'+[guid]::NewGuid().ToString('N')+'.exe')) -ErrorAction Stop
      Write-Output 'Running app preserved; restart from the new executable path.'
    }
  }
  Remove-Item -LiteralPath $legacyFolder -Force
}
foreach($oldRelease in Get-ChildItem -LiteralPath $outputRoot){
  if($oldRelease.Name -match '^Gitstar-\d+\.\d+\.\d+-win-x64(?:\.zip|-setup\.exe)?$'){
    $oldPath=[IO.Path]::GetFullPath($oldRelease.FullName)
    if(-not $oldPath.StartsWith($outputBoundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Release cleanup path escaped output directory'}
    if($oldRelease.PSIsContainer){Remove-Item -LiteralPath $oldPath -Recurse -Force}else{Remove-Item -LiteralPath $oldPath -Force}
  }
}
$hashFiles=@((Join-Path $outputRoot 'Gitstar.exe'),(Join-Path $outputRoot '使用说明.md'),(Join-Path $outputRoot 'Gitstar-win-x64.zip'))
if($installerBuilt){$hashFiles+=(Join-Path $outputRoot 'Gitstar-setup.exe')}
Get-Item -LiteralPath $hashFiles | ForEach-Object {$releaseHash=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; "$($releaseHash.Hash)  $($_.Name)"} | Set-Content (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding UTF8
& (Join-Path $PSScriptRoot 'export-source.ps1') -ProjectRoot $projectRoot -SourceRoot $SourceRoot
Get-ChildItem -LiteralPath $outputRoot -File | Select-Object Name,Length
