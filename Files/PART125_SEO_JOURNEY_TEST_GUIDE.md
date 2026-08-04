# SEO Journey test guide

1. Extract the release into a new folder.
2. Open PowerShell in the project root.
3. Run:
   `powershell -ExecutionPolicy Bypass -File .\Build\Repair-NuGet-And-Build.ps1 -Configuration Debug`
4. Start `AIWordPressManager.Desktop`.
5. Select a connected WordPress site.
6. Open Dashboard → SEO Journey.
7. Before the first audit, confirm the score shows `0 / 100` and `NOT ANALYZED`.
8. Select `Start optimization` and run the SEO audit, content audit, and link scan.
9. Return to Dashboard and confirm the weighted score and stage 1 become completed.
10. Generate suggestions and confirm stages 2 and 3 update.
11. Approve a supported action and confirm stage 4 updates.
12. Execute it through Execution Center and verify WordPress response logs/evidence.
13. Return to Dashboard and confirm the execution and verification stages update.
