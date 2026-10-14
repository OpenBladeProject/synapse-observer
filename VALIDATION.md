# Prototype validation

The implementation is a development prototype. Synthetic checks and one live
trial do not establish complete Synapse coverage or all-model compatibility.

## Completed locally

- Native hooks exercised against owned synthetic functions and an owned DLL.
- Original return values and incoming/outgoing Windows error state preserved.
- Nested-call duplicate suppression, unrelated handle/IOCTL exclusion,
  bounded queues, oversized buffers, pending completion gaps, and stop checked.
- Separate entry/API-return timestamps checked with delayed synthetic calls.
- Collector parser, payload privacy, bounds, partial export, atomic ZIP output,
  and guided starting/change/restoration handling checked with managed tests.
- Windows device identity and HID descriptors exported read-only.
- Disconnected WPF previews rendered from the real controls.
- Synthetic Blade family, unknown PID/usage/geometry, and unfamiliar-report
  evidence fixtures; these are policy checks, not physical model validation.

## Authorized ten-second live trial, 2026-10-14

Source: `c0cf0aaba2ae00c1725701525125cbd802a9cb86`.
Laptop: Razer Blade 16 RZ09-0581, HID 1532:02E0, BIOS 4.01.
Synapse: reviewed Razer-signed engine 4.0.823.108 and matching node-rz-hid module.

Preflight first exposed a generic USB Composite Device label. Reading the
bus-reported product description corrected laptop discovery without changing
model or PID admission. Core and backend tests and an independent review passed
before attachment. The exact source package passed 106 managed tests and both
native fixtures; existing upstream native compiler warnings remain recorded.

The collector observed an idle Synapse process with a native ten-second lifetime.
It did not request any setting change or send a device command. Exact handle-name
matching produced one successful HidD_SetFeature call on the selected 375-byte
feature collection (usage 000C:0001). Call entry/API return were recorded separately.
The unknown report body remained metadata-only: no payload hex was exported.

The observer reported started, then finished, with failure 0 and dropped events 0.
The final queue was drained, and Synapse remained running and responsive. Hooks
stopped; the inert DLL remains loaded until Synapse exits. Recording markers
span about 10.17 seconds including stop confirmation and drain.

The local ZIP contains only manifest.json and README.txt. It has one observation,
no rejected observations, and no exported local paths or device instance IDs.
ZIP SHA-256: `49C76291C4E6F02BD709A4FEB3065F74AD8A5C999E25A6219BF05E45F0769E7A`.
The ZIP stays local; this record contains the sanitized result only.

## Pending before public release

- Useful setting-change request/response/action correlation, including the
  91-byte control collection; the idle trial did not exercise those commands.
- Other Blade model contributor trials and additional Synapse versions.
- Security-software behavior with the intended signed release build.
- Pending asynchronous I/O completion remains outside observer coverage.

No driver installation, sleep/resume test, firmware-write validation, or public
release is claimed. This records passive user-mode calls, not firmware readback.
