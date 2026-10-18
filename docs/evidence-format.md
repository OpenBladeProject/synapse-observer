# Evidence format

English | [简体中文](zh-CN/evidence-format.md) | [Русский](ru-RU/evidence-format.md)

Every explicit export contains `manifest.json`, `coverage-draft.json`,
`annotation-draft.json`, `quality-receipt.json`, and `README.txt`. The manifest
records the collector build/source revision, inventory sections, action markers,
feature and special-key observations, guided coverage, and rejected-record
counts. The other JSON files prepare a complete OpenBlade coverage checklist,
bounded action windows, and an explicit collection-quality receipt. They contain
no automatic upload or command replay instruction. See
[evidence ZIP contents](capture-contents.md).

## Inventory

Windows registry identity includes manufacturer, product, SKU, BIOS version,
and BIOS date. HID inventory includes Razer VID/PID, usage, version, and report
geometry. Relevant Synapse process/module provenance contains file names,
versions, and hashes. Each device interface has a per-session opaque label.
Raw instance paths, process IDs, command lines, user names, and serial-number
inventory fields are not exported.

Local interface paths and USB ancestry exist only in memory to identify the
same physical laptop during attachment. The nearest matching physical USB parent
is retained even when the laptop is behind a hub. Keyboard-class Raw Input names
are associated with selected HID candidates through exact SetupAPI device-instance
identity; unmatched and ambiguous names are excluded. These paths and identities
are not bundle DTOs. Enumeration
does not borrow a known model's commands, and a matching descriptor does not
admit a write.

## Observations

An operation has a sequence-derived ID, exact selected interface label, API,
entry and API-return timing, status/error metadata, and payload disposition.
Input call records and returned-buffer records are separate. Actual output-buffer
capacity is recorded independently of the request length. Pending native
I/O has no completed response. `HidD_GetFeature` does not return an actual byte
count; `returnedLengthUnknown` preserves that caveat on its successful returned
buffer, while `returnedBytes` stays null. This caveat is independent of capture
loss and does not exclude a complete buffer from the reviewed privacy grammar.
A fully observed API failure keeps its failed status and native error; it is
counted separately from lost or incomplete observations. GET_FEATURE can return a setter
acknowledgement as well as a query response. Annotation roles and getter coverage
use the report's command identity, not the transport API name.

Only reviewed numeric thermal, fan, battery, performance, keyboard behavior,
adapter-power, and bounded numeric or RGB lighting envelopes that pass
command-specific bounds,
checksum, reserved-byte, and padding checks may export their bytes. The lighting
grammar covers exact 91-byte row, latch, brightness, logo, and effect reports,
including 52-byte and 55-byte RGB row payloads and reviewed used-length effects,
plus the reviewed 375-byte matrix frame shape. Recognizing one value or frame is
a privacy decision; it does not establish a model's full range, layout, or
support. Completed HID_GET_FEATURE IOCTL responses may contain a bare
90-byte packet without a report ID. These require a matching 91-byte feature
descriptor, output capacity of at least 91, successful completion with no gap,
and the same complete privacy grammar. Export preserves the original 90 bytes
and a null report ID. This is a privacy projection, not a command-support decision.
Unknown command bodies remain metadata-only with bounded header hints and
lengths; they are not discarded merely because they are unfamiliar. Arbitrary
feature buffers can contain identifiers, so matching a Razer handle alone does
not clear a payload for export.

User-selected or entered starting/changed/restored values are marked `userReported`.
“Setting unavailable” means the user could not find it in Synapse. It does not
mean the hardware lacks that capability. A completed recording card means the
guided user sequence ended; it does not establish firmware validation.

The optional foreground special-key probe runs for five seconds and retains only
bounded input admitted by its narrow prompted-key policy. Expected key and mode
are user reports. A prompted event is exported under a generic input-evidence
leaf unless the observed Windows key or reviewed signal independently identifies
the signal. Ordinary typing and macros are omitted, and a P event during an Fn+P
prompt does not prove that Fn was held or that the key controls performance.
Probe results remain separate from declarations of key behavior; no input signal
never replaces an earlier report or marks the declared key unavailable.

Native queues, individual report bodies, checklist markers, optional key checks, and text remain bounded. Sanitized control observations use a disk-backed store with no fixed count, total-payload, or JSON-entry size ceiling; export streams the frozen committed prefix into one ZIP. A session may include an optional `storageFailure` with a fixed category and summary; it never contains a path or exception message.
Omitted payloads, rejected records, queue loss, and incomplete restoration remain
visible. Native drop counters restart with each recording, while session loss
and earlier evidence remain accumulated. A native failure contributes one gap
whether polling or stop first detects it. If polling fails during a setting
change, the collector requests a bounded stop of its own observer and retains
the exact starting-setting reminder. Successful cleanup does not erase a partial
recording warning. Observer-loss markers also appear in the quality receipt,
even when no setting window contains observations. Call IDs include a recording
prefix so request/response pairs remain distinct across successive recordings.
ZIP writing uses a private temporary file and an atomic final rename;
closing the GUI waits for an active export to complete. Export freezes the session
before dispatching draft preparation and serialization off the UI thread. All
draft files share one set of action windows. Concurrent stop and close join one
cleanup. During ordinary stop and recoverable malformed-record rejection, one
reader remains active through native final draining. A fatal reader fault stops
that reader; cleanup performs a final drain and reports loss. Queue-drop counts
are checked independently of record parsing and again from the stop result. If
the first stop wait is unconfirmed, one additional bounded wait retains the
reader and captures a late native finish before the final drain. An unconfirmed
second result remains a partial-recording failure. Repeated malformed records
retain every rejected-record count while emitting one loss marker per recording.

See `EvidenceLimits` and `FeaturePayloadPrivacy` in `src/SynapseObserver.Core`
for the exact limits and privacy classifier. Maintainers should review the
observation layer and gaps before deriving model-specific commands.

Numeric value ranges are not production admission rules in this collector.
Within the exact reviewed thermal, Custom CPU/GPU, fixed-fan, and charge-limit
families, unfamiliar numeric values are retained with
`numericMeaningUninterpreted: true`. The report family, control tag, channel,
length, checksum, padding, source and status checks still apply. Query input
arguments must match the reviewed zero-valued shape. This permits new-model
values to be investigated without pretending to understand or support them.

## Additive collection provenance in 0.2.8

Session v2 records may include `source` on machine identity rows, distinguishing
`BiosRegistry` from `SystemInformationRegistry` within each registry view. The
quality receipt reports missing or disagreeing sources; no canonical product
is guessed from a disagreement.

Action markers may include `powerSourceAtMarker`, sampled from Windows at that
marker. Annotation windows expose `powerSourceAtAction` only when all setting
markers carry the same known source. Missing, unknown, or changed samples leave
power-specific performance coverage open. Windows external power does not
establish barrel versus USB-C class or adapter wattage. Inventory power is
separate evidence about the initial collection time.

Completed 90-byte feature responses with capacity for 91 bytes may carry
`responseStructureDiagnostics`. Fixed reason codes check both the bare-packet
and report-ID-prefixed interpretations of the available bytes. A prefixed
candidate explicitly lacks final padding. The diagnostics contain no opaque
bytes or header values. A narrower `semanticResponse` is emitted only when the
shifted logical bytes pass every available invariant and the leading byte is
excluded. The complete bare interpretation must not also match. Reviewed
summaries cover thermal acknowledgements, thermal getters, actual fan RPM,
stored fan targets, Custom levels, logo state/effect and keyboard brightness.
They retain the transaction and typed fields while excluding the leading byte,
body and unavailable final padding. `completePayloadRetained` remains false
and `headerHints` remains null. An acknowledgement is distinct from a getter;
RPM is distinct from a saved target. The annotation draft links a thermal
acknowledgement to a setter only when scope, transaction, value and ordering
identify one unique unmatched request. All summaries remain user-mode returned
buffers, not independent firmware confirmation or complete HID envelopes.
See [focused capture steps and provenance](control-capture-follow-up.md).

The shared `07/0F` and `07/8F` numeric power-mode byte uses
`reviewedPowerModeControl`, not the battery-setting projection. Its generic
`power.modeControlReports` coverage does not infer full charge or Maximum Fans
from a zero value or unrelated bit.

The app and README.txt share a collection review showing retained bodies,
unfinished actions, omitted response contents, and remaining evidence items.
A documented sequence describes user markers, not successful firmware readback.

HID descriptors also include bounded parsed button and value capabilities for
input, output, and feature reports: report IDs, usage ranges, link collections,
bit sizes, report counts, logical ranges, and flags. At most 128 shapes are kept
per interface, with an explicit partial or truncated state. Button report counts
are null on HID parser API 1, where that field is reserved. These are descriptor
metadata; no input report bodies or ordinary key presses are collected.

Each active display may also carry bounded `colorProfiles` metadata: selected
Windows scope, associated profiles, and separately identified SDR and HDR
defaults. An association has no inferred subtype. Profiles use per-session
opaque labels. Only recognized preset filenames and their SHA-256 hashes are
retained, after a bounded read from the Windows color directory. Custom names,
custom content hashes, paths, and all profile contents are omitted. Missing APIs, inaccessible files, and
bounds remain explicit partial results without removing the display's modes.

A clean recording can reuse the exact observer DLL verified by the same open
collector. Each recording uses a fresh mapping and repeats process and device
checks. Hooks, callback completion, successful worker exit, and the loaded
module's identity must all be confirmed. Reader or evidence-processing errors
block reuse independently from the native call and cleanup status. A competing start cannot replace an active
recording, and uncertain cleanup permanently blocks reuse in that Synapse
process. A late completion does not remove that block. Closing the collector
loses its verified reuse record; restart Synapse before collecting with another
window or build. The observer and fixed Windows API owners stay loaded until
the vendor process exits; no trampolines are freed while other threads may use
them.

## Derived draft schemas in 0.2.8

`coverage-draft.json`, `annotation-draft.json`, and `quality-receipt.json` use
`synapse-observer.coverage-draft.v2`, `synapse-observer.annotation-draft.v2`, and
`synapse-observer.quality-receipt.v2`, respectively. Consumers must check each
file's `schemaVersion`. The outer `synapse-observer.bundle.v2` manifest and raw
session/native protocol versions are unchanged; their versions do not select
the semantics of a nested draft.

Development builds before `7c0c8d7` used v1 draft schemas while also reporting
collector version 0.2.8. Inspect each file's schema rather than inferring it
from the collector version.

An annotation window's `completeUserReportedSequence` requires the ordered
starting, target, action, and restoration markers and excludes known unchanged
starting/target values. `unchangedReportedValues` identifies that exclusion.
Comparison trims leading and trailing whitespace, collapses whitespace separators,
and uses ordinal case-insensitive comparison. It preserves punctuation and
substantive details. Null or blank values are not classified as unchanged: an
otherwise ordered sequence can still count as complete and contribute capability
evidence. Its actual values and whether the device changed remain unproven.
The GUI requires nonblank values, but the underlying marker format permits them.

Quality `completeActionWindows` counts and derived capability promotion use the
same validity rule. Raw guided coverage and markers remain unchanged as
provenance. An annotation window's singular `gap` summarizes its primary
problem; its observations remain available separately. The quality receipt
records independent facts: `UnchangedReportedValues` or
`IncompleteActionMarkers`, plus `NoMatchingObservation` when the window has no
feature-call observations. An empty unchanged sequence retains both diagnoses.
A completed restoration of an unchanged value is not presented as an unfinished
restoration; it still does not establish a setting change.

### Windows temperature inventory

`support.thermalZones` contains WMI `MSAcpi_ThermalZoneTemperature` evidence.
The additive `support.pdhTemperatures` section independently reads two fixed
Windows PDH `Thermal Zone Information` / `High Precision Temperature` counters:

| Exported alias | Fixed ACPI instance |
| --- | --- |
| `razerEcPci0SbrgTzrz` | `\_SB.PCI0.SBRG.EC0.TZRZ` |
| `razerEcPc00LpcbTzrz` | `\_SB.PC00.LPCB.EC0.TZRZ` |

These are the Razer EC TZRZ paths used by OpenBlade's Windows temperature
source. The collector uses `PdhAddEnglishCounterW`, so counter names do not
depend on the Windows display language. It opens at most two queries, performs
one collection and formatted read per path, and closes each query and its
counter. Cancellation is checked between synchronous native calls; an in-flight
PDH call cannot be interrupted. There is no polling or arbitrary counter search.

Each record retains a fixed alias, provenance, collection state, failure stage,
numeric API/counter status, raw tenths-Kelvin value, and Celsius conversion.
Conversion uses `raw / 10 - 273.15`; values outside 0–115 Celsius are marked
invalid, matching OpenBlade's accepted range. Finite invalid raw values remain
available for diagnosis, while non-finite values and values with invalid counter
status are omitted. Query cleanup failures are explicit. Export contains fixed
aliases and reviewed metadata, never raw instance paths or native exception text.

WMI failure does not suppress PDH results. A failed alias remains visible even
when the other succeeds. Missing counters mean unavailable evidence, not missing
hardware support. Windows thermal-zone observations alone do not establish CPU
package temperature or firmware readback. This collection calls Windows PDH only;
it does not load Synapse DLLs or call vendor services, drivers, or HID endpoints.
