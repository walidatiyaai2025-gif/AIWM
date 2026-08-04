# Part 119 — Release Readiness Test Guide

1. Open **SYSTEM → Release**.
2. Press **Validate now**.
3. Resolve every item marked **Fail** before publishing.
4. Review warnings, especially missing Setup EXE, missing documentation, or unresolved XAML keys.
5. Press **Export report** to create an auditable Markdown snapshot.
6. From PowerShell at the project root run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build\Validate-EnterpriseRelease.ps1 -Configuration Release
```

The PowerShell validation performs the authoritative restore, build, tests, Bridge validation, and Setup build.
