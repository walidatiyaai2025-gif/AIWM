# Part 68 - Tabbed Settings, Job Failure Pause, Setup EXE

- Rebuilt Settings as tabs: Synchronization, AI Providers, Jobs & Reliability, Performance, Safety.
- Added a per-site/per-job-type circuit breaker. After configurable consecutive failures, new runs pause for a configurable duration.
- Added automatic resume setting.
- Added a Windows Setup executable project in Setup (AIWordPressManager.Setup.csproj). Building/publishing it produces AIWordPressManager.Setup.exe; no batch or PowerShell launcher is used.
- Existing release files remain under Files.
