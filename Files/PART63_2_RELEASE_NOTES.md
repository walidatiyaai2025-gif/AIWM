# Part 63.2 — Memory Cooling Storyboard Fix

- Fixed startup failure: `A Storyboard tree in a Style cannot specify a TargetName`.
- Removed named targets from the shared storyboard used by a Style trigger.
- The cooling badge now animates its own opacity through a targetless storyboard.
- The snowflake icon uses a separate self-targeted transform animation.
- Added clean StopStoryboard actions when memory cooling mode ends.
- Retains all Part 63.1 fixes and memory cooling behavior.
