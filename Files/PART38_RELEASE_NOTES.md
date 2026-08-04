# Part 38 — Working Progress Bar, Darker Gold, Arabic UI, Visual Inspector Foundation

## Changes
- Added a shared `UiOperationService` for visible application-wide progress.
- AI Studio now reports provider loading, request preparation, generation, completion, and failure to the footer progress bar.
- Added percentage, stage, and detail text for AI Studio operations.
- Added Arabic dynamic resources for the AI Studio workspace and navigation group headers.
- Language switching now refreshes page titles and descriptions in Arabic or English.
- Darkened gold accents in Light Mode for better readability on white backgrounds.
- Added a new `Visual Inspector` navigation entry and prepared its page metadata/workspace shell.
- Kept the original startup/offline loading progress bar intact.

## Validation
- App.xaml and MainWindow.xaml parsed successfully as XML.
- .NET SDK is not installed in the packaging environment, so build/test must be run on the user's Windows machine.
