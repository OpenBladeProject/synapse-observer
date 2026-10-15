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
2. To add an optional recording, open your laptop's controls in Synapse. Start
   both apps normally under the same Windows account, select **Check recording
   availability**, and allow observation for this session. Select **Start
   recording**.
3. Choose an evidence item and enter the exact setting shown in Synapse. Select
   **Begin this step** before changing it.
4. Change one setting in Synapse. Enter the new value and select **I changed it**.
   Restore the exact starting setting in Synapse, check that its controls show
   that value, then select **I restored it**.
5. Use **Other state…** for an unfamiliar value. Select **Skip** or **Setting
   unavailable** for items you cannot test.
6. If you recorded settings, select **Stop recording**. Select **Save evidence
   ZIP…**. A ZIP containing only device information is still useful when
   recording is unavailable or skipped.

The guide covers performance, fans, keyboard lighting and layout, the illuminated
logo, special keys, battery settings, and display controls. For optional M-key/Fn
checks, press the prompted key with the collector window active. Each
check lasts five seconds.

See [what the ZIP contains](docs/capture-contents.md).

### Submit the ZIP

1. Review the ZIP before uploading it to GitHub. A support request and its
   attachments are public. Nothing is uploaded by the collector.
2. Search the existing
   [device support requests](https://github.com/OpenBladeProject/openblade-core/issues?q=is%3Aissue%20label%3Adevice-support)
   for your model number. Attach the ZIP to a matching request, or open the
   [new device support form](https://github.com/OpenBladeProject/openblade-core/issues/new?template=device-support.yml).
3. Include the laptop name and RZ09 model number, BIOS and Synapse versions,
   OpenBlade version if installed, the settings you tested, and anything you
   skipped or could not record.
4. On the new support form, add the Synapse Observer ZIP under **Evidence ZIP**.
   For an existing request, add a comment and attach the ZIP there. If
   OpenBlade's **Help add support** button also saved
   `OpenBlade-identity-report.zip`, attach that file too.
5. Wait for each ZIP to finish uploading, then submit the form or post your
   comment.

## Recording notes

You make and restore settings in Synapse. The collector observes its activity
and records your action markers. Recordings last up to ten minutes. Keep this
window open to record more settings after stopping. If cleanup cannot be
confirmed, the app asks you to restart Synapse before another recording.

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
