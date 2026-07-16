# Performance Baseline — 0.5.0-01

Date: 2026-07-16
Status: **baseline design only; no numerical performance claim**.

## Existing evidence

- 0.4.3 passed functional builds, Bridge tests and installed smoke.
- The web build still reports a Vite main-chunk warning above 500 KB.
- The released verification explicitly excludes full 125%/150% scaling and large-library performance baselines.

Functional success is not a performance result. No cold-start, search, paging, image-memory or large-library benchmark is currently recorded, so this report deliberately records no invented timings.

## Metrics required for 0.5.0 planning

| Area | Measurement | Data set |
|---|---|---|
| Startup | cold start, warm start, first Bridge health, first usable movie wall | empty, current anonymized sample |
| Media wall | first page, next page, sort/filter response, render count | current sample, 5k synthetic |
| Search | query latency and result stability | code/title/actor/tag/path cases |
| Details | navigation and image loading after cache warm-up | representative poster + multi-image records |
| Tasks | scan/sync/image/NFO/organizer queue responsiveness | isolated test roots |
| Memory | 30-minute scrolling/detail switching/image loading | anonymized/synthetic media |

## Guardrails before benchmarking

- Use generated or anonymized fixtures only; never copy private media into repository evidence.
- Measure Bridge, SQLite and React separately when possible.
- Record hardware, display scale, database size, cache state and command used.
- Compare before/after the same workflow; do not use a single favorable run.

## Current performance backlog

1. Route-level code splitting to remove the Vite main-chunk warning.
2. Stable paging/search integration tests before query optimization.
3. Image cache size/expiry and lifecycle measurements.
4. 5,000-item first benchmark during 0.5.0; 50,000–100,000-item stress work remains a 0.5.5 LTS gate.
