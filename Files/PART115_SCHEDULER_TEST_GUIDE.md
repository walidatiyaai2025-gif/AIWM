# Scheduler Test Guide

1. Select a connected site.
2. Open SYSTEM → Scheduler.
3. Add an SEO audit task with a time one or two minutes ahead.
4. Confirm it appears as Enabled and the Next Run card updates.
5. Keep the application open and verify the task changes to Running then Completed.
6. Use Run selected now to test immediately.
7. Restart the application and confirm the schedule reloads for the same site.
8. Switch sites and confirm each site has an independent schedule file.
9. Pause a task and verify it is not executed when due.
10. Review Jobs and API Logs for operations that communicate with WordPress.
