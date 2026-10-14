# Changelog

## Unreleased

### Added

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
