# Task 00b: Fast paginated chat list

| | |
|---|---|
| Priority | Urgent. Do before task 01. Added 2026-11-15 after measuring |
| Owner folder | person-3-api-and-pagination |
| Size | 1 person, 1 day (split the projector move into 00b-2 if it does not fit) |
| Area | Performance, Pagination, API |
| Depends on | nothing. Agree the response shape with frontend task 00a |

## What we found

Measured on 2026-11-15 (`docs/reports/performance/2026-11-15-api-response-timings.md`):

- `GET /api/v1/inbox/chats` takes 3.3 to 3.6 seconds alone and 4.8 to 5.8 seconds on the Inbox page.
- It returns 183 KB: all 274 chats of the user (261 case groups, 13 direct chats) in one response, with no paging.
- Opening one conversation is fast (`conversations/{id}/messages`, 130 to 160 ms). The list is the problem.

Cause, in `InboxChatService.ListAsync`:

1. It loads the caller's inbox through `InboxRouter.ListAsync` with a page size of 100. That alone costs about 3 seconds (see task 00c).
2. It runs `InboxChatProjector.ProjectAsync` on every load. The projector writes during a GET: it creates chats, inserts case cards and adds "resolved" lines. Inside its loop it calls the database per item (`ListCaseCardsAsync`, `GetByIdAsync`, journal and name lookups).
3. It reads the caller's conversations twice, before and after the projection.
4. `RowsAsync` runs about eight more queries and builds a row for every chat.

## What to do

1. Make the list a pure read. Move the projection out of the GET:
   - run it when an inbox item is created, moves to a new approver, or is decided (the outbox handlers and `InboxChangedAsync` already mark those moments);
   - keep `ForItemAsync` as the lazy fallback for an item that was never projected.
2. Before removing the call, write a one-time backfill so existing items keep their cards. The deterministic ids already make repeats safe.
3. Page the list: cursor form (`limit`, `before` = last message time and id), default 30, maximum 50. Sort in SQL by pending first, then last message time.
4. Add filters as query parameters: type (direct, group, case), favourites, unread only, search text.
5. Build rows only for the page: member ids, unread counts, names and photos for those 30 conversations, not for all 274.
6. Decide with the product owner whether closed case groups (most of the 261) belong in the default list or behind an "archived" filter.
7. Return the pending summary the UI needs for badges from a small separate endpoint or from the counts endpoint (task 00a), so the list does not need the inbox.

## Done when

- The first page of chats answers in under 200 ms and is under 20 KB for the dapi owner user.
- A GET on the list performs no INSERT, UPDATE or DELETE (check with SQL logging).
- A new leave request still shows its case card in the right chat within a second or two.
- Tests cover: paging order, a decided item gets its resolved line, a reverted decision removes it.
