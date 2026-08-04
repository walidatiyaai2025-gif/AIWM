# Part 121 — Compile Recovery and Contract Validation

## Fixed
- Replaced obsolete `IDialogService.ShowMessageAsync` calls with `ShowInformationAsync`.
- Replaced obsolete `IApplicationPathService.LocalDataDirectory` usage with `GetApplicationDataDirectory()`.
- Corrected `Result.Error` handling to use `Error.Message`.
- Added the missing `System.IO` import to Release Readiness.
- Corrected the Operations Center `AsyncRelayCommand` construction by using a parameterless lambda.

## Prevention
Added `Build/Validate-DesktopContracts.ps1`. It blocks release validation when source code:
- uses `File`, `Path`, `Directory`, `DirectoryInfo`, or `SearchOption` without `using System.IO;`;
- calls obsolete dialog or path APIs;
- treats structured `Result.Error` objects as strings;
- binds methods with optional parameters directly to `AsyncRelayCommand`.

The Enterprise Release validator now runs this check before restore/build.
