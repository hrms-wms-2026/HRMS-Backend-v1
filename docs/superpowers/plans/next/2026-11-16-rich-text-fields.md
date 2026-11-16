# Rich Text Fields — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Use one rich text editor for legal document content and support ticket descriptions and comments (admin and tenant), backed by one HTML allowlist on the backend, by reusing the tenant app's existing `app-rich-text-editor` instead of building a new one.

**Architecture:** The tenant app's editor is made self-contained (no Work module imports) and gains a `basic` toolset that only produces allowlisted markup. The same component folder is then copied unchanged into the admin console. A small `app-rich-text-view` renders stored rich text (or old plain text) safely in both apps. On the backend, `LegalHtmlValidator`'s allowlist becomes a shared `BasicRichHtml` helper used by legal documents and support, with visible-text length limits and plain-text conversion for emails.

**Tech Stack:** .NET 10 (xUnit, Moq, FluentAssertions); Angular 21 with DOMPurify 3 (tenant: Vitest via `ng test`; admin: Jest).

**Spec:** Decision DEC-035 in Dabi's decisions log (2026-11-16), which approved this field list:
- Rich text: legal document content; support ticket description and comments (admin and tenant); incident postmortem fields (built in the incident plans).
- Plain multi-line text (unchanged): announcement body, incident customer updates and notes, tray release notes, short descriptions.
- Code editors (unchanged, never rich text): notification email HTML/CSS, configuration payload JSON, OAuth private key.

**Repos and order:**
1. Backend `HRMS-Backend-v1`, branch `feat/rich-text-fields-backend` from `development` **after backend #227 is merged**.
2. Tenant `Hrms--Web-application---front-end---v1`, branch `feat/rich-text-fields-tenant` from `Development` **after tenant #160 is merged**.
3. Admin `platform-administration`, branch `feat/rich-text-fields-admin` from `development`, after the tenant branch's Task 3 commit exists (it copies those files).

## Global Constraints

- **One allowlist everywhere.** Tags exactly: `p, br, strong, b, em, i, u, s, h1, h2, h3, h4, ul, ol, li, blockquote, a, code, pre, table, thead, tbody, tr, th, td`. Attributes exactly: `a[href, title]`, `td[colspan, rowspan]`, `th[colspan, rowspan]`; `href` only `http:`, `https:` or `mailto:`. The backend list (Task B1) and the frontend list (Task T1, `BASIC_RICH_HTML_ALLOWED_TAGS` / `_ATTR`) must be identical; a test on each side pins the list.
- **The `basic` toolset produces nothing outside that list**: no colours, highlights, checklists, dividers, images, attachments or mentions.
- **No new editor.** The admin editor is a copy of the tenant files from Task T1–T2, unchanged except import paths; each copied file starts with the header `// Copied from Hrms--Web-application---front-end---v1 <path> at <commit>. Change it there first, then copy.`
- **Old data stays readable.** A stored value with no `<` tag is plain text and is shown with line breaks kept (`white-space: pre-wrap`), never parsed as HTML.
- **Length limits count visible text**, not markup: support description 10,000 and comment 10,000 visible characters; raw HTML is capped at 50,000 characters. Legal documents keep their existing limits.
- **Emails never contain user HTML.** Support emails and in-app notification placeholders use the plain-text conversion.
- Announcements, notification templates, configuration JSON and OAuth keys are not touched.
- Zero build warnings; tenant `npm run lint` includes the hard-coded colour check (use theme tokens only).

## Review Focus

- **Pasting from Word or a web page** into the editor (spans with styles, `font`, `div`, images): the saved HTML is reduced to the allowlist and the backend accepts it. Test in T1 (frontend clean-up) and B1 (backend accepts the cleaned output of a fixture paste).
- **A link with `javascript:` or `data:`** typed or pasted: removed on the frontend, rejected with 400 on the backend. Tests in T1 and B1.
- **Exactly 10,000 visible characters wrapped in formatting** (e.g. 10,000 characters in `<p><strong>…</strong></p>`): accepted; 10,001 rejected; a 60,000-character raw body rejected even if its visible text is short. Test in B2.
- **An old plain-text ticket containing `<` in text** (e.g. "error at line 5 < 6"): shown as text, not as broken HTML, because it has no closing tag pattern. The `isRichHtml` rule must require a recognisable allowlisted tag, not just `<`. Tests in B1 and T2.
- **Dark mode in the admin copy:** the editor and view are readable in both themes because the 11 theme variables are defined for light and dark in admin `styles.css`. Test in A1 (variables present in both blocks).

---

## Backend

### Task B1: Shared `BasicRichHtml` helper; legal validator uses it

**Files:**
- Create: `src/ONEVO.Application/Common/Helpers/BasicRichHtml.cs`
- Modify: `src/ONEVO.Application/Features/DevPlatform/Compliance/Helpers/LegalHtmlValidator.cs` (delegate to `BasicRichHtml`, keep its public `Validate` and result type)
- Test: `tests/ONEVO.Tests.Unit/Common/Helpers/BasicRichHtmlTests.cs`; existing legal validator tests stay green

**Interfaces:**
- Produces: `BasicRichHtml.AllowedTags` (`IReadOnlySet<string>`), `BasicRichHtml.AllowedAttributes` (`IReadOnlyDictionary<string, IReadOnlySet<string>>`), `RichHtmlCheck Validate(string? html)` (`record RichHtmlCheck(bool IsValid, string? Error)`), `bool IsRichHtml(string? value)` (true only when it contains an opening or closing allowlisted tag, e.g. `<p>`, `</li>`), `int VisibleLength(string? value)` (plain text: length; rich: length of the text content after decoding entities), `string ToPlainText(string? value)` (block tags become line breaks, list items get "- ", links become `text (href)`, entities decoded, tags removed).

- [ ] **Step 1: Write the failing tests:** the allowlist equals the Global Constraints list exactly; `<p>Hi <strong>there</strong></p>` valid; `<script>`, `<img>`, `<span style>`, `<a href="javascript:x">`, `<a href="data:text/html,x">`, `<p onclick=x>` invalid; `IsRichHtml("line 5 < 6")` false, `IsRichHtml("<p>x</p>")` true; `VisibleLength("<p><strong>abc</strong></p>") == 3`; `ToPlainText("<ul><li>a</li><li>b</li></ul>")` = `"- a\n- b"`; a fixture representing cleaned editor output of a Word paste validates.
- [ ] **Step 2: Run, expect FAIL** — `dotnet test tests/ONEVO.Tests.Unit --filter "BasicRichHtmlTests|LegalHtmlValidator"`.
- [ ] **Step 3: Implement** by moving the tag/attribute scanning from `LegalHtmlValidator` into `BasicRichHtml` and adding `code` and `pre` to the list; `LegalHtmlValidator.Validate` maps `RichHtmlCheck` to its existing result.
- [ ] **Step 4: Run, expect PASS** (same filter).
- [ ] **Step 5: Commit** — `feat(text): shared basic rich HTML allowlist`

### Task B2: Support descriptions and comments accept basic rich text

**Files:**
- Modify: `src/ONEVO.Application/Features/DevPlatform/Support/Helpers/SupportTicketRules.cs` (`ValidateTicket`, `ValidateComment`; add `RawHtmlMaxLength = 50000`)
- Modify: `src/ONEVO.Infrastructure/Services/DevPlatform/Support/SupportNotifier.cs` (email body and tenant notification placeholders use `BasicRichHtml.ToPlainText`)
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Support/SupportRichTextTests.cs`

**Interfaces:**
- Rules: when `IsRichHtml(description)`, validate with `BasicRichHtml.Validate` (400 with its error) and limit by `VisibleLength` (10,000) and raw length (50,000); plain text keeps today's checks. Subject stays plain text. Announcements unchanged.

- [ ] **Step 1: Write the failing tests:** rich description accepted; disallowed tag rejected with the allowlist message; 10,000 visible characters in formatting accepted, 10,001 rejected, 60,000 raw rejected (Review Focus); plain-text ticket unchanged; alert email body contains `- a` for a list and no `<li>`.
- [ ] **Step 2: Run, expect FAIL** — `--filter SupportRichTextTests`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS**, plus `--filter FullyQualifiedName~Support`.
- [ ] **Step 5: Commit** — `feat(support): rich text descriptions and comments`

### Task B3: Backend verification

- [ ] `dotnet build -c Release` (0 warnings), `dotnet test tests/ONEVO.Tests.Unit`, `dotnet test tests/ONEVO.Tests.Architecture` — all pass. Push `feat/rich-text-fields-backend`.

---

## Tenant app

### Task T1: Make the editor self-contained and add the `basic` toolset

**Files:**
- Create: `src/app/shared/ui/rich-text-editor/rich-text-image-uploader.ts` (`RichTextImageUploader` interface + `RICH_TEXT_IMAGE_UPLOADER` `InjectionToken`)
- Create: `src/app/shared/utils/basic-rich-html.util.ts` (`BASIC_RICH_HTML_ALLOWED_TAGS`, `BASIC_RICH_HTML_ALLOWED_ATTR`, `sanitizeBasicRichHtml(html)`, `isRichHtml(value)`)
- Move: `src/app/modules/work/ui/color-swatch-popover/*` → `src/app/shared/ui/color-swatch-popover/*` (update every import)
- Modify: `src/app/shared/ui/rich-text-editor/rich-text-editor.component.ts` (+ spec)
- Modify: `src/app/app.config.ts` — provide `RICH_TEXT_IMAGE_UPLOADER` with a factory over `TaskApiService` (`upload(file, purpose)` → `uploadPendingFile`, `url(fileId)` → `getFileUrl`)

**Interfaces:**
- `RichTextImageUploader { upload(file: File, purpose: string): Observable<{ fileId: string }>; fileUrl(fileId: string): string; }`. The editor injects it with `{ optional: true }`; when absent, images are disabled whatever `allowImages` says.
- New input `toolset = input<'full' | 'basic'>('full')`. `basic` hides colour and highlight, checklist, divider, image, attachment and mention commands (toolbar and slash menu), forces `cssStyles` off, and runs `sanitizeBasicRichHtml` on every `htmlChange` emission and on paste.
- The editor no longer imports anything under `modules/`.

- [ ] **Step 1: Write the failing tests:** `basic` toolbar/slash menu has no colour, checklist, divider, image, attachment or mention entries; pasted Word-style HTML (fixture) is emitted as allowlisted markup only (Review Focus); `javascript:` link removed (Review Focus); `BASIC_RICH_HTML_ALLOWED_TAGS` equals the Global Constraints list; with no uploader provided, the image command is absent; an architecture-style spec reads the component source and asserts it has no `modules/` import.
- [ ] **Step 2: Run, expect FAIL** — `npx ng test --watch=false --include "src/app/shared/ui/rich-text-editor/**/*.spec.ts" --include "src/app/shared/utils/basic-rich-html.util.spec.ts"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS**, then the full `npx ng test --watch=false` to prove the 13 existing editor users are unchanged.
- [ ] **Step 5: Commit** — `refactor(editor): self-contained rich text editor with a basic toolset`

### Task T2: `app-rich-text-view`

**Files:**
- Create: `src/app/shared/ui/rich-text-view/rich-text-view.component.ts` (+ spec)

**Interfaces:** `<app-rich-text-view [value]="text" />`. Rich values (`isRichHtml`) render through `sanitizeBasicRichHtml` into `[innerHTML]` inside a `prose`-styled container using theme tokens; plain values render as text with `white-space: pre-wrap`.

- [ ] **Step 1: Write the failing tests:** rich value renders a list; `"error at line 5 < 6\nnext"` renders as text with the line break (Review Focus); a script in a rich value never reaches the DOM.
- [ ] **Step 2: Run, expect FAIL** — `npx ng test --watch=false --include "src/app/shared/ui/rich-text-view/**/*.spec.ts"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(ui): rich text view for stored rich or plain text`

### Task T3: Support pages use the editor and the view

**Files:**
- Modify: `src/app/modules/settings/feature/support-tickets/support-tickets.component.*` (create form description)
- Modify: `src/app/modules/settings/feature/support-ticket-detail/support-ticket-detail.component.*` (edit description, comment box, display of description and comments)

**Interfaces:** editors use `toolset="basic"`, `allowImages=false`, `allowAttachments=false`, `[maxLength]="10000"` (visible characters, matching the backend); display uses `app-rich-text-view`. Existing ticket attachments stay as they are.

- [ ] **Step 1: Write the failing tests:** create sends the editor HTML as `description`; comment composer sends HTML; old plain-text comments still render with line breaks.
- [ ] **Step 2: Run, expect FAIL** — `npx ng test --watch=false --include "src/app/modules/settings/feature/support-ticket*/**/*.spec.ts"`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(support): rich text in support tickets`

### Task T4: Tenant verification

- [ ] `npx ng test --watch=false`, `npm run lint`, `npm run build` — all clean. Push `feat/rich-text-fields-tenant`.

---

## Admin console

### Task A1: Copy the editor, view and helpers; theme variables; DOMPurify

**Files:**
- Copy from the tenant branch at the Task T3 commit, keeping relative structure under `src/app/shared/`: `ui/rich-text-editor/` (component, uploader token, spec), `ui/rich-text-view/`, `ui/color-swatch-popover/`, `ui/char-counter/`, `directives/stop-click-propagation.directive.ts`, `utils/editable-html.util.ts`, `utils/basic-rich-html.util.ts`, `constants/text-limits.ts` (only `CHAR_COUNTER_THRESHOLD`). Add the copy header to each.
- Modify: `package.json` — add `dompurify` at the tenant's version (`^3.4.17`); `npm install`.
- Modify: `src/styles.css` — define `--color-accent`, `--color-accent-contrast`, `--color-border`, `--color-danger`, `--color-danger-subtle`, `--color-surface`, `--color-surface-secondary`, `--color-text-primary`, `--color-text-secondary`, `--shadow-modal`, `--shadow-popover` in `:root` and in `.dark`, mapped to the admin palette (indigo accent, slate surfaces).
- Convert the copied specs from Vitest to Jest only where APIs differ (`vi.fn` → `jest.fn`); assertions unchanged.
- No image uploader is provided in admin.

- [ ] **Step 1: Write the failing test** `src/app/shared/ui/rich-text-editor/admin-theme-variables.spec.ts`: reads `styles.css` and asserts each of the 11 variables appears in both the `:root` block and the `.dark` block (Review Focus).
- [ ] **Step 2: Run, expect FAIL** — `npx jest src/app/shared`.
- [ ] **Step 3: Copy, add the dependency and variables.**
- [ ] **Step 4: Run, expect PASS** — `npx jest src/app/shared` (copied specs and the new one).
- [ ] **Step 5: Commit** — `feat(ui): reuse the tenant rich text editor in the admin console`

### Task A2: Legal document content uses the editor

**Files:**
- Modify: `src/app/modules/legal-compliance/feature/legal-document-form/legal-document-form.*` (+ spec)

**Interfaces:** `contentHtml` edited with `<app-rich-text-editor toolset="basic" [allowImages]="false" [allowAttachments]="false" [initialHtml]="…" (htmlChange)="…">`; the form control keeps the HTML string; the sandboxed preview from #62 and the backend validator are unchanged.

- [ ] **Step 1: Write the failing tests:** editing updates `contentHtml`; read-only (published) versions show the editor in `readOnly` mode; validation error from the API is shown.
- [ ] **Step 2: Run, expect FAIL** — `npx jest src/app/modules/legal-compliance/feature/legal-document-form`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(legal): edit legal documents with the rich text editor`

### Task A3: Support tickets use the editor and the view

**Files:**
- Modify: `src/app/modules/support/feature/support-ticket-create/*`, `src/app/modules/support/feature/support-ticket-detail/*` (+ specs)

**Interfaces:** same settings as tenant Task T3; announcement body stays a textarea.

- [ ] **Step 1: Write the failing tests:** create, edit and comment send HTML; descriptions and comments render through `app-rich-text-view`; old plain-text comments keep line breaks; the announcement form still uses a textarea.
- [ ] **Step 2: Run, expect FAIL** — `npx jest src/app/modules/support`.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(support): rich text in admin support tickets`

### Task A4: Admin verification and incident plan note

- [ ] `npx jest`, `npm run lint`, `npm run build` — all clean.
- [ ] In `HRMS-Backend-v1` `docs/superpowers/plans/next/2026-11-16-incident-management-*.md` (branch `spec/incident-management`), change the postmortem steps to use `app-rich-text-editor toolset="basic"` and `app-rich-text-view` in the admin plan, and `BasicRichHtml.Validate` / `VisibleLength` for postmortem fields in the backend plan. Commit there.
- [ ] Push `feat/rich-text-fields-admin`. No PRs unless Dabi asks.
