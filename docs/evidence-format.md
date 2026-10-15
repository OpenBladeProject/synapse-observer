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

Queues, report bodies, record counts, text, and total manifest size are bounded.
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
