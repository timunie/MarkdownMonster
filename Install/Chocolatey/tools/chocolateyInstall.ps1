$packageName = 'markdownmonster'
$fileType = 'exe'
$url = 'https://github.com/RickStrahl/MarkdownMonsterReleases/raw/master/v1.19/MarkdownMonsterSetup-1.19.6.exe'

$silentArgs = '/VERYSILENT'
$validExitCodes = @(0)

Install-ChocolateyPackage "packageName" "$fileType" "$silentArgs" "$url"  -validExitCodes  $validExitCodes  -checksum "E08D2670AC3AED02D0E14A3C3E85EF6869B293988AACBD9ADB607CC4D5604400" -checksumType "sha256"
