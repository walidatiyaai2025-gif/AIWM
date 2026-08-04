AI WordPress Manager Setup EXE project

This folder now contains a Windows executable setup project, not a batch or PowerShell launcher.

Build once on Windows with the .NET 8 SDK:
  dotnet publish .\Setup\AIWordPressManager.Setup.csproj -c Release -r win-x64 --self-contained true -o .\Setup

Generated executable:
  Setup\AIWordPressManager.Setup.exe

Run that EXE to publish the self-contained application into:
  Setup\Published\AIWordPressManager.Desktop.exe
