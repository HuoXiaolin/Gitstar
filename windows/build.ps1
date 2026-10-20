param([string]$Dotnet='D:\Agant\toolchain\dotnet\dotnet.exe',[string]$Inno=(Join-Path $PSScriptRoot '..\build-tools\InnoSetup\ISCC.exe'),[string]$OutputRoot='D:\Agant\Github_Releases\Gitstar',[string]$SourceRoot='D:\Agant\Github_Source')
$ErrorActionPreference='Stop'
if(-not $PSBoundParameters.ContainsKey('Dotnet')){$dedicated='D:\Agant\toolchain\dotnet_10.0.401_Gitstar\dotnet.exe';if(Test-Path -LiteralPath $dedicated){$Dotnet=$dedicated}}
$projectRoot=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'release-version.ps1')
$lockDirectory=Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $lockDirectory -Force | Out-Null
try{$buildLock=[IO.File]::Open((Join-Path $lockDirectory 'installer-build.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
catch{throw 'Another installer build is already running for this project'}
try{
$versionPlan=Get-InstallerVersionPlan -PropsPath (Join-Path $PSScriptRoot 'Directory.Build.props') -BuildInstaller (Test-Path -LiteralPath $Inno -PathType Leaf)
$version=$versionPlan.Version
$versionArguments=@("-p:Version=$version","-p:AssemblyVersion=$($versionPlan.NumericVersion)","-p:FileVersion=$($versionPlan.NumericVersion)","-p:InformationalVersion=$version")
Write-Output "Installer version: $version (Beijing day $($versionPlan.BuildDate); incremented=$($versionPlan.Incremented))"
$publishPath=Join-Path $projectRoot 'artifacts\singlefile-publish'
$outputRoot=[IO.Path]::GetFullPath($OutputRoot)
$releasePath=$outputRoot
$buildPath=Join-Path $projectRoot 'artifacts\release-build\'
& $Dotnet build (Join-Path $PSScriptRoot 'Gitstar.Desktop\Gitstar.Desktop.csproj') -c Release "-p:BaseOutputPath=$buildPath" --ignore-failed-sources @versionArguments
if($LASTEXITCODE -ne 0){throw 'Build failed'}
& $Dotnet publish (Join-Path $PSScriptRoot 'Gitstar.Desktop\Gitstar.Desktop.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableSingleFileAnalyzer=false -p:DebugType=None -p:DebugSymbols=false "-p:BaseOutputPath=$buildPath" -o $publishPath @versionArguments
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\Gitstar使用说明.md') -Destination (Join-Path $publishPath '使用说明.md')
if($versionPlan.BuildInstaller){
  $stagedGuide=Join-Path $publishPath '使用说明.md'
  Write-ReleaseText -Path $stagedGuide -Text (Set-ReleaseDocumentVersion -Text ([IO.File]::ReadAllText($stagedGuide)) -Kind guide -Version $version)
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $publishPath 'LICENSE')
New-Item -ItemType Directory -Force -Path $releasePath | Out-Null
$releaseNames=@('Gitstar.exe','使用说明.md','LICENSE')
$installerBuilt=$false
if($versionPlan.BuildInstaller){
  # Compile in staging before replacing the official release files.
  $stagedInstaller=Join-Path $publishPath 'Gitstar-setup.exe'
  if(Test-Path -LiteralPath $stagedInstaller){Remove-Item -LiteralPath $stagedInstaller -Force}
  & $Inno "/DReleaseRoot=$publishPath" "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer\Gitstar.iss')
  if($LASTEXITCODE -ne 0 -or -not(Test-Path -LiteralPath $stagedInstaller -PathType Leaf)){throw 'Installer build failed'}
  $installerBuilt=$true
  $releaseNames+='Gitstar-setup.exe'
}
else {Write-Warning 'ISCC unavailable; portable EXE is built, installer script retained.'}
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
$outputBoundary=[IO.Path]::GetFullPath($outputRoot).TrimEnd('\')+'\'
$obsoleteZip=[IO.Path]::GetFullPath((Join-Path $outputRoot 'Gitstar-win-x64.zip'))
if(Test-Path -LiteralPath $obsoleteZip){
  if(-not $obsoleteZip.StartsWith($outputBoundary,[StringComparison]::OrdinalIgnoreCase)){throw 'ZIP cleanup path escaped output directory'}
  $zipItem=Get-Item -LiteralPath $obsoleteZip -Force
  if($zipItem.PSIsContainer -or ($zipItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Obsolete ZIP is not a regular file'}
  Remove-Item -LiteralPath $obsoleteZip -Force
}
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
$hashFiles=@((Join-Path $outputRoot 'Gitstar.exe'),(Join-Path $outputRoot '使用说明.md'),(Join-Path $outputRoot 'LICENSE'))
if($installerBuilt){$hashFiles+=(Join-Path $outputRoot 'Gitstar-setup.exe')}
Get-Item -LiteralPath $hashFiles | ForEach-Object {$releaseHash=Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256; "$($releaseHash.Hash)  $($_.Name)"} | Set-Content (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding UTF8
if($installerBuilt){Complete-InstallerVersion -Plan $versionPlan -ProjectRoot $projectRoot}
& (Join-Path $PSScriptRoot 'export-source.ps1') -ProjectRoot $projectRoot -SourceRoot $SourceRoot
Get-ChildItem -LiteralPath $outputRoot -File | Select-Object Name,Length
}finally{$buildLock.Dispose()}
