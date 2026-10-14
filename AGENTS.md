# Synapse Observer agent guide

Build a standalone Windows evidence collector for Razer Blade laptop owners.
The goal is a short, guided session that produces useful, sanitized evidence
without a terminal, SDK, repository checkout, or maintainer assistance.

## Scope and authority

- This is collection software, not a device controller. Never send, replay,
  alter, or synthesize a HID report, IOCTL, or firmware command.
- Windows identity, descriptors, report geometry, installed software metadata,
  and bounded observations of calls made by Synapse are collection inputs.
- Unknown models and command shapes are discovery inputs, never admission for
  writes. Do not copy a known model's PID, interface, report geometry, or bytes.
- Do not stop OpenBlade, Synapse, services, or drivers automatically. Refuse
  incompatible concurrent capture and explain the required user action.
- Live attachment to a vendor process must be explicitly authorized. Source
  development, builds, tests with owned synthetic processes, and disconnected
  GUI previews do not authorize attachment to Synapse or hardware experiments.
- Never request disabling antivirus, tamper protection, code integrity, or
  process protection. A refused attachment is a useful diagnostic result.
- No driver installation, sleep/resume testing, automatic uploads, generic
  elevation broker, arbitrary target injection, or arbitrary remote commands.

## Evidence and privacy

- Label the observation layer: user-mode call, returned buffer, USB transfer,
  or user-reported setting. A successful API call is not firmware readback.
- Keep request input, completed response, return status, errors, timing,
  provenance, observer gaps, and incomplete restoration distinguishable.
- Retain unfamiliar control reports and unmatched operations within documented
  bounds. Do not silently discard them because they are not known commands.
- Capture only the selected laptop's control feature reports. The optional
  special-key probe may observe only the selected Blade's input interfaces for
  an explicitly prompted key while this window is foreground, for at most ten
  seconds. Export only reviewed non-text key identifiers or unknown-report
  shape and comparison metadata; omit ordinary typing, macro contents, raw
  input bodies, and mouse input. Never register background input, suppress keys,
  change keyboard mode, or record arbitrary process memory or arbitrary IOCTLs.
- Raw files stay local. Export only schema-defined fields. Remove serials,
  instance paths, usernames, local paths, unrelated device data, and accounts.
  Hashing a path is not sanitization of every payload containing that path.
- Export is explicit. Save partial results when collection fails. Report gaps
  plainly; zero observations does not mean the hardware lacks a capability.
- Restore instructions refer to the exact user-reported starting setting.
  Do not apply defaults or claim verified restoration from a button press.

## Workflow

- Inspect status and instructions first. Preserve unrelated dirty files.
- Use a dedicated worktree from main, with purpose branches such as feat/,
  fix/, docs/, and chore/. Do not implement in the landing clone.
- Link implementation to tracking and deliver through a draft pull request.
- Use authenticated gh for GitHub. Label new issues appropriately.
- Keep native observer, collector core, and UI boundaries small and explicit.
  No installed OpenBlade dependencies, service IPC changes, or product controls.
- Use up to two independent bounded workers when useful. Select model and
  reasoning effort explicitly; no recursive delegation. Main owns integration.
- Pin external source dependencies and preserve their license and provenance.
  Do not silently download or execute third-party instrumentation binaries.
- Test bounds, parser failures, privacy exclusions, process/device scope,
  cancellation, completed responses, partial export, and detach failure.
  Synthetic tests prove only the exercised implementation, not Synapse coverage.
- Build and inspect the GUI. Clearly separate disconnected preview evidence
  from a live capture. Keep the PR draft until live observation is checked.
- Use Conventional Commits and CRLF text files. Verify every changed file's
  declared endings before committing. Commit and push coherent slices.
- Update CHANGELOG.md for user-visible changes, linking the implementing PR.
- After merge/closure, verify state, preserve artifacts, remove the worktree,
  delete the verified branch, and fast-forward the landing clone safely.

## User experience

- One primary action per screen. Use native controls, visible focus, keyboard
  navigation, readable contrast, concise text, and clear busy/error feedback.
- Prefer automatic discovery and progressive disclosure to technical choices.
  Explain a concrete failure with the shortest useful next action.
- Inventory remains useful without Synapse. Every recording item can be skipped.
- No support promises, hidden uploads, fake command verdicts, or silent success.
