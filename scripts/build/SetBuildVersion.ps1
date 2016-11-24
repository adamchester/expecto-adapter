Param(
  [string]$BuildCounter,
  [string]$AssemblyInfoPath
)

$majorMinorVersionNumber = "1.0"
$thirdVersionPart = "4"
$basicVersion = $majorMinorVersionNumber + ".0.0"
$fullVersionNumber = $majorMinorVersionNumber + "." + $thirdVersionPart + "." + $BuildCounter

Write-Host ("##teamcity[buildNumber '" + $fullVersionNumber + "']")

$CommonVersionsCsFile = Get-Item $AssemblyInfoPath
Write-Output "Updating common version file at $CommonVersionsCsFile"
$CommonVersionsCsContent = Get-Content $CommonVersionsCsFile -Encoding UTF8
$AssemblyFileVersionRegex = 'AssemblyFileVersion\(\"(?<p1>\d+)\.(?<p2>\d+)\.(?<p3>\d+)\.(?<p4>\d+)\"'
$FileVersionReplacement = "AssemblyFileVersion(""$fullVersionNumber"""
$AssemblyVersionRegex = 'AssemblyVersion\(\"(?<p1>\d+)\.(?<p2>\d+)\.(?<p3>\d+)\.(?<p4>\d+)\"'
$AssemblyVersionReplacement = "AssemblyVersion(""$fullVersionNumber"""
($CommonVersionsCsContent -replace $AssemblyFileVersionRegex, $FileVersionReplacement) `
 -replace $AssemblyVersionRegex, $AssemblyVersionReplacement `
 | Out-File $CommonVersionsCsFile -Encoding UTF8



# Set version in .nupkg file
$nuspecs = Get-ChildItem src -Recurse -Include *.nuspec
foreach ($nuspectFile in $nuspecs)
{
    Write-Output "Updating $nuspectFile"
    [xml] $nuspec = Get-Content $nuspectFile
    $nuspec.package.metadata.version = $fullVersionNumber
    $nuspec.Save($nuspectFile)
}

$vsixs =  Get-ChildItem src -Recurse -Include *.vsixmanifest
foreach ($vsixFile in $vsixs)
{
    Write-Output "Updating $vsixFile"
    [xml] $vsixmanifest = Get-Content $vsixFile
    $vsixmanifest.PackageManifest.Metadata.Identity.Version = $fullVersionNumber
    $vsixmanifest.Save($vsixFile)
}