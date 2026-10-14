# Changelog

## Unreleased

### Added

- Add the guided device inventory, passive Synapse observer, and local evidence
  ZIP export prototype, with discovery fixtures across Blade families and
  unfamiliar PIDs and report sizes ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).

### Fixed

- Identify laptop USB parents by their bus-reported product description when
  Windows displays a generic composite-device label ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
- Accept signed Razer Synapse engine updates without per-version approval; keep
  hashes and versions as evidence provenance ([#2](https://github.com/OpenBladeProject/synapse-observer/pull/2)).
