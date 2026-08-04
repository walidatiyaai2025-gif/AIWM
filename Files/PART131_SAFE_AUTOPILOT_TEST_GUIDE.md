# Safe Autopilot Test Guide

1. Build and run the application.
2. Select a WordPress site from `Sites`.
3. Open `Settings > AI Automation` and confirm:
   - Before/after evidence is enabled.
   - Verified execution results are required.
   - High-risk actions are automatically rejected.
4. Return to `Dashboard > SEO Journey`.
5. If the site is not analyzed, run `Start optimization` first.
6. Confirm the Safe Autopilot readiness message is `READY`.
7. Click `Run safe plan`.
8. Confirm the safety dialog.
9. Observe the live stages:
   - Refresh AI actions.
   - Load execution queue.
   - Approve low-risk actions.
   - Prepare supported adapters.
   - Build verified plan.
   - Execute safe WordPress plan.
   - Refresh evidence and transactions.
10. Open:
    - `Execution Center` to review item states.
    - `API Logs` to inspect WordPress responses.
    - `Evidence Center` for before/after artifacts.
    - `Transaction Center` for committed or failed transactions.
11. Click `Open last receipt` on the Dashboard and review the generated Markdown run receipt.

Expected result: only low-risk supported actions enter execution. High-risk, unsupported, incomplete, and staging-required actions remain blocked.
