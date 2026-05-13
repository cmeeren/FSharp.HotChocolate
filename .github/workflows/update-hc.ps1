$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
$propsFile = Join-Path $repoRoot "Directory.Packages.props"

[xml]$xml = Get-Content $propsFile

$versionNode = $xml.SelectSingleNode("/Project/PropertyGroup/HotChocolateVersion")

if ($null -eq $versionNode) {
    throw "Could not find HotChocolateVersion in $propsFile"
}

$currentVersion = $versionNode.InnerText

try {
    $versionsUrl = "https://api.nuget.org/v3-flatcontainer/hotchocolate/index.json"
    $stableVersions = @((Invoke-RestMethod -Uri $versionsUrl -UseBasicParsing).versions | Where-Object { $_ -notmatch '-' })

    if ($stableVersions.Count -eq 0) {
        throw "No stable HotChocolate versions found"
    }

    $latestVersion = $stableVersions[-1]

    if ($latestVersion -ne $currentVersion) {
        Write-Host "Updating HotChocolate packages from version $currentVersion to version $latestVersion"
        $versionNode.InnerText = $latestVersion
    } else {
        Write-Host "HotChocolate packages are up to date with version $currentVersion"
    }
} catch {
    Write-Error $_.Exception.Message
    exit 1
}

$xml.Save($propsFile)
