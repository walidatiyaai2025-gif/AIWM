# Part 16 Compile Fix

Fixed `CS1061` in `SuggestedChangeService.cs`.

`ToHashSetAsync()` was replaced with an EF Core-supported query using `ToListAsync()` followed by an in-memory `ToHashSet(StringComparer.Ordinal)`.

No database migration is required for this correction.
