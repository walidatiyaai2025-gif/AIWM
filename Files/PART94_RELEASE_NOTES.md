# Part 94 — Office Ribbon XAML Compile Fix

## Fixed
- Replaced the invalid `TextElement.TextAlignment` attached property in `RibbonLargeButtonStyle` with the valid WPF `TextBlock.TextAlignment` attached property.
- Resolves `MC3072` in `Themes/Theme.xaml` at the Office Ribbon large-button template.

## Validation
- Theme.xaml parsed successfully as XML.
- No other occurrences of `TextElement.TextAlignment` remain in the Desktop project.
