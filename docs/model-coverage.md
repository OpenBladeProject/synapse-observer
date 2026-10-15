# Blade model coverage

English | [简体中文](zh-CN/model-coverage.md) | [Русский](ru-RU/model-coverage.md)

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

Recording identifies an installed RazerAppEngine process and its node-rz-hid
module by their trusted Razer signatures and roles. It does not use a version
or file-hash allowlist. A new version is a provenance value, not a rejection
reason. Synapse 2 or 3 and a different future transport may need another observer
backend if they do not use this engine/module path; their inventory is still
useful. Do not infer a laptop lacks a capability from an unavailable recording.

Feature reports can have unfamiliar lengths and command bodies. Their timing,
API, selected interface, status, and shape stay in the evidence. Unknown bodies
are metadata-only until a privacy review can define a safe projection. Complete
reports matching a reviewed privacy grammar may include bytes: 91-byte control
envelopes, 375-byte matrix frames, and qualifying completed 90-byte bare
responses. These shapes are export privacy rules, not attachment requirements.
A maintainer may need a focused follow-up when a new command needs more than
this metadata.

The observer follows HID feature APIs only. Controls sent through other driver
APIs or direct native syscalls are outside its coverage. Up to eight feature
collections on one physical laptop and bounded 4096-byte reports are supported;
ambiguous roots or inaccessible descriptors leave a partial inventory bundle.
It does not silently choose another device or weaken identity checks.

Live trials cover one Blade 16 RZ09-0581. The released v0.2.7 GUI recorded
Balanced to Silent and back to Balanced through Synapse, with Automatic fans
retained. The ZIP preserves four thermal setter requests, two adapter-power
query requests, and twenty incidental matrix frames. Six completed response
bodies remain redacted, so this trial does not establish response semantics or
firmware readback. The observer stopped its hooks without a reported recording
gap. Other models and the remaining guided features still need contributor
trials; see the exact sources and results in the [validation record](../VALIDATION.md).
