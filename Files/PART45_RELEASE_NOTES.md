# Part 45 — Self-healing Playwright Browser Setup

- Added an **Install browser** action to Visual Inspector.
- When Chromium is missing, the application asks to install it instead of showing a dead-end error.
- Installation runs through the generated `playwright.ps1` script and supports PowerShell 7 or Windows PowerShell.
- Progress is connected to the global status bar.
- Installation output and failures are shown through the application dialog service.
- Visual inspection can be run immediately after the one-time installation.
