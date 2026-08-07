# Release Candidate Validation Checklist

Use this checklist for every AI WordPress Manager 2.3.x release candidate. Do not mark an item complete without retaining the referenced evidence.

## Candidate identity

- [ ] Candidate version is recorded.
- [ ] Candidate commit SHA is immutable and recorded.
- [ ] Source branch is `main`.
- [ ] Setup package SHA-256 is recorded.
- [ ] Desktop executable version and SHA-256 match the Windows acceptance artifact.

## Automated gates

- [ ] PR Fast Validation passed.
- [ ] Stability Build passed.
- [ ] Windows Desktop Build passed.
- [ ] Verify Windows Solution passed.
- [ ] Windows acceptance JSON and Markdown artifacts were uploaded and reviewed.
- [ ] Release restore/build and non-UI tests passed.
- [ ] Startup and login-window smoke test passed.

## Clean Windows installation

- [ ] Test machine and Windows version are recorded.
- [ ] No previous AI WordPress Manager installation or application data remains.
- [ ] Setup installs without warnings or missing prerequisites.
- [ ] Application launches, signs in, and exits cleanly.
- [ ] SQLite database initializes successfully.
- [ ] Upgrade from the previous supported 2.3.x build preserves data.
- [ ] Uninstall behavior is verified.

## Disposable WordPress journey

- [ ] Test site URL and non-production ownership are recorded.
- [ ] Dashboard loads the selected site.
- [ ] Sites and WordPress Explorer synchronize successfully.
- [ ] SEO Audit produces current results.
- [ ] Suggested Changes creates reviewable proposals.
- [ ] Approval Queue accepts and rejects proposals correctly.
- [ ] Execution Center applies an approved change to the intended site.
- [ ] Evidence Center exposes the resulting receipt and evidence.

## Execution and recovery

- [ ] Successful execution produces before/after evidence and a terminal receipt.
- [ ] Failed execution records the error and recovery guidance.
- [ ] Cancelled execution records a terminal cancelled state.
- [ ] Partially failed execution identifies completed and failed items.
- [ ] Duplicate execution is prevented or clearly reported.
- [ ] Supported rollback restores the expected WordPress state.
- [ ] Unsupported rollback is clearly communicated without claiming success.
- [ ] Receipts remain available after application restart.

## Support and privacy

- [ ] Support bundle is generated successfully.
- [ ] Bundle SHA-256 verification succeeds.
- [ ] Representative credentials and secrets are absent or redacted.
- [ ] Manifest build version, branch, and commit match the candidate.
- [ ] Bundle contents are manually reviewed before external sharing.

## Localization and presentation

- [ ] Complete first journey is reviewed in English LTR.
- [ ] Complete first journey is reviewed in Arabic RTL.
- [ ] No clipped, untranslated, or unreadable text remains.
- [ ] Keyboard focus and navigation are usable.
- [ ] Dark/light theme contrast is acceptable.

## Release decision

- [ ] No critical startup, credential, data-loss, site-identity, or execution defect remains.
- [ ] All exceptions are linked to an issue with owner and disposition.
- [ ] Acceptance artifacts and operator notes are retained.
- [ ] Release notes, upgrade notes, and rollback instructions are ready.
- [ ] Release owner approves publishing the candidate.

## Sign-off

- Candidate version:
- Commit SHA:
- Setup SHA-256:
- Test machine:
- Disposable WordPress site:
- Operator:
- Validation date (UTC):
- Decision: Approved / Rejected
- Notes:
