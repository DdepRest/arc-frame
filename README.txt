MosquitoNetCalculator
====================================================

Desktop application (C# WPF, .NET 8.0) for calculating
mosquito net orders and generating A4 print-ready
commercial offers / contracts.

----------
BUILD
----------

Method 1 - Using build.bat:
  Run build.bat (requires .NET 8.0 SDK)

Method 2 - Manual:
  dotnet publish MosquitoNetCalculator/MosquitoNetCalculator.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true

Output: publish/MosquitoNetCalculator.exe

----------
DEPENDENCY CHECKER (check-deps)
----------

Two utility scripts are bundled with the installer (in publish/ at build time,
in {app} after installation on the user's machine) to help verify and set up
system dependencies:

  check-deps.bat   — Windows wrapper (runs the PowerShell script)
  check-deps.ps1   — PowerShell 5.0+ script (built into Windows 10/11)

Usage:
  check-deps.bat              — show dependency status report
  check-deps.bat -Install     — check and automatically install missing components
                                 (requires administrator; uses winget or direct download)
  check-deps.bat -Quiet       — quiet mode (only errors)
  check-deps.bat -Json        — JSON output (for CI/CD pipelines)

The scripts check:
  - Windows version (Build >= 17763 = Windows 10 1809 / 11)
  - WebView2 Runtime             (>= 100.0.0.0) — needed for KP preview
  - VC++ Redistributable 2015-2022 (>= 14.30, x64)
  - .NET 8 Desktop Runtime (informational only — the application is self-contained)

Recommended: run `check-deps.bat -Install` ONCE on a clean Windows machine to
install WebView2 Runtime and VC++ Redistributable. The installer does the
same automatically when you run SetupMosquitoNetCalculator-3.26.0.exe.

----------
INSTALLER
----------

To build the installer (.exe), use Inno Setup 6.0 or newer.
See README_installer.txt for details.

----------
VISUAL STUDIO
----------

1. Open MosquitoNetCalculator.sln
2. Press F5 to run with debugging

----------
PROJECT STRUCTURE
----------

MosquitoNetCalculator/
├── build.bat                            — build script (also copies deps-checker to publish/)
├── check-deps.bat                       — Windows wrapper for dependency checker
├── check-deps.ps1                       — PowerShell dependency checker script
├── compile-installer.bat                — convenience wrapper around ISCC.exe
├── installer.iss                        — Inno Setup installer script
├── MosquitoNetCalculator.sln            — solution file
└── MosquitoNetCalculator/
    ├── MosquitoNetCalculator.csproj     — project (.NET 8, WPF)
    ├── App.xaml / App.xaml.cs           — entry point, styles, error handling
    ├── MainWindow.xaml                  — main window (3 tabs, sidebar)
    ├── MainWindow.xaml.cs               — calculation logic, buttons
    ├── Converters/
    │   └── Converters.cs                — value converters (DimensionConverter)
    ├── Models/
    │   ├── OrderItem.cs                 — order line item (calculation, notifications)
    │   ├── PriceItem.cs                 — price list entry
    │   ├── ClientInfo.cs                — client data (including Notes, AdditionalKp)
    │   └── OrderData.cs                 — order data for storage
    ├── Services/
    │   ├── PriceService.cs              — load/save prices.json
    │   ├── PrintService.cs              — HTML generation and KP printing
    │   ├── OrderStorageService.cs       — save/load orders
    │   └── AmountInWordsService.cs      — amount in words
    ├── Resources/
    │   └── print_template.html          — KP template (HTML+CSS A4)
    └── prices.json                      — price list

----------
PRODUCTS AND PRICING
----------

1) Otliv / Kozyryok / Korob:
   - Belyy / Korichnevyy / Antratsit: 2150 RUB/m2
   - Zolotoy dub: 2650 RUB/m2
   Calculation: Otliv = Height / 1000 m.p.; Kozyryok / Korob = W * H / 1000000 m2

2) Okonnaya na metallicheskikh karmanakh:
   - All colors: 3200 RUB/m2
   Calculation: W * H / 1000000 m2

Colors: Belyy, Korichnevyy, Antratsit, Zolotoy dub

----------
PRINT FORM (KP / Contract)
----------

- A4 HTML template with SVG engineering drawings
- Customer block, items table, totals, conditions, signatures
- "Ispolnitel" / "Zakazchik" signature block
- Conditional: Notes block, Additional KP reference
- Conditions: offer validity, payment terms, materials included
