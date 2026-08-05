# Definition of Done

A task is complete only when all applicable items pass:

1. Code is committed with stable project/file names and no secrets.
2. `dotnet restore` succeeds.
3. `dotnet build AIWordPressManager.sln` succeeds with zero errors.
4. Relevant unit/integration tests pass; missing tests require a documented reason.
5. UI work is checked at supported window sizes, Arabic RTL, English LTR, light and dark themes.
6. No persistent overlay blocks content; dialogs are user-initiated or required confirmations.
7. Offline-first behavior is preserved and remote writes are explicit.
8. Errors include a copyable message and correlation identifier.
9. WordPress writes include risk classification, backup requirement, verification, and evidence where applicable.
10. Documentation and the execution tracker are updated with completion date and evidence.
11. Acceptance criteria are demonstrated from the built application, not inferred from code presence.
12. The main branch remains buildable.
