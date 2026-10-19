# Groups update - deployment notes

Implemented in D:\InventorySystem\InventorySystem.

- Groups, Add Group, Edit Group, Group Name and Search groups replace the old group wording.
- Groups have a Raw Material / Finished Goods type, shown in the list and saved on add/edit.
- Items use Group Name / Select Group. Choosing a classified group sets and locks its item type; finished goods use pieces.
- The server rejects mismatched group/item types and prevents changing a group to a type that conflicts with linked items, including inactive items.
- Existing status confirmation, linked-item deletion protection, quantities and invoice history are preserved.

Database deployment

Apply the included GroupClassification.sql to the TEST website database before publishing the updated application. It contains only migration 20261019090245_GroupClassification (after ManufacturingOtherMaterials). It expects the existing EF migration-history table. Alternatively apply that EF migration with your usual database-update workflow; use one method.

The migration adds nullable ItemCategory.GroupType, classifies only existing groups whose linked items all have the same supported type, and renames the standard Category/Index menu to Groups. Mixed and empty groups remain Not set; choose their type manually. For mixed groups, first move conflicting items to a matching group. No existing item types or stock quantities are rewritten.

After deployment: sign in, refresh, create one Raw Material and one Finished Goods group, add/edit items under each, confirm Group Name filtering, and verify existing group status confirmation and deletion protection.

Verification

- Original application build succeeds (existing warnings remain).
- 164 focused checks pass using the direct regression runner, including 16 group-classification cases and the existing group/status/menu/item/report/account checks.
- JavaScript syntax checks and group-selection behavior checks pass.
- SQL script generated from the EF migration; not executed against the hosted databases. LocalDB is unavailable in this environment.
- Local browser preview was blocked by browser URL policy; no live verification of this new release has been claimed.

The standard dotnet test invocation did not report results after discovery and was stopped; the 164-check result comes from direct execution of the xUnit test methods and assertions.
