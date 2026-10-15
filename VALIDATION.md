# Validation record

English | [简体中文](docs/zh-CN/VALIDATION.md) | [Русский](docs/ru-RU/VALIDATION.md)

Each check below applies to its recorded source and conditions. Synthetic checks
and individual live trials do not establish complete Synapse coverage or
all-model compatibility.

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

## Expanded support collection, 2026-10-14

Version 0.2.0 adds the full-support requirements catalog, 42 guided activities,
reviewed lighting and keyboard report grammars, prompted special-key input,
Windows support inventory, and the schema-v2 contribution ZIP. Synthetic
regressions exercise complete frame retention, unknown-buffer privacy, bounds,
ordinary-typing exclusion, duplicate request/response operation IDs, ordered
and repeated action windows, and conservative feature coverage.

An inventory-only run on the RZ09-0581 collected ten Blade HID descriptor
records, six driver ancestry records, two graphics adapters, one internal panel,
brightness, and Windows/CPU details. Windows returned HDR capability and active
color state, DRR state, and available display modes. Thermal zones, LampArrays,
and running Synapse provenance were absent during this run; EC/MCU commands,
current LampArray frames, VRR range, and unreviewed report bodies remain explicit
gaps. A null WMI thermal row exposed a collection fault; the fix skips null rows
and isolates each support section so a failed section preserves earlier data.

The saved local ZIP has five entries and 170 coverage leaves. It contains no
feature observations or special-key events from this inventory-only run.
Disconnected renders cover the dark OpenBlade palette, branding, version badge,
activity picker, Custom details, and narrow width. These checks did not attach
to Synapse or change laptop settings. Lighting, logo, keyboard mode, M/Fn input,
and the additional tuning workflows still need contributor or attended trials;
previous thermal trials do not validate them or other Blade models.

## Window and key-check correction, 2026-10-14

Version 0.2.1 removes the contributor-facing keyboard-mode declaration and
mode picker. The guide now has 41 activities; its independent support draft
still tracks mode requirements. Passive mode reports remain available, and
key probes retain Unknown mode context without asking owners to infer it.

The fixed 640-pixel maximum height caused Windows maximization to occupy only
part of the screen. The window now has no height ceiling, starts compact, and
expands once after inventory unless the user already resized it. Disconnected
previews use a real maximized WPF window and record its measured bounds beside
the image. On this 150-percent-scale display, maximized outer bounds measured
1721.33 by 1033.33 logical pixels against a 1706.67 by 1018.67 work area; the
small difference is Windows' outside resize border. The window fills the work
area instead of being limited to 640 pixels. No Synapse attachment or input
probe occurred during these layout checks.

## Claude review corrections, 2026-10-14

Claude reviewed source `433f413b3bda034def84c380180a700413fa3adb` (0.2.1)
and reported ten defects. Each was independently checked against source or an
owned fixture. Version 0.2.2 fixes all ten; no second Claude approval is claimed.

- HID and keyboard-class Raw Input paths resolve through exact SetupAPI device
  instance identity. A read-only check on this laptop found two selected Blade
  keyboard HID candidates, two verified keyboard aliases, and two matching Raw
  Input keyboard names. No input was registered or read, and no paths exported.
- USB ancestry retains the nearest matching physical laptop parent through hubs.
- Setter acknowledgements received through GET_FEATURE remain setter responses;
  thermal, battery, and brightness ACKs do not establish getter coverage.
- Concurrent Stop and Close join one cleanup. Polling continues during native
  stopping, with one final drain after the reader has joined. Owned tests cover
  success, failure, cancellation, and a full marker quota.
- Full marker sessions count rejected markers and keep stop/export available.
  Rejected user actions do not advance the guide. Starting a recording preserves
  the chosen pending activity and entered state.
- Key-probe results preserve user declarations and leave unreported key behavior
  pending. Current-state reports, skips, definitions, and probe intent remain
  context markers rather than incomplete setting/restoration windows.
- Shared-ring pressure keeps events in the larger engine queue. The owned native
  fixture passes a 128-event completed-response burst through the actual 32-slot
  shared drain during stop without additional loss. A missing consumer verifies
  the one-second bound and explicitly counted loss. The actual DLL fixture also
  passes its owned startup/stop handshake; no device calls occur in either test.
- Export freezes the session, prepares drafts and JSON off the caller thread,
  and reuses action windows across drafts. A deterministic blocked-work fixture
  verifies that the caller returns while preparation is still pending; another
  verifies cancellation leaves no destination ZIP.

All 338 Release managed tests pass (Core 217, Backend 55, App 66), along with
both native owned-process fixtures. Native dependency warnings remain visible.
These corrections do not extend the earlier live thermal trial to lighting,
logo, key probes, other Blade models, or the revised native drain. The final
package source revision and executable hash are recorded in build-manifest.json.

## Follow-up review corrections, 2026-10-14

Claude reviewed the corrective range `433f413...d904655`. Its ten reported items
were checked independently: four runtime defects, unnecessary empty-poll UI work,
and a documentation typo were confirmed. Version 0.2.3 corrects those six items.
The other findings were conditional or rejected as current integrity defects;
no second review of 0.2.3 is claimed.

Successful HidD_GetFeature returned buffers preserve `returnedLengthUnknown`
with a null actual returned-byte count. The caveat is separate from ErrorCategory
and recording loss, allowing complete reviewed report bodies through the same
privacy grammar. Unknown and malformed bodies remain metadata-only. The quality
receipt labels returned-length uncertainty, observed API failure, and unobserved
completion separately. Failed API calls keep their actual failed status and
native error while the guide reports that the call itself was captured.

Owned App fixtures exercise the real native-event-to-session projection for a
reviewed HID response, ordinary API failure, pending I/O, and dropped-event-only
steps. They confirm queue loss wins over a no-observation reason; manual stop
never reports expiry; native failure and normal expiry remain distinguishable;
and every unfinished step keeps its exact restoration reminder. Fifty empty
polls produce no binding or command notifications. New calls and dropped-event
counts update only the three affected summaries.

All 360 Release tests pass (Core 230, Backend 55, App 75). The packaged build
also runs the unchanged native fixtures. The privacy classifier and native hook
code are unchanged; the revised mapping has only owned synthetic validation.
No new vendor-process attachment, device command, or keyboard input recording
occurred. Earlier live thermal results remain limited to their recorded sources.
The final package source revision and executable hash are in build-manifest.json.

## Recording-lifetime and copy-loss corrections, 2026-10-14

Claude's completed Opus high review of `d904655...f6275b6` produced three confirmed
runtime issues and one narrower receipt diagnostic issue. Version 0.2.4 corrects
those four findings. The newly requested review runs against the committed fixes;
its outcome is recorded separately with the exact revision in the PR description.

Owned regressions verify that recordings dropping five then three events produce
eight session gaps, preserve both drop markers and earlier evidence, and label
the second changed activity CaptureFailed. Each recording gets a fresh native
counter baseline while accumulated evidence remains intact. Manual and autonomous
native failures each add one gap; stop after autonomous failure does not count
it again, and a new recording can count its own failure. Dispose failure is also
visible in the cleanup gap total.

Polling failure in both change and restoration phases preserves the exact saved
baseline in status and instructions, removes the obsolete change/mark prompt,
and keeps Mark disabled until the recording is stopped. The quality receipt's
CapturePayloadLoss code identifies unreadable-buffer and buffer-exceeds-limit
without confusing API failure, returned-length uncertainty, pending completion,
or privacy-only redaction. A ZIP regression checks that the code is exported.

All 378 Release tests pass (Core 241, Backend 55, App 82). The package build also
runs both unchanged native owned-process fixtures. No new live attachment,
setting change, input recording, or physical-model validation occurred. Package
revision and hashes remain in build-manifest.json.

## Remaining coverage limits

- Typed setting-change response payloads: the observed returned bodies remain
  metadata-only under the reviewed privacy grammar.
- Other Blade model and different transport contributor trials.
- Security-software behavior with the intended signed release build.
- Pending asynchronous I/O completion remains outside observer coverage.

These checks do not include driver installation, sleep/resume testing, or
firmware-write validation. The observer records passive user-mode calls, not
firmware readback. Version 0.2.7 was published on 2026-10-15; its released-build
checks are recorded below.

## Released v0.2.7 inventory export, 2026-10-15

The executable was freshly downloaded from the published GitHub release rather
than rebuilt from current source. The release tag and embedded build manifest
both identify source `476de1e47f563c650aa3828c694b2a0be49d48a0`.

- Release ZIP SHA-256:
  `7E84B1F06C1040DB5D7CEA664B1BEDD330CE2098898D0CE4DD872D5ED0F4B681`.
- Released executable SHA-256:
  `428ECBAC88DEBA47ED34CCF427F4B21B4D2AD537ACE710549EAC07C4A8534E2A`.
- Local inventory ZIP SHA-256:
  `3B2A0A57A4C711258BDF04B3E162762A1D64409CAED417CC849CE3D4FDD6DC64`.

The released executable's read-only inventory export completed successfully.
Its ZIP has the five expected entries: manifest.json, coverage-draft.json,
annotation-draft.json, quality-receipt.json, and README.txt. Build identity,
schemas, and cross-file counts reconcile. Of 170 checklist leaves, 13 contain
captured inventory facts and 157 remain NotInvestigated. There are no action
windows, control observations, special-key observations, or rejected records.
All query, setter, and production-admission claims remain false.

On this RZ09-0581, the export groups ten sanitized HID collections under one
physical laptop root, including distinct 91-byte and 375-byte feature
collections. It includes both Windows product-identity registry views, HID
report geometry and capability counts, six driver ancestry records, signed/hash
Synapse provenance, and Windows, CPU, GPU, display, and brightness information.
This is useful for choosing the exact candidate interfaces. It supplies neither
command semantics nor a physical keyboard-to-lighting map.

The quality receipt contains 13 inventory gap records, representing eight
distinct limits rather than 13 missing inventory sections. Display enumeration
reached its 128-unique-mode bound. Windows returned no ACPI thermal zones or
matching LampArray. EC/MCU queries and raw EDID were deliberately skipped;
LampArray current-frame and control-availability data and VRR capability were
unavailable from the implemented sources. An absent Windows source does not
establish that the laptop lacks the corresponding hardware feature.

This inventory export is not a complete support submission. A contributor still
needs to record the relevant Synapse changes and prompted keys, and supply
requested layout or adapter details. Unrecognized command bodies can require a
reviewed privacy projection and a focused follow-up recording. User restoration
markers and successful API calls remain separate from firmware readback.

## Released v0.2.7 live performance export, 2026-10-15

The same downloaded executable completed the GUI workflow on the RZ09-0581,
BIOS 4.01, using the installed Razer-signed Synapse 4.0.827.114 engine and HID
module. The GUI displays v0.2.7 and the exact release source in its version
badge description. OpenBlade's service and processes were absent before each
Synapse setting change.

Synapse displayed Balanced and Auto (Default) fans before recording. The guide
recorded Balanced as the starting value, Silent after the Synapse change, and
Balanced after restoration. Synapse visibly confirmed both transitions, and
Auto fans remained selected. All state choices used the dropdowns. No fan,
battery, lighting, or key-probe action was performed. The existing lighting
traffic was observed incidentally, without changing its effect.

The recording lasted about 101.58 seconds. Stop completed and the GUI reported
that hooks had stopped; the inert DLL remains resident until Synapse exits.
Synapse remained responsive. No firmware readback or collector-issued device
command is claimed.

Local live ZIP SHA-256:
`E2906C9C5AC9742087D70F8461742A57026FB77431EF1BE92CBFEBA47AC06A91`.

The saved ZIP has the same five-entry schema and exact release build identity.
Its 32 successful calls produce 38 observations, including six returned buffers.
There are zero failed calls, reported recording gaps, or rejected records. One
complete action window retains the starting, changed, and restoration markers.
Three incidental observations lie outside that window and are reported as such.

Twenty-six reports retain complete reviewed bytes: four 91-byte thermal setter
requests, two adapter-power query requests, and twenty 375-byte matrix frames.
The thermal requests contain numeric mode 5 during the Silent change and 0
during restoration. Those are observed values, not newly admitted commands.
Six GET_FEATURE input bodies and all six completed 90-byte returned bodies
remain metadata-only. Response capacity 91, returned count 90, API status,
completion time, and comparison metadata remain distinguishable from payload
retention. This export therefore supplies useful setter evidence but cannot
establish response semantics, getter behavior, or firmware readback.

Fourteen of 170 support-checklist leaves are Captured; 156 remain
NotInvestigated. The remaining forty guided activities are still Pending.
The counts, retained byte lengths, redacted-body omissions, action associations,
and false admission/readback claims pass focused assertions against the actual
ZIP. Checks found no known local-path, account, device-instance, or serial-field
sentinels in its exported text. This check is limited to those exclusions and
the schema's reviewed projections.

The collection and export workflow passes for this performance action. It does
not prove that one ZIP contains everything needed for full support, especially
while response bodies are withheld and the other feature actions remain
unrecorded. Raw and exported evidence remain local and uncommitted.
