# Task 19: Validation coverage and shared rules

| | |
|---|---|
| Priority | 19 of 30 (lower number = build first) |
| Owner folder | person-1-security-and-auth |
| Size | 1 person, 1 day |
| Area | Security, Code quality |
| Depends on | nothing |

## What we found

- `ONEVO.Application` has 322 command files and 145 validator files. More than half of the commands reach their handler with no FluentValidation rules.
- The duplicate-code scan shows Create and Update validators copied line for line, for example `CreateClockInPolicyCommandValidator` and `UpdateClockInPolicyCommandValidator` (31 lines), `CreatePositionCommandValidator` and `UpdatePositionCommandValidator` (25 lines).
- String length limits and rich text checks are repeated by hand. `CLAUDE.md` requires `RichTextRules.HasActiveContent` on every user-written rich text field, and that is easy to forget.

## What to do

1. Produce the list of commands with no validator. Sort by risk: anything that writes user text, money, dates or ids from the request first.
2. Add validators for the top 30. Every string gets a maximum length that matches the column. Every id list gets a maximum count. Every date range gets a maximum span.
3. Create shared rule extensions (`.ValidName()`, `.ValidRichText()`, `.ValidDateRange()`, `.ValidPageRequest()`) and use them in the new validators.
4. For each Create and Update pair, move the common rules into one shared base or `Include(...)`.
5. Add an architecture test: every command that has a string property has a validator.
6. Leave frontend validation alone. It serves the user; this serves security.

## Done when

- The 30 highest-risk commands have validators with tests.
- The architecture test exists, with an explicit allow-list for the commands still to do.
- The remaining list is saved as a follow-up task file.
