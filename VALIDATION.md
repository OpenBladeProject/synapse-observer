# Prototype validation

The implementation is a development prototype. Synthetic checks and the live
trials below do not establish complete Synapse coverage or all-model compatibility.

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

## Guided performance recordings, 2026-10-14

Laptop: Blade 16 RZ09-0581, HID 1532:02E0, BIOS 4.01, AC power.
Synapse: Razer-signed engine 4.0.827.114 with its matching signed HID module.
Engine SHA-256: `9B796816DC65318D496A0CAA10EEBF8B0814987E92CB19607FD406E92FB5E0DC`.
HID module SHA-256: `F611827603911D7807C8499DD231BDF77898FBE2EC3CE40215DCCFBB7185CC1F`.
These hashes identify the tested installation; they are not version admission rules.

Both trials used the collector GUI and Synapse's own controls. OpenBlade was
stopped, including fresh controller-absence checks before each vendor UI change.
The confirmed UI baseline was Balanced with Automatic fans. Synapse showed Silent
after the change and Balanced after restoration; Automatic fans stayed selected.
Fan, battery, and lighting collection were skipped. No collector device command,
firmware readback, USB transfer observation, or hardware validation is claimed.

- Source `c4421a6b9013e4effca6ba6f91f2a0476bdd5d68`: 70 observed calls,
  76 exported observations, four retained numeric thermal requests, six completed
  90-byte returned buffers, zero reported gaps and zero rejected observations.
  The then-current classifier redacted the response bodies because it only
  recognized a 91-byte envelope.
  Local ZIP SHA-256: `B74A0CB7D9538143A69A3D8C1CF6C171FFAC2B1FF564B8D31B06EE1D78EB3766`.
- Source `2de3a8aa79305bad8b346980069e9b8aef8d150e`: the dropdown workflow
  completed with no typed setting values. 45 observed calls became 51 exported
  observations in about 164.88 seconds, with zero reported gaps, zero rejected
  observations and zero rejected markers. Four reviewed numeric thermal requests
  were retained. Six completed HID_GET_FEATURE outputs contained 90 bytes with
  independently recorded output capacity 91 and no report ID. Their bodies did
  not pass the reviewed packet grammar and stayed metadata-only; the trial does
  not establish typed response-payload coverage.
  Local ZIP SHA-256: `D93BFBFD85768FEF3A91B407F209E570BAC610256572EEA0D5C887BCE56CCBCB`.

The recorded thermal requests include CPU/GPU numeric mode 5 for Silent and mode
0 for Balanced. These are observed request values, not new production support.
Starting, changed, restored, skipped, and stop markers remained user-reported or
observer-labeled as applicable. Hooks stopped and Synapse remained responsive.
Evidence ZIPs stay local. Exports contain only manifest.json and README.txt; checks
found no exported local paths or device instance IDs.

The subsequent compact UI refresh changes presentation and Save navigation.
It does not change the native observer or evidence classifier. Disconnected
renders cover inventory, recording, Custom details, and the narrow window;
keyboard navigation, disabled states, and automatic Save-card visibility were
checked in the real preview without attaching to Synapse.

## Pending before public release

- Typed setting-change response payloads: the observed returned bodies remain
  metadata-only under the reviewed privacy grammar.
- Other Blade model and different transport contributor trials.
- Security-software behavior with the intended signed release build.
- Pending asynchronous I/O completion remains outside observer coverage.

No driver installation, sleep/resume test, firmware-write validation, or public
release is claimed. This records passive user-mode calls, not firmware readback.
