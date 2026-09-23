```
DB: OnevoDb_calsmoke
Last migration: 20260906134431_AddHolidayCalendarSettings
Snapshot generated: 2026-09-09T11:32:50Z
```

# Domain: Notifications & Outbox

### ER Diagram (same-domain relationships only)

```mermaid
erDiagram
  notification_templates {}
  notifications {}
  outbox_messages {}
```

### notification_templates

| Column | Type | Key | Nullable | Default |
|---|---|---|---|---|
| id | uuid | PK | NOT NULL | – |
| code | varchar100 |  | NOT NULL | – |
| in_app_title_template | varchar255 |  | NOT NULL | – |
| in_app_body_template | text |  | NOT NULL | – |
| mail_subject_template | text |  | NULL | – |
| mail_body_template | text |  | NULL | – |
| in_app_enabled | boolean |  | NOT NULL | – |
| mail_enabled | boolean |  | NOT NULL | – |
| created_at | timestamptz |  | NOT NULL | – |
| updated_at | timestamptz |  | NULL | – |

#### References into other domains

_(none)_

#### Referenced by other domains

_(none)_

### notifications

| Column | Type | Key | Nullable | Default |
|---|---|---|---|---|
| id | uuid | PK | NOT NULL | – |
| tenant_id | uuid |  | NOT NULL | – |
| recipient_user_id | uuid |  | NOT NULL | – |
| template_code | varchar100 |  | NOT NULL | – |
| title | varchar255 |  | NOT NULL | – |
| body | text |  | NOT NULL | – |
| related_entity_type | varchar40 |  | NULL | – |
| related_entity_id | uuid |  | NULL | – |
| is_read | boolean |  | NOT NULL | – |
| read_at | timestamptz |  | NULL | – |
| created_at | timestamptz |  | NOT NULL | – |

#### References into other domains

_(none)_

#### Referenced by other domains

_(none)_

### outbox_messages

| Column | Type | Key | Nullable | Default |
|---|---|---|---|---|
| id | uuid | PK | NOT NULL | – |
| type | varchar150 |  | NOT NULL | – |
| encrypted_payload | text |  | NOT NULL | – |
| tenant_id | uuid |  | NULL | – |
| status | varchar20 |  | NOT NULL | – |
| attempt_count | integer |  | NOT NULL | – |
| created_at | timestamptz |  | NOT NULL | – |
| next_attempt_at | timestamptz |  | NOT NULL | – |
| processed_at | timestamptz |  | NULL | – |
| last_error | varchar2000 |  | NULL | – |

#### References into other domains

_(none)_

#### Referenced by other domains

_(none)_
