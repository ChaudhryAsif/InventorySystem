# InventorySystem — Roadmap & Checklist

A living checklist of what's built, what's in progress, and what's still missing.
Generated 2026-06-08 after a full-project review (inventory, accounting, WhatsApp/AI, admin/infra).

Legend: `[x]` done · `[~]` partial · `[ ]` not started

---

## ✅ Recently completed (this iteration)

- [x] **Bug: Stock double-count on adjustments** — stock mutation centralized into one
  guarded helper (`ApplyDetailToStockAsync`) used by both `Save` and `Post`. An adjustment
  can now only ever affect stock once. *(StockAdjustmentController.cs)*
- [x] **Bug: Item "purchase price" = sale price** — `Item/GetItems` now returns the actual
  last-purchase cost from `PurchaseInvoiceBody.PurPrice` (falls back to 0 when never
  purchased). *(ItemController.cs)*
- [x] **Bug: Hard-coded WhatsApp webhook verify token** — `MetaWebhookVerify` now reads the
  token from the active `WhatsAppChannel.WebhookVerifyToken` instead of the literal
  `"AsifPOS@2026"`. *(WhatsAppController.cs)*
- [x] **Sale: full List / View / Edit / Delete / Print**
  - `Sale/List` page with search + date-range filter
  - `Sale/GetSales`, `Sale/GetSaleById` JSON endpoints
  - `Sale/Update` — reverses old stock + ledger, re-validates, re-applies (single transaction)
  - `Sale/Delete` — restores stock and reverses ledger/receipt entries
  - `Sale/Print` — printable invoice view
  - Edit mode wired into the existing create form via `?id=` *(saleinvoice.js)*

---

## 🔴 Critical — make transactions fully manageable

The same create-only limitation that Sale had still applies to every other transaction module.
Replicate the Sale pattern (List + View + Edit + Delete + Print, with stock & ledger reversal):

- [ ] **Purchase** — List / View / Edit / Delete / Print (reverse stock-in + ledger + payment voucher)
- [ ] **Purchase Return** — List / View / Edit / Delete / Print
- [ ] **Sale Return** — List / View / Edit / Delete / Print
- [ ] **Consume** — List / View / Edit / Delete / Print
- [ ] **Production** — Edit existing order; Print order + material slip
- [ ] **Stock Adjustment** — Edit (currently create + post + delete only); Print
- [ ] **Cost Sheet** — Print / Export; validate that plies reference existing items

### Remaining known bugs / correctness
- [ ] **Stock validation on Purchase** — Purchase accepts any quantity with no checks; add a
  centralized `IStockService` so all modules validate/mutate stock the same way.
- [ ] **Adjustment direction ignored** — every adjustment *adds* to stock regardless of
  `AdjustmentType`; "Decrease"/"Correction" should subtract.
- [ ] **Item edit form lacks a Purchase Price field** — add `PurchasePrice` to the `Items`
  table + form so cost isn't only inferred from purchase history.
- [ ] **Ledger reversal is a hard delete** — on Sale edit/delete we remove auto ledger rows;
  consider soft-void (`IsVoid`) once reports honor the flag (see Accounting below).

---

## 🟠 Production-readiness / cross-cutting

- [ ] **Move secrets out of `appsettings.json`** — DB connection string + Groq/Speech/Meta keys
  to user-secrets / environment variables.
- [ ] **Global error-handling middleware** + structured logging (`ILogger`); remove empty
  `catch {}` blocks and `Console.WriteLine` debugging in services.
- [ ] **Audit log** — who created/edited/deleted records, logins, permission changes
  (add `ModifiedBy/ModifiedDate`, an `AuditTrail` table).
- [ ] **Pagination** on all list endpoints (currently every `GetList` returns the full table).
- [ ] **Excel/CSV export** across reports and transaction lists (only client-side PDF today).
- [ ] **Server-side validation** (FluentValidation) beyond `ModelState`.
- [ ] **Automated tests** — none exist; start with the Sale stock/ledger reversal logic.

### Security
- [ ] Password reset / forgot-password flow (needs SMTP/email service)
- [ ] Self-service password change
- [ ] Login rate-limiting + account lockout on repeated failures
- [ ] Cookie policy: enforce HTTPS (`SecurePolicy.Always`) in production
- [ ] Optional 2FA/MFA

---

## 🟡 Accounting module

- [ ] **Period locking** — prevent posting to closed/locked months
- [ ] **Approval workflow** — Draft → Approved → Posted states (currently auto-posts)
- [ ] **True voucher edit** UI (currently void + re-post)
- [ ] **Report drill-down** — Trial Balance / P&L / Balance Sheet rows link to source vouchers
- [ ] **Report export** (PDF/Excel) and batch printing
- [ ] **Soft-void everywhere** — ensure all balance queries filter `IsVoid` so ledger rows are
  never hard-deleted
- [ ] Recurring journal entries (accruals, depreciation, standing orders)
- [ ] Cost centers / job costing; multi-currency

---

## 🟡 WhatsApp / AI chatbot

- [ ] **Image / document inbound** support (currently audio + text only)
- [ ] **Message status callbacks** — handle Meta delivery/read updates; populate
  `AgentMessage.Status` beyond sent/failed
- [ ] **Duplicate-message dedup** on webhook retries (index + check `WhatsAppMessageId`)
- [ ] **Rate limiting / circuit breaker** on AI, TTS, and outbound sends
- [ ] **Bound conversation history** sent to the LLM (token-overrun protection)
- [ ] Order-to-fulfilment flow: payment collection, delivery tracking, follow-up reminders

---

## 🟢 Scale / nice-to-have

- [ ] Multi-company / multi-warehouse (models currently assume a single entity/branch)
- [ ] Global settings / configuration page
- [ ] Role-specific & customizable dashboards; KPI threshold alerts
- [ ] Caching for dashboard/report queries (`IMemoryCache`)
- [ ] Database backup/restore tooling
- [ ] Swagger/OpenAPI docs for the JSON endpoints

---

## Suggested order of work

1. **Purchase** List/View/Edit/Delete/Print (highest value after Sale; mirrors Sale exactly).
2. Remaining transaction modules (Sale Return, Purchase Return, Consume).
3. Centralized `IStockService` + fix adjustment direction + Purchase stock validation.
4. Secrets out of config + global error handling/logging.
5. Accounting period-locking + soft-void.
6. WhatsApp media + dedup + status callbacks.
