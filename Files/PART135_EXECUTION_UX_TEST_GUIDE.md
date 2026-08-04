# Part 135 Execution UX Test Guide

1. Select a site and open **AI & Automation → Execution Center**.
2. Confirm that loading the queue displays the full-window operation overlay.
3. Select pending items and press **Approve selected**.
4. Confirm the Ribbon and site selector remain locked until approval and refresh finish.
5. Select findings that need values and press **Prepare selected**.
6. Verify the loader describes value preparation and closes only after the queue reloads.
7. Execute one supported low-risk action.
8. Confirm the overlay remains visible through backup, WordPress write, verification, and evidence capture.
9. Test rollback on an executed row and verify the same protected transaction behavior.
10. Verify failure messages remain visible in Execution Center and API Logs.
