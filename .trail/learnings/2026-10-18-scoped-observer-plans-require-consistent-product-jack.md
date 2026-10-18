---
title: Scoped Observer plans require consistent product identity
author: Jack
date: 2026-10-18
branch: feat/json-test-plan
touches:
  - src/SynapseObserver.Core/CaptureTestPlan.cs
  - tests/SynapseObserver.Core.Tests/CaptureTestPlanTests.cs
---

## Decided
Model-scoped follow-up plans require complete machine identity with at least one usable product row and agreement across every row. A single matching registry result cannot outweigh another model, missing product or partial collection. Generic plans remain usable on incomplete identity; target matching retains legitimate product and SKU suffixes.

## Tried and ruled out
Any matching row admitted model-specific owner instructions on mixed or stale registry evidence. Observer does not issue hardware writes, but that still mislabeled owner evidence and could direct the wrong owner through a model-specific plan.

## Watch out
Keep target matching separate from OpenBlade hardware admission. Owner reports never become firmware readback. The native observer and live attachment path did not change in the JSON-plan work, so the documented Chroma Studio live evidence can be reused; new identity, parser, report and export behavior is validated by focused software tests and disconnected UI checks.
