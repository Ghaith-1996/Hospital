# Definition of Done

This is the active gate checklist. Per-phase scope, evidence and verification counts are in the [development history](../archive/development-history.md); approvals, tags and acceptance are distinguished in [phase approval evidence](phase-approval-evidence.md). No blanket Phase 0-8 approval is claimed.

## Gate pattern

Every phase or change set reports, then stops for owner review:

1. Files changed.
2. Architectural decisions.
3. Commands run and results.
4. Tests added and results.
5. Known limitations and unresolved human decisions.
6. Human actions required.
7. Proposed commit message.
8. A clear stop for review before the next phase.

Technical closure, owner continuation/publication authorization, tags and final acceptance are separate records. Implementation work never self-approves.

## Required checks

A change is done only when all of the following hold:

- [ ] Tests were written first and cover the behavior; real PostgreSQL (Testcontainers) is used for relational behavior, not an in-memory substitute.
- [ ] `scripts/test-all.ps1` passes: locked restore, format verification, Release build with no warnings, backend tests, dependency scans, OpenAPI contract verification, web tests, typecheck, lint, Chromium flows, API/worker/web container builds, `scripts/verify-web-container.ps1`, and the sensitive-data scan.
- [ ] The connected system harness (`npm run web:e2e:system`) passes against isolated PostgreSQL, migrations, the real worker and the production web build.
- [ ] Each [safety invariant](../security/safety-invariant-traceability.md) touched has a documented test or design control.
- [ ] No real PHI, employee data, contact data, secrets or sensitive screenshots; fictional data only (`555` phone values).
- [ ] No persistent browser workflow storage; the browser holds unsaved edits only.
- [ ] No real provider, hospital integration, external callback or production identity was added.
- [ ] Documentation and tests are updated in the same change; each behavior is labelled simulation-only or an approved human decision.
- [ ] Every missing hospital decision uses the exact marker `REQUIRES_HOSPITAL_DECISION`; no production timing, authority, escalation, privacy, retention or fallback value was invented.
- [ ] Anything not run is stated with the reason; a missing prerequisite (for example Docker) is a limitation, not a pass.

## Standing safety expectations

- Dispatch requires authenticated human confirmation of the exact alert version, message, critical values and units, recipients, channels and policy version; any edit invalidates approval.
- Original source, structured suggestions and approved content stay separate; critical numbers and units are unresolved until a human confirms them.
- Delivered, opened, acknowledged and responsibility accepted stay separate; unsupported states are `NotApplicable`.
- SMS and voicemail carry generic wake-up wording only by default.
- Failed deliveries and outages remain visible; no hidden fallback route exists.
- AI and speech features default disabled, typing remains primary, raw audio is never retained, and human Apply precedes any draft change ([speech and AI suggestions](../architecture/speech-and-ai-suggestions.md)).

## Open items

- [ ] Phase 0 package approval by a human is not recorded (also blank in [product decisions](product-decisions.md); ADRs 0001, 0002 and 0005 remain proposed).
- [ ] Project owner acceptance of the integrated Phase 0-8 simulation baseline is not recorded.
- [ ] Project owner acceptance of Phase 9 and of Phase 11 is pending; the Phase 11 gate evidence is in [its verification record](../superpowers/phase11-verification.md).
- [ ] GitHub rulesets/branch protection are not configured; this is a repository-administrator action. The repository must remain public.
- [ ] Every production decision stays `REQUIRES_HOSPITAL_DECISION` until an authorized human approves it; see [production readiness gates](../security/production-readiness-gates.md).
