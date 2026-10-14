# Blade model coverage

The collector is for the whole Razer Blade family. Device information collection
has no model, generation, SKU, BIOS, or PID allowlist. It keeps the actual Windows
identity and every discovered Razer HID interface, including unfamiliar IDs,
usage pages, and report sizes. It remains available when Synapse is missing or
recording cannot start.

Recording identifies a physical laptop from its Razer USB vendor identity and
bus-reported Blade product description. It observes that laptop's own feature
collections, never a copied Blade 16 interface or report geometry. Pro, Stealth,
14, 15, 16, 17, 18, and unfamiliar Blade names have synthetic discovery fixtures.
Those fixtures check policy; they do not establish physical compatibility.

The current native prototype recognizes Synapse 4.0.823.108 and its reviewed
node-rz-hid module. This software-version gate applies to every laptop model.
Synapse 2, Synapse 3, and unfamiliar Synapse 4 builds can contribute inventory;
their process/module identities and transport need review before live recording
is available. Do not infer a laptop is unsupported from that recording result.

Feature reports can have unfamiliar lengths and command bodies. Their timing,
API, selected interface, status, and shape stay in the evidence. Unknown bodies
are metadata-only until a privacy review can define a safe projection. Complete
91-byte envelopes matching the reviewed numeric grammar may include bytes;
91 bytes is an export privacy rule, not an attachment requirement. A maintainer
may need a focused follow-up when a new command needs more than this metadata.

The observer follows HID feature APIs only. Controls sent through other driver
APIs or direct native syscalls are outside its coverage. Up to eight feature
collections on one physical laptop and bounded 4096-byte reports are supported;
ambiguous roots or inaccessible descriptors leave a partial inventory bundle.
It does not silently choose another device or weaken identity checks.

The live trial so far covers one Blade 16 RZ09-0581 on the reviewed Synapse build.
A ten-second idle session observed a completed 375-byte HidD_SetFeature call and
confirmed observer stop. Its unfamiliar body was withheld. No setting was
changed by the collector. Other models and setting changes still need contributor
trials; see [validation status](../VALIDATION.md).
