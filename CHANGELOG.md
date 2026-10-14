# Changelog

## Unreleased

### Added

- Retain unfamiliar numeric settings in reviewed control families, label their meaning as uninterpreted, and capture zero-valued charge-limit queries. ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2))
- Add the guided device inventory, passive Synapse observer, and local evidence
  ZIP export prototype, with discovery fixtures across Blade families and
  unfamiliar PIDs and report sizes ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Add Windows support inventory, prompted special-key evidence, and a complete
  OpenBlade coverage draft with action-window and quality receipts
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Add dark and high-contrast presentation, an in-app version badge, the
  OpenBlade tray icon, and clearer recorder-discovery diagnostics
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).

### Fixed

- Count drops correctly across successive recordings, show cleanup failures in
  the gap total, and keep exact restoration instructions after polling failure
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Identify native buffer-copy and size loss separately from privacy redaction
  in the quality receipt ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).

- Preserve reviewed successful HID responses while labeling unknown returned
  lengths separately; distinguish observed API failures from recording loss
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Show the actual recording end reason, prioritize known queue loss over missing
  events, and update only changed summaries while polling
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Correct the marker-limit instructions
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).

- Preserve selected activities and reported key behavior, associate keyboard input
  with the exact laptop, and keep declarations separate from setting changes
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Distinguish setter acknowledgements from query responses; preserve the laptop's
  USB parent when it is connected through a hub
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Join concurrent recording cleanup, drain queued observations during stop,
  handle full marker sessions, and save large evidence ZIPs without blocking
  the window ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).

- Remove internal keyboard-mode questions from the guide and key check, and let
  maximization fill the screen's usable area
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).

- Identify laptop USB parents by their bus-reported product description when
  Windows displays a generic composite-device label ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Accept signed Razer Synapse engine updates without per-version approval; keep
  hashes and versions as evidence provenance ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Select common settings from dropdowns during guided recordings, with an Other
  state option and details for exact restoration ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Preserve completed 90-byte feature responses without inventing a report ID;
  retain only reviewed numeric payloads ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Match the refreshed blocker UI with compact status cards, blue buttons,
  keyboard focus rings, and automatic navigation to Save after recording
  ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
