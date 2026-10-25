param([string]$ProjectRoot=(Split-Path $PSScriptRoot -Parent),[string]$SourceRoot='D:\Agant\Github_Source')
$ErrorActionPreference='Stop'
$projectPath=[IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\')
$sourceBoundary=[IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')+'\'
$destination=[IO.Path]::GetFullPath((Join-Path $SourceRoot 'Gitstar'))
if(-not $destination.StartsWith($sourceBoundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Source destination escaped configured directory'}
if($projectPath.Equals($destination,[StringComparison]::OrdinalIgnoreCase)){Write-Output "Building from source directory: $destination";return}
if($destination.StartsWith($projectPath+'\',[StringComparison]::OrdinalIgnoreCase) -or $projectPath.StartsWith($destination+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Source export must be outside the project directory'}
# Never follow links or alter the destination repository metadata.
foreach($candidatePath in @($projectPath,$destination)){
    $cursor=$candidatePath
    while($cursor){if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Source export path contains a link: $cursor"}};$cursor=Split-Path $cursor -Parent}
}
$excluded='(^|\\)(bin|obj|\.git|\.vs|node_modules|artifacts|outputs|dist|\.gradle|\.gradle-local)(\\|$)'
$files=@('README.md','CHANGELOG.md','CHANGELOG_HISTORY.md','LICENSE','.gitignore','pantone-line.svg','docs\Gitstar使用说明.md','docs\实现与验证.md')
$windowsRoot=Join-Path $projectPath 'windows'
$pending=[Collections.Generic.Stack[string]]::new();$pending.Push($windowsRoot)
$screenshotsRoot=Join-Path $projectPath 'docs\screenshots'
if(Test-Path -LiteralPath $screenshotsRoot){
    foreach($folder in @((Join-Path $projectPath 'docs'),$screenshotsRoot)){
        if((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Screenshot directory is a link: $folder"}
    }
    $pending.Push($screenshotsRoot)
}
$supportRoot=Join-Path $projectPath 'docs\support'
if(Test-Path -LiteralPath $supportRoot){
    foreach($folder in @((Join-Path $projectPath 'docs'),$supportRoot)){
        if((Get-Item -LiteralPath $folder -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Support asset directory is a link: $folder"}
    }
    $pending.Push($supportRoot)
}
while($pending.Count){
    foreach($item in Get-ChildItem -LiteralPath $pending.Pop() -Force){
        $relative=$item.FullName.Substring($projectPath.Length+1)
        if($relative -match $excluded){continue}
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Source file or directory is a link: $relative"}
        if($item.PSIsContainer){$pending.Push($item.FullName)}else{$files+=$relative}
    }
}
# Validate every destination before copying; preserve .git and all unrelated files.
foreach($relative in $files){
    $from=Join-Path $projectPath $relative
    if(-not(Test-Path -LiteralPath $from -PathType Leaf)){throw "Source file missing: $relative"}
    if((Get-Item -LiteralPath $from -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Source file is a link: $relative"}
    $cursor=Join-Path $destination $relative
    while($cursor.Length -gt $destination.Length){
        if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Destination contains a link: $cursor"}}
        $cursor=Split-Path $cursor -Parent
    }
}
foreach($relative in $files){
    $from=Join-Path $projectPath $relative;$to=Join-Path $destination $relative
    New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $from -Destination $to -Force
    if((Get-FileHash -LiteralPath $from).Hash -ne (Get-FileHash -LiteralPath $to).Hash){throw "Source copy verification failed: $relative"}
}
Write-Output "Source exported and verified: $destination ($($files.Count) files)"
