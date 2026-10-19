# POS update and rollout

Offline storage and synchronization are deferred. No production database or website was changed during implementation.

## Before deployment

1. Back up the production database and deployed application, and test restoring those backups.
2. Compare the deployed source and schema with this repository. The hosted website had features and branding that differed from the local source; preserve those differences before publishing.
3. Confirm the database migration history ends at `20260826164015_RemoveConsumeModule`. Review `PosIntegrity.sql` against the actual schema; an idempotent migration script does not reconcile manually added columns or schema drift.
4. Check for duplicate nonempty item barcodes before creating the unique index. Resolve duplicates deliberately.
5. Review user branch assignments. The new BranchIds column defaults existing users to branch 1; assign other authorized branches explicitly. SuperAdmin can access all branches.
6. Review the new permissions. The migration grants them to the existing Admin role with ID 1. Confirm that ID identifies Admin in the target database and assign appropriate permissions to custom roles.
7. Apply the reviewed migration script during a maintenance window, then publish the matching application. Database migration and production deployment require the owner's approval.
8. Smoke-test login, role/branch access, sale, purchase, linked return, cancellation, stock adjustment, production consumption, and accounting reports using controlled records.

## Behavior changes

Invoice totals are calculated on the server. Stock cannot become negative. Linked returns cannot exceed the original quantities or remaining original net amount; header charges and discounts are allocated proportionally. Confirm this allocation matches the business refund policy.

Cancellation requires a reason, preserves invoice lines, and voids associated accounting entries while reversing stock. Posted production consumption is replaced atomically on resubmission. Closed production orders cannot consume more stock.

Write endpoints enforce CSRF protection and database permissions. Invoice requests include identifiers to prevent duplicate saves on retry. Audit records store response metadata rather than raw submitted payloads. WhatsApp management requires authentication and inbound POST webhooks require a valid Meta app-secret signature.

An optional new SuperAdmin bootstrap account requires `Bootstrap:SuperAdminPassword` of at least 12 characters; existing accounts are preserved. Store passwords and service credentials outside source control.

## Validation

Build: `dotnet build InventorySystem.sln`

Full regression suite with isolated disposable LocalDB databases (PowerShell):

```powershell
$env:POS_TEST_SQL_SERVER = 'localdb'
dotnet test InventorySystem.Tests/InventorySystem.Tests.csproj
```

Without that environment variable, SQL/HTTP integration tests are skipped. Integration tests use uniquely named `InventorySystem_Test_` databases and do not use the production connection string.
