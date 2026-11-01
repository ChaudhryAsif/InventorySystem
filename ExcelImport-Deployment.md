# Excel cost-sheet import

The Create/Edit Cost Sheet screen now offers Excel upload alongside manual entry.

1. Choose a .xlsx file (maximum 5 MB) in the supplied Costing M. Ctn 1 sample layout.
2. Select Fill from Excel. The existing form is filled directly, with no separate preview popup. This replaces unsaved form values and sets Draft; it does not save a record.
3. Review/edit the filled fields and select any missing finished item, paper and adhesive inventory links. Import warnings appear inline above the form.
4. Save as Draft or Final normally.

The sample layout supports one to five layers at rows 20, 22, 24, 26 and 28. Its R18 ply count must include every populated layer. A workbook must contain exactly one matching cost sheet; unrelated tabs are ignored. Different layouts, unsupported formula patterns, invalid inputs, .xls/.xlsm files and encrypted/corrupt workbooks are rejected with a readable message.

Excel item/customer numbers are not database IDs. Only unique, exact, active item-name matches with the required stock type/unit are linked automatically. Unmatched or ambiguous names remain unselected. Mapping a paper stock item keeps the imported quotation rate.

The system recalculates costs from imported inputs using the agreed costing rules, including GST on cost plus profit and zero sheet/glue-lamination/chemical cost when Lamination is No. Cached Excel totals are not treated as authoritative. The sample's customer name/reference are kept in remarks; cost sheets remain reusable recipes.

No schema migration or new package dependency is required. Publish the updated project to webpos first. The original file arriving from the client must still be checked against this sample layout before confirming its compatibility.

Validation: actual sample import; No=99.77, Yes=110.38; invalid/oversize/incorrect-layout inputs; Create/Edit permission checks; no mutation during preview; exact/duplicate item-name matching; imported Draft save/reopen/edit/Final; 253 focused checks passed. Local Admin browser verified upload/preview/apply, inventory mapping without rate replacement, Draft save/reopen and edited Final save/reopen. No client/live data was modified for this feature.

Direct-form follow-up: local Admin browser verified direct fill of all five layers, dimensions, rates, GST and MOQ without an import dialog. Manual edits remain possible. An invalid file leaves existing form values intact.

Upload wording shortened: only file type/size and a short success message are shown. Missing inventory links are listed only while needed; the guidance updates after paper and stock selections. Long formula explanations were removed from the upload UI.
