# Task 00a Monitoring Count Design

## Goal

Replace the Monitoring provider's list-derived badge count with an aggregate database count while preserving the exact active-case visibility of `ListAsync`.

## Chosen design

The provider reads only distinct candidate employee identifiers needed by the authority resolver. `ExceptionCaseRouting` converts the resolved scope into count criteria: exact approver employee IDs, held case IDs, HR no-approver employee IDs, and journaled escalation IDs. Conversation membership contributes added case IDs. `IExceptionRepository` applies the union of own, routed, held, legacy-HR-escalated, and chat-added visibility to one active non-calendar `COUNT` query, so duplicate visibility paths count once.

No exception entity, employee display data, action, evidence, or `InboxItem` is built on the count path. The providers continue to run sequentially and no cache is added (DEC-053).

## Rejected alternatives

- `GetListTotalCountAsync` alone: it cannot express exact approver, escalation-holder, legacy HR fallback, and chat-added visibility.
- Reusing `VisibleActiveCasesAsync`: it materializes up to 500 full cases and is the measured bottleneck.
- Broad manager/HR scope counting: it changes the routing contract and exposes badge totals for cases the caller does not own.

## Verification

Unit tests prove the provider delegates to the aggregate count and never calls list/detail reads. Repository tests cover own, routed employee, held case, legacy HR, added case, calendar/resolved exclusion, tenant isolation, and duplicate visibility. Existing list-routing tests remain the semantic guard.
