# Build validation

Run this before accepting any new release:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build\Validate-And-Build.ps1 -Configuration Debug
```

For the distributable build:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build\Validate-And-Build.ps1 -Configuration Release
```

The script cleans generated folders, restores packages, compiles the entire solution, runs tests, and stops immediately on the first failure. A release must not be distributed unless this command finishes successfully.
