$paths = @(
  'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
  'C:\Program Files\Inno Setup 6\ISCC.exe',
  'C:\InnoSetup\ISCC.exe',
  'C:\Users\Asus\AppData\Local\Programs\Inno Setup 6\ISCC.exe',
  'C:\Users\Asus\AppData\Local\Programs\InnoSetup\ISCC.exe'
)
foreach ($p in $paths) {
  if (Test-Path $p) { Write-Host ("FOUND: " + $p) }
  else { Write-Host ("NOT:    " + $p) }
}
Write-Host "---LOCAL PROGRAMS DIRS---"
Get-ChildItem 'C:\Users\Asus\AppData\Local\Programs' -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name | ForEach-Object { Write-Host $_ }
