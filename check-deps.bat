@echo off
REM ============================================================
REM  MosquitoNetCalculator - Dependency Check
REM ============================================================
REM  Self-contained script (embedded PowerShell, no temp files).
REM
REM  Usage:
REM     check-deps.bat                  - check and show report
REM     check-deps.bat -Install          - check and install missing
REM     check-deps.bat -Quiet            - no info messages
REM     check-deps.bat -Json             - JSON output
REM
REM  Requires PowerShell 5.0+ (built into Windows 10/11).
REM ============================================================

setlocal

powershell -NoProfile -ExecutionPolicy Bypass -Command "$P=@{}; $a=@($args); if($a-contains'-Install'){$P.Install=$true}; if($a-contains'-Quiet'){$P.Quiet=$true}; if($a-contains'-Json'){$P.Json=$true}; function cv($A,$B){if([string]::IsNullOrWhiteSpace($A)){$A='0'}; if([string]::IsNullOrWhiteSpace($B)){$B='0'}; $A=$A.Replace(',','.'); $B=$B.Replace(',','.'); $a=@($A-split'\.'-ne''); $b=@($B-split'\.'-ne''); $an=[int[]]$a; $bn=[int[]]$b; $l=[Math]::Max($an.Count,$bn.Count); for($i=0;$i-lt$l;$i++){$na=if($i-lt$an.Count){$an[$i]}else{0}; $nb=if($i-lt$bn.Count){$bn[$i]}else{0}; if($na-gt$nb){return 1}; if($na-lt$nb){return -1}}; return 0}; function rv($P,$N){try{return(Get-ItemProperty -Path $P -EA Stop).$N}catch{return $null}}; $e=$false; $r=@{}; $w=rv 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' ''; if($w){$b=[int]$w.CurrentBuild}else{$b=0}; $r.Windows=@{ProductName=$w.ProductName;Build=$b;MinBuild=17763;Supported=($b-ge17763)}; if(-not$P.Quiet-and-not$P.Json){Write-Host 'Windows...' -Foreground Cyan}; if($r.Windows.Supported){Write-Host '  [OK]   Windows Build '$b -Foreground Green}else{Write-Host '  [X]    Windows Build '$b' (requires 17763+)' -Foreground Red;$e=$true}; $k='HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4F95-ADA8-00C4A42566F8}'; $v=rv $k 'pv'; $ok=$v -and (cv $v '100.0.0.0')-ge0; $r.WebView2=@{Installed=($v-ne$null);Version=$v;MinVersion='100.0.0.0';Ok=$ok}; if(-not$P.Quiet-and-not$P.Json){Write-Host 'WebView2 Runtime...' -Foreground Cyan}; if($ok){Write-Host '  [OK]   WebView2 Runtime: '$v -Foreground Green}else{Write-Host '  [X]    WebView2 Runtime: '+(if($v){'version '+$v+' outdated'}else{'not installed'}) -Foreground Red;$e=$true}; $k='HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64'; $v=rv $k 'Version'; $i=rv $k 'Installed'; $ok=($i-eq1)-and$v-and(cv $v '14.30.0.0')-ge0; $r.VCRedist=@{Installed=($i-eq1);Version=$v;MinVersion='14.30.0.0';Ok=$ok}; if(-not$P.Quiet-and-not$P.Json){Write-Host 'VC++ Redistributable 2015-2022 (x64)...' -Foreground Cyan}; if($ok){Write-Host '  [OK]   VC++ Redistributable: '$v -Foreground Green}else{Write-Host '  [X]    VC++ Redistributable: '+(if($v){'version '+$v+' outdated'}else{'not installed'}) -Foreground Red;$e=$true}; $r.AllOk=(-not$e); if($P.Json){ConvertTo-Json $r -Depth 3; exit}; Write-Host ''; if($r.AllOk){Write-Host '  SYSTEM READY' -Foreground Green; exit 0}else{Write-Host '  SYSTEM NOT READY' -Foreground Yellow}; if($P.Install-and-not$r.AllOk-and$r.Windows.Supported){Write-Host ''; Write-Host '  Auto-install not available in this version.' -Foreground Yellow; Write-Host '  Install manually:' -Foreground Yellow; if(-not$r.WebView2.Ok){Write-Host '    WebView2: https://go.microsoft.com/fwlink/p/?LinkId=2124703'}; if(-not$r.VCRedist.Ok){Write-Host '    VC++ Redist: https://aka.ms/vs/17/release/vc_redist.x64.exe'}; exit 2}; if($r.AllOk){exit 0}else{exit 1}" %*

exit /b %errorlevel%
