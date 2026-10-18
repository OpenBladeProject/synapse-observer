---
title: Observer JSON-plan lexical check reconciliation
author: Jack
date: 2026-10-18
branch: feat/json-test-plan
touches:
  - CHANGELOG.md
---

## Decided
The JSON-plan patch follows the earlier evidence-boundary learning: invalid custom fields, numeric key names and deferred malformed UTF-8 are rejected; filtered activities use their selected object, and key choices are cached on import. Tests enforce these outcomes. No custom instructions execute and no per-refresh key-list allocation is retained.

## Tried and ruled out
Trail check's lexical detector reported six conflicts because docs/tests name rejected syntax and code names cached-key refresh behavior. These matches do not establish a behavioral conflict. The PR description records the source-backed rationale; changing rejection tests or removing documentation to silence word matches would reduce clarity.

## Watch out
Treat the tool as a review aid. Check the actual constructor, reset path, parser catch boundary and cache assignment before judging whether an earlier rule was violated. Keep the imported plan immutable and preserve all live recording and restoration gates.
