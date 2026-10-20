# Build the single-file Windows exe into .\publish
Set-Location $PSScriptRoot
dotnet publish src/AndroidTVDebloater -c Release -r win-x64 -o publish
Write-Host "Built publish\UnbloatMyTV.exe"
