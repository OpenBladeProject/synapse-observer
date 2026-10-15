# Synapse Observer

Help add support for your Razer Blade to [OpenBlade](https://github.com/OpenBladeProject/openblade-core)
by collecting information about your laptop and recording setting changes in
Synapse. You can save and send the results without a terminal or repository checkout.

[Download the latest release](https://github.com/OpenBladeProject/synapse-observer/releases/latest),
extract the ZIP, and open `SynapseObserver.exe` normally.

## What you need

- Windows 11, x64.
- [.NET 10 Desktop Runtime, x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
  It is a separate download. Windows offers an installation link if it is missing.
- Synapse for recording setting changes. You can collect device information
  without it.

You can collect information from Blade, Blade Pro, and Blade Stealth laptops.
Recording availability depends on how Synapse communicates with your laptop.
See [model coverage](docs/model-coverage.md).

## Send evidence

1. Open the app and select **Collect device information**.
2. To add a recording, open your laptop's controls in Synapse. Start both apps
   normally under the same Windows account, check recording availability, and
   allow observation for this session. Select **Start recording**.
3. Choose an item and its starting setting, then select **Begin this step**.
4. Change one setting in Synapse, then mark its new value in the collector.
   Restore the starting setting in Synapse and mark restoration.
5. Use **Other state…** for an unfamiliar value. Skip items you cannot find.
6. Stop recording, save the evidence ZIP, and attach it to your support request.

The guide covers performance, fans, keyboard lighting and layout, the illuminated
logo, special keys, battery settings, and display controls. For optional M-key/Fn
checks, press the prompted key with the collector window active. Each
check lasts five seconds.

A device-information ZIP is still useful when recording cannot start. See
[what the ZIP contains](docs/capture-contents.md).

## Recording notes

You make and restore settings in Synapse. The collector observes its activity
and records your action markers. Recordings last up to five minutes. Restart
Synapse normally before starting another recording.

Nothing is uploaded automatically. Review the ZIP before sharing it. Ordinary
typing, macros, mouse input, device paths, and serial-number inventory fields
are excluded. Unfamiliar or potentially identifying command contents are omitted;
reviewed numeric and lighting reports can include their bytes.

This is an unsigned development build. Live trials have recorded performance
changes on one Blade 16; other laptops and Synapse backends still need testing.
Keep security protections enabled. Maintainers review the evidence and test
support separately; a recording does not confirm that a setting reached the
laptop's firmware.

## Further details

Read the [evidence format](docs/evidence-format.md),
[support evidence requirements](docs/support-evidence-requirements.md), or
[validation status](VALIDATION.md). For development, see
[building and verification](docs/building.md) and the
[native observer notes](native/README.md).

Licensed under [GPL-3.0-or-later](LICENSE). Dependency notices are included in the ZIP.
