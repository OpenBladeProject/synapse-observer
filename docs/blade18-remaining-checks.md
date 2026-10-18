# Blade 18 remaining checks

This plan targets Blade 18 RZ09-0484 after the three reviewed Synapse Observer
bundles and the later exact-model source recovery in OpenBlade commit
`164540baff9abf723ed8578b584021ad2fc9d41b`. It asks for no repeat of the
captured identity, Synapse performance, Balanced fan, charge-limit, brightness,
Fire, Wheel, or logo actions.

Import [the plan](examples/blade18-remaining-checks.json) into an Observer build
that includes schema version 2 from [PR #23](https://github.com/OpenBladeProject/synapse-observer/pull/23)
and PDH temperature collection from [PR #21](https://github.com/OpenBladeProject/synapse-observer/pull/21).
Public version 0.2.11 cannot import this plan. The only Observer activity is the read-only
`sensor-telemetry` current-state report. Stop the Synapse recording in Observer,
then close Synapse before using OpenBlade, but leave Observer open for its manual check
reports. Observer must not capture OpenBlade HID traffic. Record the OpenBlade version in the starting-settings entry for the first check.
The results retain detected model, starting state, result, and restoration
as user-reported provenance. They are
not Observer firmware evidence.

For each OpenBlade check, save the exact starting state, perform only the listed
typed action, confirm the reported result, restore the saved state, and confirm
restoration. Stop further mutations after an ambiguous submission or readback.
Skip a conditional control when OpenBlade does not expose it after its required
getter. The plan requires no power cycle, sleep, driver installation, raw HID
capture, or untyped command.

## Included OpenBlade checks

| Check | Why it remains | Evidence |
| --- | --- | --- |
| `custom-level-readback` | Existing bundles show requests, not independent firmware application or restoration. One Boost and Low round trip exercises both clusters without retesting all twelve pairs. | `OpenSynapse/docs/blade18-observer-evidence.md:24-29`, `:70-82` |
| `balanced-curve-behavior` | The UI has one linked fixed slider. Separate target requests come from the CPU and GPU curve editors and require usable temperatures. Missing data safely holds the maximum; the owner should not create load to move between points. | `OpenSynapse/src/OpenBlade.Protocol/ExperimentalBladePort.Fans.cs:95-109`, `OpenSynapse/src/OpenBlade.Service/FanCurveControl.cs:16-80`, `OpenSynapse/docs/experimental-blade-reference.md:55-65` |
| `custom-automatic-fan-handoff` | The owner can confirm only the settled Custom and Automatic result plus restored saved intent. Integration tests, rather than this manual check, prove internal write ordering. | `OpenSynapse/docs/blade18-source-feature-recovery.md:146-151`, `OpenSynapse/docs/experimental-blade-reference.md:98-118` |
| `silent-fixed-fan-range` | Silent fixed control is newly admitted at 3700 to 5300 RPM and has no owner round trip. Balanced capture does not validate this preset-specific range. | `OpenSynapse/docs/blade18-source-feature-recovery.md:133-144` |
| `maximum-fans-round-trip` | The transport validator is fixed, but enablement and readback remain unverified. A guarded block is a result; the owner must not turn off another Power Mode option to force admission. | `OpenSynapse/docs/blade18-observer-evidence.md:84-88`, `:157-162`; `OpenSynapse/docs/experimental-blade-reference.md:67-80` |
| `lighting-per-key-sequence` | Exact regional maps now admit per-key colors and bounded sequence playback. Physical key placement, final-frame hold, and baseline restoration remain untested. | `OpenSynapse/docs/blade18-spatial-lighting.md:9-42`, `:136-151` |
| `lighting-breathing-wave` | Dual-color Breathing and leftward Wave are new host-rendered patterns over the exact map. One representative run covers this software path. | `OpenSynapse/docs/blade18-spatial-lighting.md:153-159` |
| `lighting-spatial-effects` | Reactive, Ripple, and Tidal use different recovered spatial behavior. One key pulse and one wave direction cover the material paths without a key-by-key matrix. | `OpenSynapse/docs/blade18-spatial-lighting.md:80-117`, `:119-134` |
| `fn-service-display-actions` | Fn plus P and Fn plus R reach distinct service and display controllers. Live ordering, single action delivery, and duplicate suppression remain unverified. | `OpenSynapse/docs/blade18-fn-decoder.md:81-101`, `:127-143` |
| `fn-windows-actions` | Touchpad, media, and navigation provide a small sample of the remaining current-user action paths. Every Fn key is unnecessary. | `OpenSynapse/docs/blade18-fn-decoder.md:83-109`, `:132-143` |
| `gaming-mode-host-policy` | The implemented feature is an opt-in host-wide Windows-key and Alt-Tab policy, not firmware Gaming Mode. Its enable and release behavior still needs a live check. | `OpenSynapse/docs/blade18-source-feature-recovery.md:46-61`, `OpenSynapse/docs/experimental-blade-support.md:69-71` |
| `startup-animation-state` | The inherited getter and setter are admitted only after a valid read. A toggle and restore can validate state handling without a power cycle; boot appearance remains outside this plan. | `OpenSynapse/docs/blade18-source-feature-recovery.md:217-233` |
| `temporary-full-charge` | The prior bundle never enabled bit 04. Run only with existing active protection, connected power, and battery at or above the limit but below 100 percent. Do not change policy or wait through a charge cycle. An unchanged charging status does not prove the control is unsupported. | `OpenSynapse/docs/blade18-source-feature-recovery.md:235-261`, `OpenSynapse/docs/experimental-blade-reference.md:168-172` |

## Included maintainer research

These entries are not laptop-owner mutation requests. They preserve unresolved
source and native-component questions as maintainer work.

| Check | Why it is research only | Evidence |
| --- | --- | --- |
| `research-fn-primary-abi` | The present hook cannot safely suppress only this laptop before device identity is known. Default Fn shortcuts do not admit always-on function-primary inversion. | `OpenSynapse/docs/blade18-source-feature-recovery.md:263-276` |
| `research-firmware-gaming-mode` | Product 672 supports the host mapping policy but lacks the reviewed firmware Gaming Mode feature gate. | `OpenSynapse/docs/blade18-source-feature-recovery.md:40-61` |
| `research-thx-legacy-stack` | This 2023 model uses a legacy THX path; no exact APO, codec binding, client ABI, or complete component pin is available. | `OpenSynapse/docs/blade18-source-feature-recovery.md:109-131` |
| `research-snaptap-abi` | A signed PID02A0 stack exists, but the installed hash differs and the public interception and output ABI is unproved. | `OpenSynapse/docs/blade18-source-feature-recovery.md:63-107` |
| `research-hyperboost` | Shared HyperBoost code exists, but exact Product 672 supplies no enabling feature flag or admitted setter. | `OpenSynapse/docs/blade18-source-feature-recovery.md:40-49`, `OpenSynapse/docs/experimental-blade-reference.md:138-139` |
| `research-custom-performance-fixed-fans` | The declared Custom range is inactive without its missing feature gate, and no Performance manual range was recovered. | `OpenSynapse/docs/blade18-source-feature-recovery.md:146-151`, `:209-215` |
| `research-silent-curves` | Silent fixed targets are admitted, while the curve backend and its temperature contract remain Balanced-only. | `OpenSynapse/docs/blade18-source-feature-recovery.md:133-144`, `:188-191` |
| `research-cpu-temperature-fallback` | The old bundle queried WMI, not OpenBlade's two PDH counters. The focused current-state report can resolve that concrete gap before another provider is considered. | `OpenSynapse/docs/blade18-source-feature-recovery.md:153-191` |

## Covered and deliberately omitted work

- The existing bundles already cover the exact identity, Synapse Custom CPU and
  GPU actions, Balanced Automatic and fixed fans, charge limit, keyboard
  brightness, Fire, Wheel, and logo transitions. Their retained scope is listed
  in `covered`; none needs another Synapse action recording.
  `OpenSynapse/docs/blade18-observer-evidence.md:113-169`, `:171-198`, `:287-317`.
- The exact-model Fn decoder and default map are recovered statically, so
  `specialKeys` stays empty. A few manual OpenBlade outcomes test the live path;
  another raw key mapping capture would repeat source evidence.
  `OpenSynapse/docs/blade18-observer-evidence.md:238-285` and
  `OpenSynapse/docs/blade18-fn-decoder.md:24-79`.
- Fire, Wheel, and logo states are not repeated in Synapse. The new lighting checks cover only the
  later per-key, sequence, host-rendered, and spatial paths. The regional maps
  and frame transport are already source-backed; the remaining question is
  physical appearance and restoration through OpenBlade.
  `OpenSynapse/docs/blade18-observer-evidence.md:287-324` and
  `OpenSynapse/docs/blade18-spatial-lighting.md:119-159`.
- No exhaustive Custom pair, fan value, keyboard region, or Fn-key matrix is
  requested. The implementation shares typed paths, and the selected checks
  exercise each materially different path once.
- Temporary full charge is not requested from Synapse again. The prior recording
  contains no enable request, while the current OpenBlade control has guarded
  readback and restoration. Startup animation is likewise checked only through
  OpenBlade when its getter admits the control.
- HyperBoost, function-primary mode, firmware Gaming Mode, THX, SnapTap, fixed
  fans in Custom or Performance, Silent curves, and a CPU-temperature fallback
  remain research entries. The plan does not turn missing source contracts into
  owner experiments.
