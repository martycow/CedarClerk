# UI changes — the inventory is read first, not written last

`docs/design/UI-INVENTORY.md` lists every front-end element: where it lives, what it is, what it is for, whether it has a loading state, and what it replaced. It is 400+ rows and it is current.

**Before adding any UI element — a button, a field, a panel, an indicator, a settings block — search that file for the feature it belongs to.** Most new controls already have a home: a settings block, a step of the Export modal, a tab of the Publishing Manager. Adding a second place for the same kind of thing is how the UI comes apart.

This has been raised before, and it has a concrete history: **X and Bluesky connection controls were first built inside the Export modal, then moved to Settings → Integrations** in ADR-095 (07.08.2026) — a rebuild that existed only because nobody looked at where the Telegram connection already lived. The inventory records both the move and the reason, on the `sec-integrations` rows.

Rules:

1. **Read before you add.** Grep `docs/design/UI-INVENTORY.md` for the area (`sec-integrations`, `Export modal`, `Publishing Manager`, …). If a panel for that concern exists, the new control goes there, or you say out loud why it cannot.
2. **Update the inventory in the same commit as the code.** A row that appears a commit later is a row nobody trusts. Keep the column shape: Element | Location | Type | Purpose | Loading state | Notes.
3. **`UiInventoryDriftTests` enforces rule 2** for the coarse case: it fails `dotnet test` when a page component or a `sec-*` settings section exists in the front end with no mention in the inventory. It cannot check that a *row* is accurate — only that the element is not missing entirely.
4. **Deleting a UI element deletes its row**, and the note about what replaced it goes on the row that survives.

The same reflex applies to the design tokens: colours, spacing and component patterns live in `docs/design/DESIGN.md`, and a one-off value in a component is the same mistake at a smaller scale.
