# Single-branch operation

The application now defaults to one operating location (branch ID 1). No database migration is required.

- Purchases, sales, returns, cost sheets, vouchers, and stock adjustments receive branch 1 automatically when it is omitted.
- All active users use this location while their existing role permissions still apply. Existing branch assignments are preserved when editing a user; new users default to branch 1.
- Branch selectors and report columns are hidden. Reports, history, production, inventory, and accounts are scoped to branch 1, including for SuperAdmin.
- Requests explicitly targeting another branch are rejected. Existing records in other branches are retained and are not merged, reassigned, or deleted.
- Category, item, and party records remain shared master data.

To restore the existing multi-branch workflow later, configure and restart the application:

```json
"Branches": {
  "MultiBranchEnabled": true
}
```

The equivalent environment variable is `Branches__MultiBranchEnabled=true`. Review user branch assignments before enabling it. Keeping the setting absent or false uses single-branch mode.

This change does not add finished-goods production receipts or change costing calculations. Those remain separate work before a manufacturing go-live.
