# Changelog

## Unreleased

### Changed

- Rewrite Chinese and Russian guides with clearer instructions and more natural wording. ([#9](https://github.com/OpenBladeProject/synapse-observer/pull/9))
- Clarify how to download Synapse Observer and record evidence
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Explain how to review and submit evidence for an existing or new OpenBlade
  device support request
  ([#3](https://github.com/OpenBladeProject/synapse-observer/pull/3)).

### Added

- Add complete Simplified Chinese and Russian README, evidence guides, and
  validation history with matching language navigation and parity checks
  ([#8](https://github.com/OpenBladeProject/synapse-observer/pull/8)).

## [0.2.7] - 2026-10-15

### Added

- Add a portable Windows app that inventories a Razer Blade, passively observes
  selected Synapse activity, and saves a local evidence ZIP for OpenBlade
  support work ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Guide collection of settings and prompted special-key evidence, with Windows
  support inventory, coverage summaries, action receipts, and quality receipts
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Add an OpenBlade-branded dark and high-contrast interface with compact status
  cards, green action buttons, visible keyboard focus, a version badge, and
  guided navigation through recording and saving
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).

### Fixed

- Preserve useful partial evidence, recording-loss details, drop totals, and
  restoration guidance when recording stops or encounters malformed input,
  polling errors, or cleanup failures
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Keep reviewed successful responses and unfamiliar numeric settings without
  inventing meaning, while separating API failures, capture loss, and privacy
  redaction in the exported evidence
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).

[0.2.7]: https://github.com/OpenBladeProject/synapse-observer/releases/tag/v0.2.7
