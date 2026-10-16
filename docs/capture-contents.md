# Evidence ZIP contents

English | [简体中文](zh-CN/capture-contents.md) | [Русский](ru-RU/capture-contents.md)

Saving creates one local ZIP. Nothing is uploaded automatically. The files are
bounded JSON drafts plus a short text guide.

## `manifest.json`

The manifest is the sanitized session record. It contains the collector build,
Windows identity and support inventory, per-session HID labels, signed driver
and Synapse module provenance, action markers, prompted special-key evidence,
feature-call observations, guided-item coverage, and rejected-record counts.

Control observations keep the API, direction, timing, completion status,
selected per-session interface label, report shape, and privacy disposition.
Only report bodies that match a reviewed numeric grammar can be retained.
Unfamiliar, malformed, failed, sensitive, and over-limit bodies are redacted.
A per-session HMAC equality token and bounded changed-byte ranges may show that
two opaque reports match or differ without creating a stable device identifier.

## `coverage-draft.json`

This file lists every leaf from OpenBlade's schema-2 device-coverage template
plus current product requirements that the older template does not yet
represent. It also keeps generic external-power and prompted-input leaves where
Windows cannot establish a barrel/USB-C class or a physical key's product role.
Each leaf is `NotInvestigated` or `Captured`, with references to the supporting
Windows inventory, feature call, or prompted key observation.
`Captured` identifies evidence to review. It does not mean the full range was
captured or that a query, setter, restore path, lifecycle path, or production
admission passed. A setter acknowledgement read through GET_FEATURE does not
create getter coverage; command identity distinguishes it from a query response.
For a narrowly reviewed incomplete thermal acknowledgement, the draft may retain
closed semantic fields and a unique preceding setter reference while the opaque
leading byte and report body remain excluded. This still does not create getter
or firmware-readback coverage.

## `annotation-draft.json`

This file groups action markers and observations into bounded action windows.
Its v2 draft includes `unchangedReportedValues`; known unchanged selections do
not form a complete setting-change sequence. Check each draft's `schemaVersion`
using the [format rules](evidence-format.md#derived-draft-schemas-in-028).
Current-state reports, skips, definitions, and probe intent remain separate
`contextMarkers`; they do not create setting-change or restoration windows.
It also keeps special-key probe summaries and observations. Its layer labels are
`UserModeFeatureApi`, `WindowsRawInput`, and `WindowsInventory`.

The file helps a maintainer locate relevant evidence. It is not a USB capture,
a finished capture-repository annotation, or proof that firmware accepted a
request.

## `quality-receipt.json`

The receipt counts captured and not-investigated coverage leaves, inventory
gaps, complete action windows, reviewed reports, redacted bodies, unassociated
observations, special-key observations, and rejected records. It distinguishes
`ObservedApiFailure`, `CompletionNotObserved`, and the informational
`ReturnedLengthNotProvided` caveat. `CapturePayloadLoss` identifies native
unreadable-buffer or bounded-size copy loss, including when the API succeeded;
it remains separate from deliberate privacy redaction. An observed API failure is independent of
collector data loss. It names partial,
skipped, unavailable, failed, cancelled,
redacted, and incomplete results. It also records the maintainer stages still
required and sets query-validation, setter-validation, and production-admission
claims to false.

In v2, `UnchangedReportedValues` and `IncompleteActionMarkers` diagnose the
reported sequence. `NoMatchingObservation` independently identifies a window
without feature calls, so an unchanged empty sequence retains both facts.

## `README.txt`

The text file explains the same trust boundary beside the evidence. Keep it
with the JSON files when attaching the ZIP to a support request.

## Collection limits

- Sanitized control observations are written to a local temporary spool as they arrive, then streamed into one ZIP. There is no fixed observation count, total retained-payload allowance, or JSON-entry size ceiling. Memory use stays bounded; available local disk space determines how much can be retained. The temporary spool contains the same privacy-filtered fields as the manifest, never raw reports or the comparison secret. A storage failure stops collection with an explicit gap; only completely stored records are exported. Failed or cancelled export preserves the collection for retry. Temporary evidence is released when the collection and its active exports close.
- An interrupted setting keeps its exact starting-value reminder. A late restoration confirmed after recording stopped is exported as a separate user-reported context marker, leaving the earlier recorded sequence incomplete. Partial save is available without confirmation. Marker space is reserved for cleanup. Queue-loss notices coalesce within each recording and guided action window, preserving its first-loss timestamp and latest recording-wide cumulative count. Cleanup failures retain the existing manual Synapse restart requirement.
- A recording lasts at most ten minutes.
- One identified physical Blade laptop can contribute up to eight feature
  collections during live observation.
- Feature reports are bounded to 4,096 bytes per observation and 16 KiB per native observer event.
- The session accepts up to 512 markers and 128 guided coverage items.
  Reaching the marker limit preserves earlier evidence, counts rejected markers,
  and leaves stop/export available. The independent coverage draft still contains
  the complete maintained capability universe; it is not limited by the guided-item bound.
- Special-key evidence is bounded to 256 observations, 32 observations per
  prompted probe, and 64 probe summaries. Reaching a total key-input or probe
  limit disables further optional key checks; setting recordings can continue. A probe that cannot retain
  its result does not claim normal completion.
- Display text is limited to 256 characters and opaque identifier tokens to 80
  characters.
- Payload comparison keeps at most 64 streams and 16 changed-byte ranges per
  comparison. Its random HMAC key is destroyed with the session and is never
  exported.
- Inventory accepts up to 256 HID interfaces, 32 Synapse processes, and 256
  modules per process. Support inventory accepts 32 HID descriptors, 24 device
  drivers, 8 files and 16 filters per driver, 64 executable modules, 8 graphics
  adapters, 16 displays with 512 modes each, 16 brightness entries with 101
  levels each, 16 thermal zones, 8 processors, and 32 explicit gaps. It accepts
  16 LampArrays with 4,096 lamps, 256 key mappings, and 512 lamp indices per key
  for each array. HID capability summaries are limited to 128 shapes per interface;
  color-profile lists retain up to 32 associations plus the two default-profile
  records for each queried scope. Only recognized preset files are hashed, with
  hashing limited to 16 MiB per file.
- Export streams JSON entries to a private temporary ZIP,
  flushes it, and replaces the selected ZIP only after every entry succeeds.

The ZIP excludes HID interface paths, PnP instance paths, serial-number fields,
user names, process IDs, command lines, account data, module paths, and arbitrary
process memory. See [support evidence requirements](support-evidence-requirements.md)
for the work required after collection.

Numeric value ranges are not production admission rules in this collector.
Within the exact reviewed thermal, Custom CPU/GPU, fixed-fan, and charge-limit
families, unfamiliar numeric values are retained with
`numericMeaningUninterpreted: true`. The report family, control tag, channel,
length, checksum, padding, source and status checks still apply. Query input
arguments must match the reviewed zero-valued shape. This permits new-model
values to be investigated without pretending to understand or support them.

Keyboard modes are internal evidence. Contributors are not asked to identify
Normal or Driver mode. Mode feature reports remain in passive observation;
prompted key checks use Unknown mode context rather than guessing from keys.

Color-profile evidence includes Windows associations and SDR/HDR defaults. It
does not include profile contents or establish calibration or gamut accuracy.
The collector does not yet enumerate the separate control and virtual-interface
topology required for an unfamiliar native-brightness driver stack. Driver-file
signature checks do not separately verify catalog signatures: an `Unsigned`
result is not proof that a Windows driver lacks a valid catalog signature.
These collection limits are included in the quality receipt.

The optional key check also listens to eligible Generic Desktop input collections with an undefined usage. Windows delivers that page to the foreground collector, which checks each device before reading its report body. Mouse data and unrelated devices are ignored. Unknown report contents remain redacted. Incomplete optional descriptor details do not block a check when its top-level usage and input size were collected. Rejected input and interrupted or unavailable checks are reported separately from a completed check with no input.
