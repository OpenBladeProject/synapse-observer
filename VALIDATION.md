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

## Pending before public release

- Typed setting-change response payloads: the observed returned bodies remain
  metadata-only under the reviewed privacy grammar.
- Other Blade model and different transport contributor trials.
- Security-software behavior with the intended signed release build.
- Pending asynchronous I/O completion remains outside observer coverage.

No driver installation, sleep/resume test, firmware-write validation, or public
release is claimed. This records passive user-mode calls, not firmware readback.

## Recording reuse and support evidence, 2026-10-15

Runtime source: `78c9395ccd5ff54bd8453fb8f81553656a87f7ed`.
The framework-dependent package passed 478 managed tests and both native owned
fixtures. Independent Claude reviews were completed, with confirmed findings
fixed and other claims checked against reachable source paths. Cleanup
uncertainty remains a permanent reuse refusal for that Synapse process.

The GUI completed two passive recordings in the same Synapse process without
restarting it between recordings. Both recordings captured calls and confirmed
hook and worker shutdown. The explicit GUI export retained both recordings with
separate operation IDs and the five expected evidence files. Local inspection
checked ZIP structure and excluded local paths and device instance identifiers.
The ZIP remains private.

A third recording in the same process completed the reversible battery preset
workflow and restored the starting selection in Synapse. The action markers
used the actual battery context even though the inventory was collected while
external power was present. Reviewed semantic acknowledgement summaries were
linked to their prior requests; opaque bytes and incomplete payload envelopes
remained omitted. No failed calls, recording drops, or rejected evidence were
reported. Automatic fans remained selected in the vendor UI.

This validates passive observation, guided restoration markers, conservative
response summaries, resident reuse, and export retention on the exercised
laptop. It does not establish independent firmware readback, prompted-key
coverage, another feature's behavior, or physical compatibility with another
Blade model. Later presentation changes improve recording status and recovery
and add Battery Saver to the suggested presets. Discovery also reports blocked
resident observers before offering Start. These changes do not alter the tested
native hook lifecycle or evidence decoder. Raw local evidence remains private.

## Broader Synapse workflow trial, 2026-10-15

Live package source: `6c61570e6863cc98e5e24c12051fca0939f11a60`.
Executable SHA-256: `4AF8B3B3DB59642A1959FEC584A03104688529B3CC22664AE3C12728365B253B`.
The laptop was Blade 16 RZ09-0581, HID `1532:02E0`, BIOS 4.01, with Synapse
4.0.827.114. Synapse reported insufficient external power and disabled its
higher performance presets. The connector and adapter wattage were not inferred.

Twelve guided setting windows completed with restoration checked in Synapse:

| Evidence item | Starting selection | Temporary selection | Restored selection |
| --- | --- | --- | --- |
| Keyboard lighting | Quick Static green preset | Red preset | Same green preset |
| Logo lighting | Off | Static | Off |
| Function row | Multimedia keys | Function keys | Multimedia keys |
| Gaming mode | On, Windows/Copilot disabled, Alt+Tab/Alt+F4 allowed | Off | Same On settings |
| Charge limit | 80%, optimizer On | 70% | 80% |
| Battery protection | Optimizer On, 80% | Off | On, 80% |
| Full charge override | Off, optimizer On, 80% | On | Off, optimizer On, 80% |
| Keyboard brightness | Plugged In, 6% | 7% | 6% |
| Animated lighting frames | Quick Static green, 6%, logo Off | Spectrum Cycling, default animation | Same Static green, 6%, logo Off |
| Fans | Auto | Fixed, displayed 5400 RPM | Auto |
| Display refresh | 60 Hz | 240 Hz | 60 Hz |
| Display color mode | Native | sRGB | Native |

The collector completed eleven passive recordings in the same Synapse process,
with confirmed hook and worker shutdown after each. The first recording reached
its normal five-minute bound; its completed lighting window remained intact,
and clean stop allowed reuse. No failed feature calls or recording drops were
shown. One rejected Gaming description remains in the quality history: a slash
passed the old UI validation but failed the evidence text validator before any
setting was changed. The later valid Gaming window completed and restored.

The report-only flow saved Synapse's displayed 2560 x 1600 panel resolution
without a recording. Skip and Setting unavailable advanced the guide. The
five-second prompted M1 check ended with Timeout and zero input observations;
no physical key was pressed and no key coverage is claimed.

The first nine-window checkpoint contains exactly the five expected ZIP files,
nine ordered complete/restored windows, 2,124 observations, and 2,024 retained
reviewed input bodies. All 44 output bodies remained excluded. A bounded privacy
scan found no local paths, accounts, device instance paths, or serial fields.
The rejected marker remains visible as a global quality gap. Raw ZIPs stay local.

This trial found two collector defects. The fixes prevent identical reported
starting and changed values from completing a change, conservatively flag known
legacy no-op windows in exports, and apply the evidence text validator before
allowing a UI marker. Focused regressions and the Release suite pass 511 tests
(Core 311, App 115, Backend 85). These results cover the code changes; the live
setting trial above used the separately identified frozen package.

Remaining evidence includes disabled Custom/Performance controls and tuning,
physical M/Fn keys and keyboard legends, per-key color mapping, and features not
exposed by this Synapse UI. HDR, VRR, dynamic refresh, panel brightness, EC/MCU
versions, and cooling-pad behavior were not exercised. Manual fan-curve editing
was not part of the Fixed fan-mode round trip. No model beyond this laptop,
firmware readback, physical lighting observation, or signed-build antivirus
compatibility is established by these results.
The final frozen-package ZIP retained all nine earlier action-window objects
unchanged and added the fan, refresh, and color windows. All twelve windows have
exact restoration strings and no window gaps. Eleven recording start/stop pairs
are present, all with clean-stop markers. The bundle contains 2,458 control
observations, 2,182 reviewed input bodies, and four semantic-only returned
summaries; all 132 output bodies remain excluded. The current-state resolution
report is separate from action windows. The M1 timeout retains NoMatchingInput,
and the rejected Gaming marker remains in global quality history. The same
bounded privacy scan found no excluded identifiers or local paths.

The framework-dependent `66b5dd6` package passed 511 managed tests and both native
owned-process fixtures. Its disconnected preview showed the unchanged-value
explanation beside a disabled change button and rejected `Windows/Copilot` with
an explanatory hint. Preview evidence is separate from live Synapse evidence.
Claude's follow-up review prompted submission-value hardening around the power
query. Production currently completes this Win32 lookup synchronously; the
delayed-query scenarios exist only through the internal test seam. The fix
snapshots validated submitted values; three delayed-query regressions cover
starting, changed, and report-only values. App tests pass 118 cases.
Annotation, quality, and capability coverage draft schemas advance together to
v2 for the corrected validity/count/promotion semantics; raw session and native
protocol versions remain unchanged. Independent no-observation gaps survive
alongside unchanged or incomplete marker diagnoses. Core tests pass 311 cases.
Runtime source `7c0c8d794890d4bfb0549ccc8df9298e69205cdd` passed the complete
package script: 514 managed tests (311 Core, 118 App, 85 Backend) and both native
owned-process fixtures. Its executable SHA-256 is
`14922E4A07944BD49A54853D92FAFD9CC9412D570A8E3EE273528D750B04CB6E`.
Following the already authorized Synapse restart, this package collected actual
inventory, attached for a short passive recording, stopped, and saved a new ZIP.
No setting was changed in this smoke recording. Post-restart Synapse inspection
still showed all saved starting selections. The broader twelve-window capture
remains separately identified by its `6c61570` source.

The corrective Claude review completed. Additional claimed stop/close/timeout
races required an incomplete power await; the concrete shipped Windows provider
returns a completed ValueTask from GetSystemPowerStatus, and AsyncCommand has no
yield before invoking the marker action. No such production race was reproduced.
The one-shot delayed test gate is intentional. Independent source triage also
confirmed that nested draft versions are separate from the unchanged outer
bundle format, and that a primary annotation summary need not duplicate every
quality diagnostic. The missing format documentation was corrected.

## AC controls follow-up, 2026-10-15

Live runtime source: `7c0c8d794890d4bfb0549ccc8df9298e69205cdd`,
using the executable hash recorded above. The same Blade 16 RZ09-0581,
`1532:02E0`, BIOS 4.01 and Synapse 4.0.827.114 were used. Synapse now
exposed its AC performance controls; connector type and wattage remain unknown.

| Evidence item | Starting selection | Temporary selection | Restored selection |
| --- | --- | --- | --- |
| Performance mode | Balanced, Auto fans | Custom, CPU Medium, GPU High, GPU overclock Off | Balanced, Auto fans |
| Fan mode | Automatic | Stored Manual Fan Curve, GPU source, percent display, use-both unchecked | Automatic |
| Custom CPU level | Medium | Low | Medium |
| Custom GPU level | High | Medium | High |

No curve points were edited. Custom mode was entered to expose its level
selectors; after restoring both stored levels, the overall mode was returned
to the original Balanced selection. Restoration was checked in Synapse, not
through independent firmware readback.

The first recording reached its five-minute limit before the fan restoration
marker could be saved. Automatic was restored in Synapse, but the incomplete
window remains in the ZIP. A dedicated shorter fan recording completed all
four markers. Separate CPU and GPU recordings also completed and stopped.

The final private ZIP has SHA-256
`CD13EAB2EB6C5F0A13A000F8F7606B24F03DB9E7A40F5C1C0499D73B64CE8F30`.
It contains the five expected files, four paired recording starts/stops,
five action windows (four complete and the retained incomplete fan attempt),
3,150 successful observations, 1,194 complete reviewed input bodies, and ten
semantic-only response summaries. All 978 output bodies remain excluded.
There are no failed calls, rejected records, dropped-event or cleanup-failure
markers. The checkpoint's observations, markers, and two action-window objects
are unchanged prefixes of the final export. A bounded scan found no local
paths, accounts, instance identifiers, serial fields, or email addresses.

HyperBoost and cooling-pad state were marked unavailable for this session.
CPU power-limit and optimizer controls were not exposed in the inspected
Custom panel. GPU overclocking was Off; its offsets were not exercised.
Local exact-PID runtime metadata contains tuning-related fields, but that
neither establishes editable UI availability nor admits commands. These are
session-level limits, not permanent model capability claims.

The attempt to open Windows Settings through Computer Use timed out awaiting
app approval. HDR, VRR, dynamic refresh, and Windows display-brightness changes
remain untested. Physical M/Fn keys, keyboard legends, per-key color mapping,
physical LED observations, EC/MCU versions, adapter rating, and cooling-pad
behavior also remain unverified. No sleep/resume or startup hardware test was
attempted. Prior unchanged shared-path evidence is reused; no all-model or
complete-support claim is made.

A later presentation correction names the review count "complete action
sequence" because the public marker API accepts omitted values. Its focused
regression uses accepted null values; whitespace-only values are rejected by
the session API. This wording change does not alter the native observer,
recording lifecycle, evidence decoder, or the live runtime identified above.

The final summary wording package at source
`4be4f49b8dd768108520aca3e46da3e8462fd917` passed 515 managed tests
(312 Core, 118 App, 85 Backend) and both native owned-process fixtures.
Its executable SHA-256 is
`903C002F58A9E58036FA1B23D7C25ADEEF062B52F7346CB8BAC975CEF0B82F7A`.
This package remains framework-dependent and does not bundle .NET.
