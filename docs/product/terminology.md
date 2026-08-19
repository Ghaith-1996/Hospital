# Terminology

These terms are implementation and review vocabulary. They do not define hospital policy.

| Term | Meaning | Important distinction |
|---|---|---|
| Original source | The exact text typed by the operator or the exact transcript returned by a transcription provider. | Never overwritten by a structured suggestion or approved message. |
| Transcription | A provider-produced representation of audio. | It is source content, not a clinical conclusion and not a dispatch command. |
| Structured suggestion | A fielded/SBAR-style proposal, with evidence spans, confidence, missing fields, and ambiguities. | It is non-authoritative until a human edits/approves content. |
| Approved content | The exact human-reviewed content associated with a confirmed alert version. | Only this version can be dispatched after explicit confirmation. |
| Critical number | Any numeric value the template or human review identifies as material to the message. | The exact value and unit require separate human confirmation. |
| Unit | The measurement unit associated with a critical number. | A missing, ambiguous, or changed unit blocks confirmation. |
| Recipient | A manually selected fictional practitioner or team reference. | AI may suggest a specialty but never selects the final recipient. |
| Channel | A secure message, SMS, voice, or other approved/simulated delivery path. | SMS/voicemail use generic wake-up content by default. |
| Submitted/accepted by provider | The provider accepted a request for processing. | It is not delivered, opened, acknowledged, or accepted responsibility. |
| Delivered | A normalized provider status that the message reached the provider-defined delivery destination. | It is not proof of opening or human awareness. |
| Opened | The authenticated interface recorded that the alert view was opened. | It is not acknowledgement or responsibility acceptance. |
| Delivery state not applicable | The channel cannot produce a particular state, such as `Opened` for a channel without an authenticated view. | It is not the same as pending, failed, or not yet observed. |
| Delivery state pending/not observed | The system has not yet received a valid event for a supported state. | It is not proof that the state did not occur. |
| Acknowledged | The recipient recorded an acknowledgement action. | It is not acceptance of responsibility. |
| Responsibility accepted | The recipient deliberately accepted responsibility in the workflow. | It is not implied by delivery, opening, or acknowledgement. |
| Escalation | A deterministic, versioned policy evaluation that may create additional delivery work. | AI and background processing may not stop it autonomously. |
| Failed | A durable workflow/delivery outcome that requires operator attention. | It must never disappear silently. |
| Simulation-only assumption | A value used to make fictional Development/Test behavior deterministic. | It must not become a production default. |
| Hospital decision | An explicit, documented, approved real-workflow choice. | Missing values use `REQUIRES_HOSPITAL_DECISION`. |
