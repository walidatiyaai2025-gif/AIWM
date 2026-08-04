# Part 113 — Autopilot Compile Fix and Policy Storage Hardening

## Fixed
- Added `using System.IO;` to `AutopilotOrchestratorViewModel.cs` so `Path`, `Directory`, and `File` resolve correctly.
- Assigned the injected `IApplicationPathService` to `_paths` in the constructor.
- Added a null guard for the path service to prevent a later runtime failure when loading or saving site policy files.

## Result
The six CS0103 errors and the CS0649 warning shown in Visual Studio are addressed.

## Validation note
The source was checked directly. A full `dotnet build` could not be run in the packaging environment because the .NET SDK is not installed there.
