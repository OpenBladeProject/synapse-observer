---
title: Observer documentation parity is a required CI gate
author: Jack
date: 2026-10-18
branch: docs/chroma-studio-validation
touches:
  - docs/ru-RU/VALIDATION.md
  - docs/zh-CN/VALIDATION.md
  - tests/Test-DocumentationLocales.ps1
---

## Decided
Run tests/Test-DocumentationLocales.ps1 before pushing documentation changes. English and translated evidence-format and VALIDATION sections must preserve paragraph coverage, headings and English fragment anchors, inline literals, numeric facts, code, list structure, URLs and links. Translate the full technical record rather than adding a condensed locale summary.

## Tried and ruled out
The Observer PRs failed before compilation because appended English sections had no full matching locale sections. Local solution tests alone did not exercise this first CI gate. Correct the translated content; retain the locale checker.

## Watch out
Live Chroma Studio validation remains scoped to the documented package, laptop and observed path. A successful capture does not erase the earlier unresolved freeze or establish firmware readback. Reuse unchanged evidence without claiming that new code was physically exercised.
