# Focused test plans

A maintainer can send a small JSON file that selects the evidence checks needed
for a support request. Choose **Import test plan…** before recording or starting
a check. The app shows only those evidence items and focused keys, in the
requested order. Device information is still collected normally.

Use **Show all checks** to return to the full checklist before testing starts.
Once a recording or check starts, the plan stays fixed for that session so an
unfinished change or restoration cannot disappear. Reopen Observer for a different
plan. Invalid imports leave the current checklist unchanged.

Importing does not start recording, allow Synapse observation, arm a key check,
or change a setting. You still choose each action. Skip controls the laptop does
not offer and restore the exact starting setting. A key-only plan hides the
recording guide; keep Observer active during its prompted key check. A plan
containing only current-state reports hides recording consent and Start recording.

## JSON format

Use UTF-8 JSON, at most 16 KiB:

```json
{
  "schemaVersion": 1,
  "activityIds": ["fans", "sensor-telemetry"],
  "specialKeys": []
}
```

At least one activity or special key is required. Arrays may be omitted when
empty. Names are case-sensitive; duplicate IDs, unknown fields, unknown IDs,
numeric key values, comments and trailing commas are rejected. Version 1 plans select built-in checks only. Version 2 also supports bounded,
displayed instructions for owner reports and maintainer research.

Examples: [fans and displayed sensors](examples/focused-fans.json) and
[a focused Fn + P check](examples/focused-fn-p.json). Fn + P may change a setting;
note its starting value and restore it afterward.

The evidence ZIP includes a normalized, reimportable test-plan.json. It contains
only the selected IDs, not the imported filename or local path. Collection review
counts requested items and identifies unattempted requested keys. The full
support-coverage draft still labels other support leaves NotInvestigated; they
are not treated as failed requested tests. Passive recording remains scoped to
the selected laptop and may retain other eligible calls made by Synapse during
the recording. Selecting a plan does not narrow that call stream.

## Evidence item IDs

| ID | Evidence item |
| --- | --- |
| performance | Performance mode |
| fans | Fan setting |
| charge-limit | Battery charge limit |
| lighting | Keyboard lighting |
| custom-cpu-level | Custom CPU level |
| custom-gpu-level | Custom GPU level |
| hyperboost | HyperBoost |
| cooling-pad | Cooling pad detection |
| battery-protection | Battery protection |
| full-charge | Full charge override |
| keyboard-brightness | Keyboard brightness |
| lighting-frames | Chroma effects and frame streaming |
| lighting-key-map | Physical key and lighting position |
| keyboard-layout | Keyboard layout and zones |
| logo-lighting | Logo lighting |
| mute-indicator | Mute indicator |
| function-row | Function row behavior |
| gaming-mode | Gaming mode |
| startup-lighting | Startup lighting behavior |
| special-key-m1 | M1 key |
| special-key-m2 | M2 key |
| special-key-m3 | M3 key |
| special-key-m4 | M4 key |
| special-key-m5 | M5 key |
| special-key-fn | Fn key |
| special-key-fn-p | Fn + P |
| power-source | Power source |
| adapter-rating | Adapter power rating |
| gpu-core-offset | GPU core offset |
| gpu-memory-offset | GPU memory offset |
| cpu-power-limits | CPU power limits |
| cpu-optimizer | CPU optimizer |
| display-refresh | Display refresh rate |
| display-hdr | Display HDR |
| display-vrr | Display variable refresh |
| display-drr | Display dynamic refresh |
| display-color | Display color mode |
| display-brightness | Display brightness |
| sensor-telemetry | Sensor telemetry |
| ec-mcu | EC or MCU information |
| other-feature | Other laptop feature |

## Focused key names

Use M1, M2, M3, M4, M5, Fn, FnP, FnR, or FnF1 through FnF12.
Special-key evidence items in activityIds are user reports; specialKeys selects
separate five-second input checks. Choose each explicitly when both are needed.

## Follow-up plans (version 2)

[The Blade 18 follow-up plan](examples/blade18-remaining-checks.json) separates
remaining owner checks, maintainer research, and work already covered. Its
[rationale](blade18-remaining-checks.md) explains why each item remains.

Version 2 preserves activityIds and specialKeys, and adds:

- targetModel: optional model family such as RZ09-0484. OpenBlade reports are
  blocked when identity collection is incomplete or any product row disagrees.
- checks: at most 24 entries. Each has a unique id, kind (openblade or research),
  title, one to six steps, expected, and requiresRestoration. A required
  restoration also needs restore.
- covered: at most 24 objects with id and summary describing evidence already
  retained. These are not tests to repeat.

IDs are bounded tokens (80 characters); titles are at most 128 characters;
steps, expected results, restoration instructions and summaries are at most 256.
Display strings reject paths and control characters. IDs must be unique across
activities, checks and covered entries. The entire file still fits in 16 KiB.
At least one activity, key or check is required. Unknown fields are rejected.
Version 1 imports and exports retain their original format.

OpenBlade checks ask the owner to record starting settings before beginning.
The plan then stays locked. The owner reports Worked, Problem, Control unavailable
or Skipped, and records restoration when required. If restoration is unconfirmed,
record a Problem and stop testing. Save the partial ZIP. Observer neither applies
these settings nor connects to OpenBlade. Finish Synapse recording and close
Synapse before following OpenBlade instructions; leave Observer open to report.

The ZIP contains the normalized plan and, when a check begins,
openblade-checks.json. Its append-only start and result records are explicitly
user-reported, never firmware readback or automatic validation. A started check
without a result stays unfinished in collection review. Research and covered
entries have no owner result controls and do not count as unfinished owner work.
Importing instructions cannot execute commands or authorize attachment.
