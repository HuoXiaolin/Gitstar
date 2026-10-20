# The successful installer date lives with the version, never in disposable build caches.
function Get-InstallerBuildDay {
    param([datetime]$UtcNow=[datetime]::UtcNow)
    [TimeZoneInfo]::ConvertTimeFromUtc($UtcNow,[TimeZoneInfo]::FindSystemTimeZoneById('China Standard Time')).Date
}

function Get-InstallerVersionPlan {
    param([string]$PropsPath,[datetime]$BuildDate=(Get-InstallerBuildDay),[bool]$BuildInstaller=$true)
    [xml]$props=[IO.File]::ReadAllText($PropsPath)
    $versionNodes=@($props.SelectNodes('/Project/PropertyGroup/Version'))
    if($versionNodes.Count -ne 1){throw 'Expected one project Version property'}
    $current=$versionNodes[0].InnerText
    if($current -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'){throw 'Installer version must have three numeric parts'}
    $parts=@($current.Split('.') | ForEach-Object {[int]$_})
    if(@($parts | Where-Object {$_ -gt 65535}).Count){throw 'Version exceeds Windows file-version limits'}
    $dateNodes=@($props.SelectNodes('/Project/PropertyGroup/LastInstallerBuildDate'))
    if($dateNodes.Count -gt 1){throw 'Expected at most one LastInstallerBuildDate property'}
    $today=$BuildDate.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $lastDate=if($dateNodes.Count){$dateNodes[0].InnerText}else{''}
    if($lastDate){
        $parsedDate=[datetime]::MinValue
        if(-not [datetime]::TryParseExact($lastDate,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$parsedDate)){throw 'Invalid LastInstallerBuildDate'}
        if($parsedDate.Date -gt $BuildDate.Date){throw 'Last installer date is in the future; check the local clock'}
    }
    $increment=$BuildInstaller -and $lastDate -ne $today
    $version=$current
    if($increment){
        if($parts[2] -eq 65535){throw 'Patch version exhausted; set the next major/minor version explicitly'}
        $version='{0}.{1}.{2}' -f $parts[0],$parts[1],($parts[2]+1)
    }
    [pscustomobject]@{
        PropsPath=[IO.Path]::GetFullPath($PropsPath)
        OriginalHash=(Get-FileHash -LiteralPath $PropsPath -Algorithm SHA256).Hash
        CurrentVersion=$current
        Version=$version
        NumericVersion=$version+'.0'
        BuildDate=$today
        BuildInstaller=$BuildInstaller
        Incremented=$increment
    }
}

function Set-ReleaseDocumentVersion {
    param([string]$Text,[ValidateSet('readme','guide','changelog','installer')][string]$Kind,[string]$Version)
    $pattern=switch($Kind){
        'readme' {'(?m)^(当前版本：\*\*)[0-9]+\.[0-9]+\.[0-9]+(\*\*)'}
        'guide' {'\A(# Gitstar )[0-9]+\.[0-9]+\.[0-9]+( · Windows x64 使用说明)'}
        'changelog' {'(?m)^(## )[0-9]+\.[0-9]+\.[0-9]+( · [0-9]{4}-[0-9]{2}-[0-9]{2})'}
        'installer' {'(?m)^(  #define AppVersion ")[0-9]+\.[0-9]+\.[0-9]+(")'}
    }
    $regex=[regex]::new($pattern)
    if($regex.Matches($Text).Count -ne 1){throw "Expected one version label in $Kind"}
    $regex.Replace($Text,[Text.RegularExpressions.MatchEvaluator]{param($match) $match.Groups[1].Value+$Version+$match.Groups[2].Value})
}

function Write-ReleaseText {
    param([string]$Path,[string]$Text)
    $bytes=[IO.File]::ReadAllBytes($Path)
    $bom=$bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191
    $temporary=$Path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    try{
        [IO.File]::WriteAllText($temporary,$Text,[Text.UTF8Encoding]::new($bom))
        for($attempt=0;;$attempt++){
            try{[IO.File]::Replace($temporary,$Path,[NullString]::Value);break}
            catch{
                if($attempt -ge 3){throw "Cannot commit release text '$Path': $_"}
                Start-Sleep -Milliseconds 100
            }
        }
    }finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
}

function Complete-InstallerVersion {
    param($Plan,[string]$ProjectRoot)
    if(-not $Plan.BuildInstaller){return}
    if((Get-FileHash -LiteralPath $Plan.PropsPath -Algorithm SHA256).Hash -ne $Plan.OriginalHash){throw 'Version source changed during the build; refusing to overwrite it'}
    [xml]$props=[IO.File]::ReadAllText($Plan.PropsPath)
    $versionNode=$props.SelectSingleNode('/Project/PropertyGroup/Version')
    $group=$versionNode.ParentNode
    foreach($entry in @{
        Version=$Plan.Version
        AssemblyVersion=$Plan.NumericVersion
        FileVersion=$Plan.NumericVersion
        InformationalVersion=$Plan.Version
        LastInstallerBuildDate=$Plan.BuildDate
    }.GetEnumerator()){
        $node=$props.SelectSingleNode('/Project/PropertyGroup/'+$entry.Key)
        if(-not $node){$node=$props.CreateElement($entry.Key);[void]$group.AppendChild($node)}
        $node.InnerText=$entry.Value
    }
    $documents=@(
        @{Path=(Join-Path $ProjectRoot 'README.md');Kind='readme'},
        @{Path=(Join-Path $ProjectRoot 'docs\Gitstar使用说明.md');Kind='guide'},
        @{Path=(Join-Path $ProjectRoot 'CHANGELOG.md');Kind='changelog'},
        @{Path=(Join-Path $ProjectRoot 'windows\installer\Gitstar.iss');Kind='installer'}
    )
    # Validate all labels first; only current labels change, historical records stay intact.
    foreach($document in $documents){$document.Text=Set-ReleaseDocumentVersion -Text ([IO.File]::ReadAllText($document.Path)) -Kind $document.Kind -Version $Plan.Version}
    foreach($document in $documents){Write-ReleaseText -Path $document.Path -Text $document.Text}
    # Commit date and all version properties together after installer generation and delivery.
    Write-ReleaseText -Path $Plan.PropsPath -Text ($props.OuterXml+[Environment]::NewLine)
}
