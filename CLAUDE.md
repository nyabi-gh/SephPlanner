# Project Guidelines

- **Root-Cause Solutions**: Avoid workarounds and temporary patches. Always identify and resolve the underlying root cause of bugs and issues.
- **Minimal Comments**: Do not write redundant or obvious comments. Include only strictly necessary comments where the intent cannot be inferred from the code itself.
- **No Commit Trailers**: Do not include Git trailers (e.g., `Co-authored-by`, `Signed-off-by`) in commit messages.
- **Verify, Don't Assume**: Game rules come from decompiling `Sephiria_Data/Managed/Assembly-CSharp.dll` (`ilspycmd` is installed), never from guesswork or another tool's behavior. Measure before claiming a performance or quality change. When a number cannot be measured, use it anyway but say in the code that it is an estimate.
- **Language**: Documentation, code comments, and anything the player sees are Korean. Commit messages and pull request text are English.
- **Run `scripts/check.ps1` Before Committing**: CI cannot build the plugin, because that needs the game assemblies. A change that breaks only the plugin passes CI green.
- **Ask When Ambiguous**: Do not make arbitrary assumptions when requirements or details are unclear. Always ask the user for clarification.
- **Changelog Format**: Write `CHANGELOG.md` and release notes as `### Added` / `### Changed` / `### Fixed` bullet lists. Keep each entry to one or two plain sentences describing what changed for the player. No implementation detail, no benchmark numbers, no bold-per-bullet.
