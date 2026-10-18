# Changelog

## Unreleased

### Added

- Import a JSON test plan to show only requested evidence items and M/Fn key checks,
  keep the plan fixed during testing, and include its selected IDs in the evidence ZIP.
  Cache filtered choices and keep unrequested items out of unfinished-check notes
  ([#23](https://github.com/OpenBladeProject/synapse-observer/pull/23)).

## [0.2.11] - 2026-10-16

### Fixed

- Retain 16-column Chroma RGB rows and shorter reviewed effect reports, record
  their reviewed color modes, and preserve logo and brightness values from
  reviewed incomplete replies
  ([#17](https://github.com/OpenBladeProject/synapse-observer/pull/17)).
- Preserve fan RPM and stored-target replies separately, correlate them with
  Synapse's SET_FEATURE query / GET_FEATURE response sequence across complete
  and incomplete replies, and avoid
  classifying the shared power-mode byte as full-charge-only evidence
  ([#17](https://github.com/OpenBladeProject/synapse-observer/pull/17)).
- Offer explicit Fn chords in the focused key check, select the matching key
  when opening a special-key checklist item, and clarify the lighting and fan
  capture steps
  ([#17](https://github.com/OpenBladeProject/synapse-observer/pull/17)).

## [0.2.10] - 2026-10-16

### Fixed

- Include the selected Blade's undefined-usage input collections in prompted
  key checks, and retain their redacted evidence in the ZIP. Preserve usable
  input geometry when optional descriptor details are incomplete, and distinguish
  rejected, interrupted, or unavailable checks from a completed check with no
  input ([#15](https://github.com/OpenBladeProject/synapse-observer/pull/15)).
- Refresh the collection review after a key check and explain when changed
  device information prevents a check from starting
  ([#15](https://github.com/OpenBladeProject/synapse-observer/pull/15)).

## [0.2.9] - 2026-10-16

### Fixed

- Explain when to wait for Synapse profile syncing before changing or restoring
  settings, and how to save partial evidence if syncing stays stuck. Make the
  separate Fn key check easier to find and distinguish a completed no-input
  check from an interrupted probe
  ([#13](https://github.com/OpenBladeProject/synapse-observer/pull/13)).

- Stream sanitized control reports to local disk and export one ZIP without
  the previous observation, retained-payload, or JSON-entry size ceilings.
  Keep memory bounded during recording and export, preserve late setting
  changes and responses, and allow partial export after a storage failure.
  Keep optional key-check limits separate from control recording and retain
  queue-loss totals within each guided action window
  ([#13](https://github.com/OpenBladeProject/synapse-observer/pull/13)).

## [0.2.8] - 2026-10-15

### Improved

- Allow a recording to run for up to ten minutes so longer guided sequences can
  reach their restoration marker before the observer stops
  ([#7](https://github.com/OpenBladeProject/synapse-observer/pull/7)).

- Start another recording in the same collector window after a confirmed clean
  stop, without restarting Synapse. Keep earlier evidence and recheck the current
  process and laptop before every recording. Clarify start and failure guidance
  and include Battery Saver among the suggested performance states
  ([#7](https://github.com/OpenBladeProject/synapse-observer/pull/7)).

- Include independent Windows identity sources, per-action power context, parsed
  HID capabilities, and a collection review in the GUI and exported ZIP
  ([#7](https://github.com/OpenBladeProject/synapse-observer/pull/7)).
- Collect Windows color-profile associations and SDR/HDR defaults, keeping custom
  names and file contents private and reporting missing inventory explicitly
  ([#7](https://github.com/OpenBladeProject/synapse-observer/pull/7)).
- Retain up to 512 display modes per display so higher resolution and refresh
  choices are less likely to be clipped from device information
  ([#7](https://github.com/OpenBladeProject/synapse-observer/pull/7)).
- Preserve restricted summaries of reviewed thermal acknowledgements with
  incomplete envelopes, while excluding opaque bytes and keeping them separate
  from firmware readback
  ([#7](https://github.com/OpenBladeProject/synapse-observer/pull/7)).

### Fixed

- Keep all suggested and custom values reachable when switching evidence items
  with different numbers of choices
  ([#7](https://github.com/OpenBladeProject/synapse-observer/pull/7)).

- Keep timeout restoration guidance accurate for Windows display settings as well
  as Synapse controls
  ([#7](https://github.com/OpenBladeProject/synapse-observer/pull/7)).

- Require a different reported value for a setting change and explain unchanged
  legacy sequences in exported evidence. Validate typed setting descriptions
  before saving a marker, and preserve the submitted value during power checks.
  Keep collection summaries accurate when setting values are missing.
  Version the corrected draft contracts and retain independent missing-call gaps
  ([#7](https://github.com/OpenBladeProject/synapse-observer/pull/7)).

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

[0.2.8]: https://github.com/OpenBladeProject/synapse-observer/compare/v0.2.7...v0.2.8

[0.2.10]: https://github.com/OpenBladeProject/synapse-observer/compare/v0.2.9...v0.2.10
