# ADR-0004: Keep SMS and Voicemail Generic by Default

- Status: Mandatory safety decision for the simulation and default product behavior.
- Date: 2026-08-19.
- Deciders: User-requested project rule; hospital communications/privacy approval remains required for production.

## Context

SMS and voicemail may be exposed on lock screens, shared devices, previews, or untrusted channels. They are not the authenticated secure interface and are not appropriate places for full clinical details by default.

## Decision

SMS and voicemail adapters receive generic wake-up content only by default. Detailed case content remains behind the authenticated interface or another hospital-approved secure channel. The provider request includes a secure-message reference or generic message, an opaque endpoint reference (never a raw phone number or email address), a stable idempotency key, and a correlation ID; it does not include the full clinical body by default.

## Consequences

Positive:

- Accidental disclosure through previews, recordings, or shared endpoints is reduced.
- Provider adapters have a narrow, testable contract.
- Simulation can prove delivery state without real clinical payloads.

Trade-offs:

- Recipients need authenticated access to see details.
- The generic message wording must be approved and tested for accessibility and usefulness.
- The hospital may require additional secure channels or an exception process.

## Guardrails

- No clinical payloads, patient references, tokens, or contact numbers in URLs or logs.
- Full case text is not passed to wake-up adapters unless a separately approved policy explicitly changes the contract.
- Provider callbacks are untrusted and are normalized without trusting message bodies.
- Simulation providers use only fictional endpoints and generic content.

## Not decided here

Exact wake-up wording, languages, sender identity, provider, rate limits, voicemail script, retention, and any exception are `REQUIRES_HOSPITAL_DECISION`.
