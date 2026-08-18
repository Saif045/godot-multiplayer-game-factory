# Architecture Decision Records

Architecture decision records capture durable choices that materially constrain the project. They explain why a choice was made, its consequences, and what evidence would justify revisiting it.

## Current index

No architecture decision records have been accepted yet.

The charter and architecture documentation contain project principles and current observations. They are not substitutes for a reviewed ADR when a concrete design choice becomes durable.

## Status vocabulary

- **Proposed** — ready for review but not binding.
- **Accepted** — the active architectural decision.
- **Superseded** — replaced by a later ADR; link both records.
- **Deprecated** — retained for context but no longer recommended.
- **Rejected** — considered and deliberately not adopted.

## Process

1. Copy [adr-template.md](adr-template.md) to a numbered, descriptive filename such as `0001-example-topic.md`.
2. Describe the observed context and constraints without presenting planned behavior as implemented.
3. State one concrete decision and its scope.
4. Record alternatives, tradeoffs, consequences, and validation evidence.
5. Submit the record as `Proposed` with the relevant implementation or preparatory change.
6. Change it to `Accepted` only after architectural review.
7. Add accepted and historical records to this index.
8. Supersede rather than rewrite an accepted decision when its reasoning changes materially.

## Candidate topics

The following topics may deserve ADRs after design work. Listing them here does not accept a decision:

- composition-root form and lifetime;
- ownership between `NetworkSession` and `INetworkTransport`;
- structured error-code boundaries;
- replication automatic/manual/replacement modes;
- persistent player identity;
- runtime network-object identity and spawn-definition stability;
- testing framework and Godot integration approach;
- multiprocess scenario protocol and checkpoint format;
- compatibility and versioning policy.

## Naming

Use four-digit sequence numbers and short kebab-case topics. Sequence numbers identify records; they do not imply that every earlier proposal was accepted.
