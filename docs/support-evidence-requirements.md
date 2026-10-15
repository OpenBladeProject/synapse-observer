# OpenBlade support evidence requirements

English | [简体中文](zh-CN/support-evidence-requirements.md) | [Русский](ru-RU/support-evidence-requirements.md)

Synapse Observer works backwards from the evidence OpenBlade needs before it can
support a laptop safely. The observer collects passive evidence and prepares a
draft. It never admits a model or command for production use.

## Capability families

The coverage draft keeps every leaf in the OpenBlade device-coverage template
and adds current product requirements that the older template does not yet
represent. The complete families are:

- identity and firmware: USB and HID descriptors, model number, BIOS, EC, MCU,
  relevant interfaces, HID topology, Windows platform details, signed driver
  provenance, and signed software provenance;
- performance: presets, Custom CPU and GPU levels, CPU power limits, CPU voltage
  optimization, GPU core and memory offsets, HyperBoost with and without the
  cooling pad, HyperBoost base modes, and readback on full AC, each USB-C power
  class, and battery, plus generic external-power evidence when Windows cannot
  distinguish the attached adapter class;
- fans: Automatic, fixed range and step, maximum, manual curve writes and
  readback, and RPM queries;
- sensors: CPU and GPU temperature, fan RPM, and sensor-health reporting;
- battery: protection off, advertised limits, limit query, temporary full
  charge, and its query;
- keyboard lighting: Off, brightness, getters, matrix and LampArray discovery,
  Windows-host rendering, layout identity, physical slot mapping, plus every
  effect's colors, durations, directions, regions, key maps, frame format, and
  cadence;
- logo lighting: Off, Static, Breathing, and getter;
- keyboard behavior: function-key priority, Gaming mode, startup animation,
  Normal and Driver device modes, playback-mute indication, and their getters;
- special keys: Fn-layer reports, the performance key, M1 through M5, and
  generic prompted-input evidence that does not assume those product roles;
- display: Windows-visible modes, dynamic and variable refresh, HDR, SDR and
  HDR color profiles, panel brightness, and the model-specific native
  brightness driver path;
- lifecycle: cold boot, sign-in, service restart, sleep and resume, and device
  reconnect;
- power: full AC, USB-C classes, adapter-power query, battery, and every
  transition between them;
- conflicts: vendor-controller presence, external changes, the default
  zero-write policy, and explicit reclaim; and
- failures: timeout, malformed response, checksum, transaction, command and
  status mismatches, unsupported PID or firmware, and disconnect during a
  transaction.

The collector is not restricted to the current OpenBlade production models. It
keeps discovery evidence for Blade Pro, Stealth, 14, 15, 16, 17, and 18 models,
as well as unfamiliar Blade product names and PIDs. Discovery never copies an
interface, report shape, or command from another model.

## What passive collection can establish

`coverage-draft.json` uses only `NotInvestigated` and `Captured`. `Captured`
means the ZIP contains a real, bounded observation for that leaf. Windows
inventory may support identity, descriptor, display, driver, or LampArray
leaves. A feature leaf needs a privacy-reviewed report inside a complete action
window with ordered starting, changed, action, and restoration markers. Known
unchanged starting/changed values are excluded. Missing values are not treated
as equal, so marker order can still qualify without proving the actual change;
see the [draft validity rules](evidence-format.md#derived-draft-schemas-in-028).
A special-key
leaf needs prompted input and a matching key, mode, and probe summary. A prompt
alone advances only a generic input-evidence leaf. An exact role needs an
independently observed Windows key or reviewed signal that establishes it.

The status is deliberately narrow. One brightness, color, direction, matrix
row, or performance level does not establish the whole supported range. A
recognized report shape is a privacy decision, not a command definition. User
markers describe what the contributor selected; they do not prove a firmware
readback or successful restoration. Unsupported report bodies remain redacted,
with their shape and quality gap retained.

The observation layers are `UserModeFeatureApi`, `WindowsRawInput`, and
`WindowsInventory`. They must not be relabeled as USB transfers. The exported
drafts are not PCAP files and do not claim compliance with the separate
OpenBlade capture-repository annotation validator.

## What maintainers must still do

Production support requires a separate, reviewed workflow:

1. Commit a sanitized oracle capture for the exact model, PID, firmware, and
   interface.
2. Decode requests and responses deterministically, including length, checksum,
   transaction, status, error, and timeout handling.
3. Implement a typed read-only query that works without the vendor process.
4. Save the confirmed prior state, apply one typed value, confirm it through an
   independent readback, restore the exact saved state, and confirm restoration
   through readback.
5. Prove unsupported device and firmware rules produce zero writes.
6. Validate controller conflict behavior, relevant power contexts, restart and
   reconnect behavior, and attended sleep and resume where required.
7. Back production code with the committed sanitized fixture and complete the
   explicit model admission review.

If submission or readback is ambiguous, validation stops and continues
read-only. A maintainer does not replay the uncertain write or substitute a
user-reported restoration.

The source checklist is the schema-2
[`device-coverage.template.json`](https://github.com/OpenBladeProject/openblade-captures/blob/main/templates/device-coverage.template.json)
and the full evidence ladder is in the capture repository's
[`CAPTURE_GUIDE.md`](https://github.com/OpenBladeProject/openblade-captures/blob/main/CAPTURE_GUIDE.md).

Numeric value ranges are not production admission rules in this collector.
Within the exact reviewed thermal, Custom CPU/GPU, fixed-fan, and charge-limit
families, unfamiliar numeric values are retained with
`numericMeaningUninterpreted: true`. The report family, control tag, channel,
length, checksum, padding, source and status checks still apply. Query input
arguments must match the reviewed zero-valued shape. This permits new-model
values to be investigated without pretending to understand or support them.
