# Evidence format

Every explicit export contains `manifest.json` and `README.txt`. The manifest
records the collector build/source revision, inventory sections, action markers,
feature observations, coverage gaps, and rejected-record counts. It contains
no automatic upload or command replay instruction.

## Inventory

Windows registry identity includes manufacturer, product, SKU, BIOS version,
and BIOS date. HID inventory includes Razer VID/PID, usage, version, and report
geometry. Relevant Synapse process/module provenance contains file names,
versions, and hashes. Each device interface has a per-session opaque label.
Raw instance paths, process IDs, command lines, user names, and serial-number
inventory fields are not exported.

Local interface paths and USB ancestry exist only in memory to identify the
same physical laptop during attachment. They are not bundle DTOs. Enumeration
does not borrow a known model's commands, and a matching descriptor does not
admit a write.

## Observations

An operation has a sequence-derived ID, exact selected interface label, API,
entry and API-return timing, status/error metadata, and payload disposition.
Input call records and returned-buffer records are separate. Actual output-buffer
capacity is recorded independently of the request length. Pending native
I/O has no completed response. `HidD_GetFeature` does not return an actual byte
count; that uncertainty remains explicit.

Only reviewed numeric thermal, fan, and battery command envelopes that pass
command-specific bounds, checksum, reserved-byte, and padding checks may export
their bytes. Completed HID_GET_FEATURE IOCTL responses may contain a bare
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

Queues, report bodies, record counts, text, and total manifest size are bounded.
Omitted payloads, rejected records, queue loss, and incomplete restoration remain
visible. ZIP writing uses a private temporary file and an atomic final rename;
closing the GUI waits for an active export to complete.

See `EvidenceLimits` and `FeaturePayloadPrivacy` in `src/SynapseObserver.Core`
for the exact limits and privacy classifier. Maintainers should review the
observation layer and gaps before deriving model-specific commands.
