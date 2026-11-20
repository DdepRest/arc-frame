$dst = "C:\Users\Asus\Desktop\ARC~1.FRA\gwga\TMP_CC~1\CCALC_~1.MDB"
Remove-Item "C:\Users\Asus\Desktop\ARC~1.FRA\gwga\TMP_CC~1\*.ldb" -Force -ErrorAction SilentlyContinue
$connStr = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + $dst + ";"
Write-Host "CONN: $connStr"
$conn = New-Object System.Data.OleDb.OleDbConnection($connStr)
try {
    $conn.Open()
    Write-Host "OPENED OK, state=$($conn.State)"
    $schema = $conn.GetOleDbSchemaTable([System.Data.OleDb.OleDbSchemaGuid]::Tables, @($null,$null,$null,"TABLE"))
    Write-Host "=== TABLES ==="
    $schema | Select-Object TABLE_NAME | Format-Table -AutoSize
    $conn.Close()
} catch {
    Write-Host "ERR: $($_.Exception.Message)"
}
Write-Host "DONE"