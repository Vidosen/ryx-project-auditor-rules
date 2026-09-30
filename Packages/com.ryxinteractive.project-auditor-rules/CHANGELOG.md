# Changelog

## Unreleased

- Add RYXPA2001 for normalized duplicate private static method implementations within a Player compilation.
- Preserve semantic symbol identity and skip unsupported constructs; leave the SLOC baseline unchanged.

## [0.1.1] - 2026-09-10

- Split baseline capture records into short, checksummed chunks to avoid Unity silently dropping messages with long source paths.
- Require an end-of-compilation receipt with record count and digest for each assembly before saving a baseline.
- Preserve existing baseline files when chunks or complete records are missing.

## [0.1.0] - 2026-09-01

- Published the initial Ryx Project Auditor Rules package.
- Added Roslyn diagnostics for oversized types and executable members.
- Added Player-only Project Auditor integration, JSON settings, baseline capture and ratcheting.

