Param(
  [string]$Tag,
  [string]$Auth
)

$apiBaseUrl = "https://api.github.com"
$base64Token = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Auth))

$createReleaseUrl = $apiBaseUrl + "/repos/interactsw/fuchu-adapter/releases"

$body = @{
  tag_name = $Tag
  target_commitish = "master"
  name = $Tag
  body = ("Release " + $Tag)
  draft = $true
  prerelease = $false
}

$headers = @{ Authorization = ("Basic " + $base64Token) }

$result = Invoke-RestMethod -Uri $createReleaseUrl -Method Post -Headers $headers -Body (ConvertTo-Json $body)
$assetsUrl = $result.upload_url.Replace("{?name,label}","")

$pdbs = Get-ChildItem Pdbs -r -i *.pdb
$headers["Content-Type"]="application/octet-stream"
foreach ($pdb in $pdbs)
{
    $pdbAssetUrl = $assetsUrl + "?name=" + $pdb.Name
    echo $pdbAssetUrl
    $fileContent = [System.IO.File]::ReadAllBytes($pdb.FullName)
    Invoke-RestMethod -Uri $pdbAssetUrl -Method Post -Headers $headers -Body $fileContent
}

$analyzers = Get-ChildItem Analyzers -r -i *.nupkg
$headers["Content-Type"]="application/zip"
foreach ($pkg in $analyzers)
{
    $uploadAssetUrl = $assetsUrl + "?name=" + $pkg.Name
    echo $uploadAssetUrl
    $fileContent = [System.IO.File]::ReadAllBytes($pkg.FullName)
    Invoke-RestMethod -Uri $uploadAssetUrl -Method Post -Headers $headers -Body $fileContent
}
