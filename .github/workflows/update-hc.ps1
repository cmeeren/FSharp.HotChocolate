$propsFile = "Directory.Packages.props"

[xml]$xml = Get-Content $propsFile

function Get-PackageVersions {
    param (
        [string]$PackageName
    )
    $nugetUrl = "https://api.nuget.org/v3/index.json"
    $nugetIndex = Invoke-RestMethod -Uri $nugetUrl -UseBasicParsing
    $packageBaseAddress = ($nugetIndex.resources | Where-Object { $_.'@type' -eq 'PackageBaseAddress/3.0.0' }).'@id'
    $packageVersionsUrl = "$packageBaseAddress$($PackageName.ToLowerInvariant())/index.json"
    $versionsResult = Invoke-RestMethod -Uri $packageVersionsUrl -UseBasicParsing

    return $versionsResult.versions
}

foreach ($itemGroup in $xml.Project.ItemGroup) {
    $label = $itemGroup.Label

    if ($label -eq "HC_Pre") {
        $includePrerelease = $true
        Write-Host "Updating packages in ItemGroup with Label 'HC_Pre' to latest prerelease versions..."
    } elseif ($label -eq "HC_Stable") {
        $includePrerelease = $false
        Write-Host "Updating packages in ItemGroup with Label 'HC_Stable' to latest stable versions..."
    } else {
        continue
    }

    $packageVersionNodes = $itemGroup.PackageVersion | Where-Object { $_.Include -like "HotChocolate*" }
    foreach ($pkgNode in $packageVersionNodes) {
        $packageName = $pkgNode.Include
        $currentVersion = $pkgNode.Version
        try {
            $allVersions = Get-PackageVersions -PackageName $packageName


            $currentIndex = $allVersions.IndexOf($currentVersion)

            if ($currentIndex -eq -1) {
                Write-Warning "Current version $currentVersion of $packageName not found in available versions."
                $currentIndex = 0
            }

            if ($includePrerelease) {
                $filteredVersions = $allVersions
            } else {
                $filteredVersions = $allVersions | Where-Object { $_ -notmatch '-' }
            }

            if ($filteredVersions.Count -eq 0) {
                Write-Warning "No appropriate versions found for package $packageName"
                continue
            }

            $latestVersion = $filteredVersions[-1]
            $latestIndex = $allVersions.IndexOf($latestVersion)

            if ($latestIndex -gt $currentIndex) {
                Write-Host "Updating $packageName from version $currentVersion to version $latestVersion"
                $pkgNode.SetAttribute('Version', $latestVersion)
            } else {
                Write-Host "$packageName is up to date with version $currentVersion"
            }
        } catch {
            Write-Warning $_.Exception.Message
        }
    }
}

$xml.Save($propsFile)
