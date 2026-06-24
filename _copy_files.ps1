Copy-Item -Path "$PSScriptRoot\MosquitoNetCalculator\prices.json" -Destination "$PSScriptRoot\publish\prices.json"
Copy-Item -Path "$PSScriptRoot\MosquitoNetCalculator\Resources\app_icon.ico" -Destination "$PSScriptRoot\publish\app_icon.ico"
Copy-Item -Path "$PSScriptRoot\check-deps.bat" -Destination "$PSScriptRoot\publish\check-deps.bat"
Write-Host "Files copied"
