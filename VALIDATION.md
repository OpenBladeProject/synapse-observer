# Prototype validation

The first implementation is a development prototype. Source and synthetic
checks do not establish complete Synapse coverage or hardware behavior.

## Completed locally

- Native hooks exercised against owned synthetic functions and an owned DLL.
- Original return values and incoming/outgoing Windows error state preserved.
- Nested-call duplicate suppression, unrelated handle/IOCTL exclusion,
  bounded queues, oversized buffers, pending completion gaps, and stop checked.
- Separate entry/API-return timestamps checked with delayed synthetic calls.
- Collector parser, payload privacy, bounds, partial export, atomic ZIP output,
  and guided starting/change/restoration handling checked with managed tests.
- Windows device identity and HID descriptors exported read-only on the local
  Blade, without vendor process attachment or feature reports.
- Disconnected WPF previews rendered from the real controls.

## Pending before public release

- Exact-head live attachment to the reviewed signed Synapse build.
- Real NT handle-name matching across this laptop's feature-report collections.
- Useful request/response/action correlation and confirmed observer stop.
- Security-software behavior with the intended signed release build.
- Additional supported Synapse engine/module identities and unknown-model
  contributor trials.

No live Synapse attachment, laptop setting change, driver installation,
sleep/resume test, or public release is claimed by this validation record.
