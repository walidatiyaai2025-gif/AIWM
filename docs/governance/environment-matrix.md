# Environment Matrix

| Environment | Purpose | OS | Runtime/SDK | Database | Configuration | Data |
|---|---|---|---|---|---|---|
| Developer | Local coding and debugging | Windows 10/11 | .NET 8 SDK, Visual Studio 2022 | Local SQLite | Development settings; user secrets/environment variables | Synthetic or authorized test data |
| CI | Restore, build, tests, package validation | Windows runner | Pinned .NET 8 SDK | Temporary SQLite | CI environment variables | Generated fixtures |
| Test/UAT | Functional and user acceptance | Supported Windows workstation | Published .NET 8 desktop runtime | Isolated SQLite | UAT provider/site endpoints | Authorized non-production sites |
| Production | End-user operation | Supported Windows workstation | Versioned signed release | Per-user protected SQLite | Production settings and protected secrets | Authorized production WordPress sites |

## Tool controls
- SDK and package versions are centrally pinned.
- Production credentials are never committed.
- Database and application-data locations are resolved by `IApplicationPathService`.
- Every environment has separate logs, backups, and WordPress application passwords.
- Tests must never write to production WordPress sites.
