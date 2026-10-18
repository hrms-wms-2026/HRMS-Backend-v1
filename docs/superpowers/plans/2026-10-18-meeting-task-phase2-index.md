# Meetings as Tasks — Phase 2 Plan Index

**Spec:** `docs/superpowers/specs/2026-10-18-meeting-task-phase2-design.md`
**Base branch:** `feature/meeting-phase0-port`.
**Prerequisite:** P-1, the outbox claim fix on `fix/outbox-concurrency`, which runs separately.

The spec spans several subsystems, so it is split into five plans. Each plan produces working, tested software on its own and merges in the order below. Every plan after A starts from the previous plan's branch.

| Plan | Scope (spec sections) | Depends on | Ships | Estimate (inline) |
|---|---|---|---|---|
| **A — Foundation** ([plan](2026-10-18-meeting-task-phase2-A-foundation.md)) | §2.2 filter; §4 schema + RLS + triggers; §5.6 insert SQL (closes the open item); `job_leases`; options/flags; Release A runbook | port branch | **Release A** (safe floor, all flags off) | ~1 day |
| **B — Meeting core (backend)** | §5.1 `IMeetingAccessResolver` + `MeetingVisibility`; §5.2 create (direct / `meeting.create` approval / Office), Stale rules; §5.3 edit/cancel/attendees/RSVP; §5.5 splits, forward-only moves, suppressions, the 7-day generator (on `job_leases`); `/meetings` endpoints + §7 error codes; ShortId `M` counter; leak tests | A | Release B core (behind `Meetings:Create`) | ~2 days |
| **C — Calendar engine for series owners** | `MeetingOwner` abstraction across `MeetingProvisioner`, `EventMeetingLinkService`, sender keys/.ics (§3, §5.7: shared links, references, "needed" rule); per-occurrence attendance (§5.8: sessions, union, statuses, checkpoints, recheck); C2 engine `IEmployeeMeetingOccurrenceSource`, reminders, RSVP history; external push (§5.10); `GET /calendar` merge + conflicts (§5.9); Phase 0 parity runs for both owner kinds | B | Links, attendance and engine for Phase 2 meetings | ~2 days |
| **D — Action items, bulk invite, attendance views** | §5.4 action items (optional `sourceMeetingOccurrenceId` on `task.create`); §5.2 bulk preview/expansion (D8; needs P-1 before `Meetings:BulkInvite`); D16/D17 `GET /employees/{id}/meeting-attendance` with private masking; monitoring compliance masking | C (+ P-1 for the bulk flag) | Remaining backend features | ~1 day |
| **E — Frontend** | Shared meeting form (project/Office, module picker, bulk preview with counts, recurrence, platform); board card (status pill, no drag); My Work "Today's meetings"; calendar merge (virtual ids, suppressions); approval snapshot view; attendance panels (organizer, self, manager; "Unknown" vs "No join recorded"); action items; notes after the meeting | B–D APIs | UI | ~2 days |

**Release order:** P-1 → A (Release A) → B + C + D + E (Release B, flag by flag). Never roll back below Release A once meeting rows exist (spec §8).

**Writing the next plans.** Plan B is written once Plan A's interfaces have landed, so Plan B consumes the real types (`WorkTaskQueryFilters`, `IMeetingOccurrenceWriter`, `IJobLeaseService`, `MeetingsOptions`, the entities). Plans C, D and E follow the same rule.
