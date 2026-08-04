# Part 77 — Theme Refinement, Credential Recovery Guard, Auto-load Editor

- Rebalanced Light and Dark themes with neutral professional surfaces and two-color accents.
- Removed hard-coded button/grid colors so palette changes remain readable in both modes.
- Persisted Light/Dark mode and palette independently.
- Prevented DPAPI credential failures from crashing the WPF dispatcher.
- Added a recovery message instructing the user to re-save the WordPress application password.
- Post & Page Editor now loads live fields automatically when a row is selected, with cancellation/debounce to avoid stale requests.
- Retained the manual reload command as a fallback.
