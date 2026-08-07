# AI WordPress Manager

AI WordPress Manager is a .NET 8 WPF desktop application for managing WordPress sites through an offline-first workflow with SQLite persistence.

Current desktop version: **2.3.1**

## First controlled journey

The current baseline guides operators through:

1. Dashboard
2. Sites
3. WordPress Explorer
4. SEO Audit
5. Suggested Changes
6. Approval Queue
7. Execution Center
8. Evidence Center

Terminal executions generate HTML and JSON receipts. The application also exposes build identity and creates sanitized support bundles containing diagnostics, recent logs, receipts, and SHA-256 integrity entries.

## Build

```powershell
dotnet restore .\AIWordPressManager.sln
dotnet build .\AIWordPressManager.sln -c Debug --no-restore
dotnet test .\AIWordPressManager.sln -c Debug --no-build
```

For the repository-managed Windows update, build, and launch workflow:

```powershell
.\Build-And-Run.bat
```

The script targets the latest `main` branch and embeds its branch and commit identity in the desktop build.

## Project documentation

- [Current status](docs/STATUS.md)
- [Roadmap](docs/ROADMAP.md)
- Release and historical implementation notes remain under the `Files` directory.

## Contribution workflow

- Start from the latest successful `main`.
- Work on a focused feature or fix branch.
- Open a Draft pull request.
- Keep it Draft until required contract validation, build, test, and startup checks pass.
- Do not treat text-token contract validation as a replacement for Windows runtime acceptance.
