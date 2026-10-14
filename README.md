# Synapse Observer

A small Windows app that collects device information and Synapse activity to
help add Razer Blade support to [OpenBlade](https://github.com/OpenBladeProject/openblade-core).

Device information collection covers the Blade family, including Pro and Stealth
models. Recording discovers the laptop's own HID interfaces and report sizes;
there is no Blade model or PID allowlist. See [model coverage](docs/model-coverage.md).

## Requirements

- Windows 11, x64.
- [.NET 10 Desktop Runtime, x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
  The download does not include .NET. Windows offers its installation link if
  the required runtime is missing.
- Synapse for recording setting changes. Device information can be collected
  without Synapse.

## Use

1. Open `SynapseObserver.exe` normally.
2. Select **Collect device information**.
3. To add a recording, open your laptop's controls in Synapse, check recording
   availability, and allow observation for this session.
4. Select the starting setting, change one setting in Synapse, then select its
   new value and restore the starting setting. Use **Other state…** for an
   unfamiliar value. Skip settings you do not have.
5. Stop recording, save the evidence ZIP, and attach it to your support request.

Nothing is uploaded automatically. The collector does not apply settings or
replay commands. A partial device-information bundle is useful when recording
cannot start.

## Prototype limits

Native observation identifies the installed, signed Razer Synapse engine and
its loaded HID module. Updates do not require version approval. Versions and
hashes are recorded as evidence. An unfamiliar Blade PID is a discovery input;
it is not rejected just because OpenBlade does not support it.

Recordings last at most five minutes. The observer collects feature-report API
calls for one identified physical Blade device. It excludes keyboard and mouse
input, unrelated IOCTLs, local device paths, and serial-number inventory fields.
Unknown or potentially identifying report bodies remain metadata-only; reviewed
numeric command envelopes can include their bytes. See [evidence format](docs/evidence-format.md).

Pending asynchronous I/O is reported as a gap. Observed returned buffers and
your restoration markers do not establish firmware readback. After a session,
the inactive observer module remains loaded until Synapse exits; restart
Synapse normally before recording again.

This is an unsigned development prototype. Live trials recorded a Balanced →
Silent → Balanced change on one Blade 16 and confirmed observation stopped.
Other laptops, different Synapse backends, and security-software compatibility
still need validation before a public release.
Do not disable security protections to run it.

## Development

See [building and verification](docs/building.md), [validation status](VALIDATION.md),
and the [native observer notes](native/README.md). Licensed under
[GPL-3.0-or-later](LICENSE); dependency notices are included in the portable ZIP.
