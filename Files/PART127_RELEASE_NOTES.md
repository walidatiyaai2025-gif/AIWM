# Part 127 — Current Site Compile Fix and WebView2 Recovery

## Fixed
- Removed the remaining legacy `_sites` reference from `HealthCenterViewModel`.
- Health Center now obtains both the selected site ID and display name from `ICurrentSiteContext`.
- This resolves `CS0103: The name '_sites' does not exist in the current context`.

## WebView2 validation
- Added `Build/Repair-WebView2-And-Build.ps1`.
- Deletes stale `bin` and `obj` folders, clears NuGet temporary caches, restores the full solution, verifies the WebView2 package, builds the solution, and checks for the Edge WebView2 Runtime.
- The two `MC3074` entries shown by Visual Studio are design-time warnings that normally disappear after a successful package restore. The new script validates the actual package and build rather than relying on stale IntelliSense state.

## Guided-flow contract gate
- Added `Build/Validate-GuidedFlowContracts.ps1`.
- Blocks new feature ViewModels from depending directly on `SitesViewModel` or the removed `_sites` field.
- All guided SEO workflow screens must obtain the active website from `ICurrentSiteContext`.
