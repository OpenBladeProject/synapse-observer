# Lighting, Fn keys and fan replies

Use the current collector for these focused follow-ups. Keep Synapse and the
collector open. Start recording before beginning a setting-change step. Each
step needs its starting value, changed value and restoration marker; selecting
an item alone does not record that sequence.

1. **Chroma effects and frame streaming:** record the starting effect and its
   settings, begin the step, then select one other effect. Let it run for five
   seconds before marking the change. Restore the exact starting effect and
   settings, wait five seconds and mark restoration. Repeat for another effect
   if needed. The ZIP can retain 16-column and 17-column RGB rows; the effect
   name comes from your markers. Rows alone do not establish a physical key map.
2. **Logo lighting:** begin this item before changing only the logo control.
   Wait five seconds after changing and restoring it. Use Static, Breathing,
   Off or the exact setting offered. If Synapse has no logo control, select
   **Setting unavailable**. Reviewed logo requests and returned state/effect
   summaries are retained separately from keyboard lighting.
3. **Fn keys:** select the Fn evidence item and open the focused key check.
   Select the exact key or chord, then press only that key while the collector
   stays active for five seconds. Repeat for the brightness or other Fn-row
   chords needed. Note the starting setting first and restore it if a shortcut
   changes it. Fn alone may produce no Windows input. A received F-key, letter
   or media signal does not prove that Fn was held or what action it performed.
4. **Fan setting:** record the performance mode and each fan setting needed
   for restoration. Leave the fan controls open for five seconds before the
   change, after the change and after restoration. This gives Synapse time to
   make its own queries. A returned measured RPM is distinct from a returned
   saved speed target. The collector never sends a query itself.

Stop recording and save the ZIP after finishing the restoration markers.
Missing replies and completed key checks with no input are useful gaps; they
are not proof that a laptop lacks a feature. These observations do not confirm
firmware application or restoration.

## Maintainer evidence

The 0.2.10 Blade 18 bundle showed `03/0B` rows with a 52-byte payload. The old
privacy grammar accepted only 55 bytes. The added grammar requires the reviewed
`FF, row, 00, end-column` header, six bounded rows, exactly 48 or 51 RGB bytes,
and the existing complete-envelope checks. It does not import another model's
physical layout or enable writes.

The 16-column reference is
[ExperimentalBladeLightingFrame.cs at 4912d46](https://github.com/OpenBladeProject/openblade-core/blob/4912d467898462441f67dc2d42c57453df4b4c7f/src/OpenBlade.Protocol/ExperimentalBladeLightingFrame.cs).
The vendor's `setLedMatrixRGBDefinePattern` and `setLedMatrixRGBEffect` in
[product 672's shared backend](https://apps.razer.com/synapse/products/672/mw/rzDevice25.85ce8530211c29f3c304.js)
define row data and used-length numeric/RGB effect payloads. That source's
SHA-256 is `DFB3E3612D36B20910AF02E61A17ED64A1FFD3781E09C63BAAEE7CBEFDEBCA24`.
The pinned vendor policy's logo path calls `setLedEffect` followed by
`setLedState`, using logo ID 4 and storage class 0 or 1; its hash is
`651F63DCC0EFC92B0FD7496EE70751707B5F650F20AF5C01C3255B61817C1997`.

Some completed GET_FEATURE calls return 90 bytes: an excluded leading byte
followed by the logical packet through its checksum, without the last padding
byte. Reviewed numeric summaries preserve that incompleteness and user-mode
provenance. They do not manufacture a complete report. Actual fan RPM, stored
fan target, thermal mode, Custom level, logo state/effect and keyboard brightness
remain distinct response kinds. A setter acknowledgement never counts as a
getter. No stored-target reply was present in the supplied Blade 18 follow-up;
retaining its reviewed shape does not establish that Synapse uses it on that
model.

The supplied fan queries use `HidD_SetFeature` to send the full high-bit getter
command, followed by `DeviceIoControl.HID_GET_FEATURE` to receive its reply.
Native GET call records contain only the report-ID input byte. Correlation uses
the reviewed command, selected interface, transaction, selector and ordering;
the transport name alone does not identify a getter request. Synthetic tests
exercise this split request/reply sequence.

`07/0F` and `07/8F` are retained as a shared power-mode bitmask. A zero byte or
an unrelated bit does not establish temporary full-charge support.
