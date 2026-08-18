# Ownership audit reference (09.07.2026, as of Phase 6 Step 1)
n*(Kept with the ADR log; lived between ADR-017 and ADR-018 in the single-file `DECISIONS.md` until the 18.08.2026 split.)*

Snapshot of which endpoints enforce owner-scoping, kept here because it was a deliberate, documented audit pass rather than an incidental fact:

| Endpoint | Owner filter |
|---|---|
| `GET/POST/PUT/DELETE /api/drafts...` | ✅ `OwnerId == uid` everywhere |
| `GET/POST /api/drafts/{id}/cedar` (export/import) | ✅ export filtered; import stamps current `uid` |
| `GET/POST/DELETE /api/channels...` | ✅, + creation quota |
| `GET /api/channels/{id}/stats` | ✅ ownership checked before returning snapshots |
| `POST /api/posts/export` | ✅ via `PublishAsync(ownerId)` |
| `GET/POST/DELETE /api/posts/scheduled...` | ✅ everywhere |
| `POST /api/assets` | ✅ `OwnerId` stamped, + storage quota |
| `POST/DELETE /api/drafts/{id}/publish-blog, comments` | ✅ ownership checked |
| `DELETE /api/comments/{id}` | ✅ via `Comment.DraftId → Draft.OwnerId` |
| Public `blog.mooexe.dev` (annotations/react/comments) | No ownership by design — filtered by `IsBlogPublished` instead |
| `/media/*` | Public, no auth by design (Telegram must be able to download) — GUID filenames, not enumerable |

---

