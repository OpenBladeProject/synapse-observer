---
title: Observer focused JSON plans preserve evidence boundaries
author: Jack
date: 2026-10-18
branch: feat/json-test-plan
touches:
  - README.md
  - README.ru-RU.md
  - README.zh-CN.md
  - docs/evidence-format.md
  - src/SynapseObserver.App/App.xaml.cs
  - src/SynapseObserver.App/CollectorViewModel.SpecialKeys.cs
  - src/SynapseObserver.App/CollectorViewModel.cs
  - src/SynapseObserver.App/MainWindow.xaml
  - src/SynapseObserver.App/MainWindow.xaml.cs
  - src/SynapseObserver.App/SettingEntry.cs
---

## Decided
Observer imports a strict 16 KiB JSON plan selecting built-in activities and exact prompted-key names. Filter checklist and key picker without granting recording, attachment, input observation or device-control authority. Key-only plans hide the recording guide. Keep baseline/change/restoration guidance, lock the plan once tests start, and export normalized IDs without an import path.

## Tried and ruled out
Filtered activity indexes cannot index the original catalog; reset SettingEntry from the selected activity. Cache filtered key choices instead of allocating on every UI refresh. Reject custom instructions, numeric enum values and deferred malformed UTF-8. JsonDocument can throw during property/value access, after initial parse.

## Watch out
Use immutable parser-created plans and a trusted application allowlist. Do not hide an unfinished restoration by changing plans. Review requested items and unattempted keys without treating unrequested checklist items as failed tests; global support coverage remains NotInvestigated. Passive recording may retain other eligible calls from the selected laptop. Disconnected previews do not establish live Synapse behavior.
