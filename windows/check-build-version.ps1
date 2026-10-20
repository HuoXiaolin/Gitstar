$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'release-version.ps1')
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('Gitstar-version-check-'+[guid]::NewGuid().ToString('N'))
$fixture=Join-Path $testRoot 'project'
$checks=0
function Assert-VersionRule {param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message};$script:checks++}
function Assert-VersionThrows {param([scriptblock]$Action,[string]$Pattern)
    $caught=$null
    try{& $Action | Out-Null}catch{$caught=$_}
    Assert-VersionRule ($null -ne $caught -and $caught.ToString() -match $Pattern) "Expected failure: $Pattern; actual: $caught"
}
try{
    New-Item -ItemType Directory -Path (Join-Path $fixture 'windows\installer'),(Join-Path $fixture 'docs') -Force | Out-Null
    $originalRoot=Split-Path $PSScriptRoot -Parent
    foreach($relative in @('README.md','CHANGELOG.md','CHANGELOG_HISTORY.md','LICENSE','.gitignore','pantone-line.svg','docs\Gitstar使用说明.md','docs\实现与验证.md','windows\build.ps1','windows\release-version.ps1','windows\export-source.ps1','windows\Directory.Build.props','windows\installer\Gitstar.iss')){
        Copy-Item -LiteralPath (Join-Path $originalRoot $relative) -Destination (Join-Path $fixture $relative)
    }
    $propsPath=Join-Path $fixture 'windows\Directory.Build.props'
    [IO.File]::WriteAllText($propsPath,'<Project><PropertyGroup><Version>1.0.0</Version><AssemblyVersion>1.0.0.0</AssemblyVersion><FileVersion>1.0.0.0</FileVersion><InformationalVersion>1.0.0</InformationalVersion></PropertyGroup></Project>')
    $today=Get-InstallerBuildDay
    Assert-VersionRule ((Get-InstallerBuildDay -UtcNow ([datetime]::new(2026,10,20,15,59,59,[DateTimeKind]::Utc))) -eq [datetime]'2026-10-20') 'Before Beijing midnight belongs to the previous day'
    Assert-VersionRule ((Get-InstallerBuildDay -UtcNow ([datetime]::new(2026,10,20,16,0,0,[DateTimeKind]::Utc))) -eq [datetime]'2026-10-21') 'Beijing midnight starts a new packaging day'
    $first=Get-InstallerVersionPlan -PropsPath $propsPath -BuildDate $today
    Assert-VersionRule ($first.Version -eq '1.0.1' -and $first.Incremented) 'First installer increments the third part'
    Assert-VersionRule ((Get-InstallerVersionPlan -PropsPath $propsPath -BuildDate $today).Version -eq '1.0.1') 'Planning without success never consumes a version'
    Assert-VersionRule ((Get-InstallerVersionPlan -PropsPath $propsPath -BuildDate $today -BuildInstaller $false).Version -eq '1.0.0') 'Portable-only builds retain the current version'
    $fakeDotnet=Join-Path $testRoot 'dotnet.ps1'
    $fakeInno=Join-Path $testRoot 'inno.ps1'
    [IO.File]::WriteAllText($fakeDotnet,@'
Add-Content -LiteralPath (Join-Path $PSScriptRoot 'dotnet-arguments.txt') -Value ($args -join '|')
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'fail-dotnet')){exit 1}
if($args[0] -eq 'publish'){
  $output=$args[[array]::IndexOf($args,'-o')+1]
  New-Item -ItemType Directory -Path $output -Force | Out-Null
  [IO.File]::WriteAllText((Join-Path $output 'Gitstar.exe'),($args -join '|'))
}
exit 0
'@)
    [IO.File]::WriteAllText($fakeInno,@'
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'fail-inno')){exit 1}
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'empty-inno')){exit 0}
$release=($args | Where-Object {$_ -like '/DReleaseRoot=*'}).Substring(14)
$version=($args | Where-Object {$_ -like '/DAppVersion=*'}).Substring(13)
[IO.File]::WriteAllText((Join-Path $release 'Gitstar-setup.exe'),$version)
exit 0
'@)
    $build=Join-Path $fixture 'windows\build.ps1'
    $output=Join-Path $testRoot 'release'
    $source=Join-Path $testRoot 'source'
    $buildArgs=@{Dotnet=$fakeDotnet;Inno=$fakeInno;OutputRoot=$output;SourceRoot=$source}
    $before=(Get-FileHash -LiteralPath $propsPath).Hash
    [IO.File]::WriteAllText((Join-Path $testRoot 'fail-dotnet'),'')
    Assert-VersionThrows {& $build @buildArgs} 'Build failed'
    Remove-Item -LiteralPath (Join-Path $testRoot 'fail-dotnet')
    Assert-VersionRule ((Get-FileHash -LiteralPath $propsPath).Hash -eq $before) 'Failed compile does not change version or date'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $officialExe=Join-Path $output 'Gitstar.exe'
    [IO.File]::WriteAllText($officialExe,'previous release')
    [IO.File]::WriteAllText((Join-Path $testRoot 'fail-inno'),'')
    Assert-VersionThrows {& $build @buildArgs} 'Installer build failed'
    Remove-Item -LiteralPath (Join-Path $testRoot 'fail-inno')
    Assert-VersionRule ((Get-FileHash -LiteralPath $propsPath).Hash -eq $before) 'Failed installer does not change version or date'
    Assert-VersionRule ([IO.File]::ReadAllText($officialExe) -eq 'previous release') 'Failed installer preserves the official executable'
    [IO.File]::WriteAllText((Join-Path $testRoot 'empty-inno'),'')
    Assert-VersionThrows {& $build @buildArgs} 'Installer build failed'
    Remove-Item -LiteralPath (Join-Path $testRoot 'empty-inno')
    Assert-VersionRule ((Get-FileHash -LiteralPath $propsPath).Hash -eq $before) 'Successful exit without an installer does not consume a version'
    & $build @buildArgs | Out-Null
    [xml]$saved=[IO.File]::ReadAllText($propsPath)
    Assert-VersionRule ($saved.Project.PropertyGroup.Version -eq '1.0.1') 'Successful packaging commits the increment'
    Assert-VersionRule ($saved.Project.PropertyGroup.FileVersion -eq '1.0.1.0' -and $saved.Project.PropertyGroup.AssemblyVersion -eq '1.0.1.0') 'Windows numeric versions remain aligned'
    Assert-VersionRule ($saved.Project.PropertyGroup.LastInstallerBuildDate -eq $today.ToString('yyyy-MM-dd')) 'Successful date is stored in source'
    Assert-VersionRule ([IO.File]::ReadAllText((Join-Path $output 'Gitstar-setup.exe')) -eq '1.0.1') 'Installer receives the selected version'
    $arguments=[IO.File]::ReadAllText((Join-Path $testRoot 'dotnet-arguments.txt'))
    Assert-VersionRule ($arguments.Contains('-p:Version=1.0.1') -and $arguments.Contains('-p:FileVersion=1.0.1.0')) 'Compiler receives explicit matching versions'
    foreach($relative in @('README.md','docs\Gitstar使用说明.md','CHANGELOG.md','windows\installer\Gitstar.iss')){
        Assert-VersionRule ([IO.File]::ReadAllText((Join-Path $fixture $relative)).Contains('1.0.1')) "Current version label updated: $relative"
    }
    Assert-VersionRule ((Get-FileHash -LiteralPath (Join-Path $fixture 'CHANGELOG_HISTORY.md')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $originalRoot 'CHANGELOG_HISTORY.md')).Hash) 'Historical versions are unchanged'
    Assert-VersionRule ((Get-FileHash -LiteralPath (Join-Path $source 'Gitstar\windows\Directory.Build.props')).Hash -eq (Get-FileHash -LiteralPath $propsPath).Hash) 'Export retains the version and successful date'
    & $build @buildArgs | Out-Null
    Assert-VersionRule ((Get-InstallerVersionPlan -PropsPath $propsPath).Version -eq '1.0.1') 'Repeated packaging on the same day does not increment'
    $next=Get-InstallerVersionPlan -PropsPath $propsPath -BuildDate ($today.AddDays(1))
    Assert-VersionRule ($next.Version -eq '1.0.2' -and $next.Incremented) 'The next calendar day increments once'
    # A new project-local lock file after cache cleanup does not reset the saved date.
    Remove-Item -LiteralPath (Join-Path $fixture 'artifacts\installer-build.lock')
    & $build @buildArgs | Out-Null
    Assert-VersionRule ((Get-InstallerVersionPlan -PropsPath $propsPath).Version -eq '1.0.1') 'Recreating build cache does not increment again'
    $heldLock=[IO.File]::Open((Join-Path $fixture 'artifacts\installer-build.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try{Assert-VersionThrows {& $build @buildArgs} 'Another installer build'}finally{$heldLock.Dispose()}
    $noInstaller=@{Dotnet=$fakeDotnet;Inno=(Join-Path $testRoot 'missing-iscc.exe');OutputRoot=$output;SourceRoot=$source}
    $savedHash=(Get-FileHash -LiteralPath $propsPath).Hash
    & $build @noInstaller 3>$null | Out-Null
    Assert-VersionRule ((Get-FileHash -LiteralPath $propsPath).Hash -eq $savedHash) 'Unavailable ISCC does not write the daily marker'
    $stalePlan=Get-InstallerVersionPlan -PropsPath $propsPath
    [IO.File]::AppendAllText($propsPath,[Environment]::NewLine)
    Assert-VersionThrows {Complete-InstallerVersion -Plan $stalePlan -ProjectRoot $fixture} 'Version source changed'
    [IO.File]::WriteAllText($propsPath,'<Project><PropertyGroup><Version>1.0.0</Version><LastInstallerBuildDate>invalid</LastInstallerBuildDate></PropertyGroup></Project>')
    Assert-VersionThrows {Get-InstallerVersionPlan -PropsPath $propsPath} 'Invalid LastInstallerBuildDate'
    Write-Output "All $checks installer version checks passed (isolated compiler/installer fixtures)."
}finally{
    $resolved=[IO.Path]::GetFullPath($testRoot)
    $tempBoundary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if(-not $resolved.StartsWith($tempBoundary,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'Gitstar-version-check-*'){throw 'Test cleanup escaped its dedicated directory'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
