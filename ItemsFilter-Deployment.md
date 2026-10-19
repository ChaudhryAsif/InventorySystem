# Items page client-request checklist — 19 October 2026

- [x] List filter offers Raw Material and Finished Goods, plus All Item Types to clear the filter.
- [x] Individual group names are not options in that list filter.
- [x] Filtering applies before paging; counts match the selected type, search and status.
- [x] Search remains case-insensitive and combines with type/status.
- [x] No-results and Clear filters restore the list correctly.
- [x] Admin cannot see Delete; direct Delete requests are denied.
- [x] SuperAdmin can see Delete; existing deactivation/history preservation remains.
- [x] Edit remains available and its Group Name selection still loads correctly.

231 focused checks passed through the direct runner. Local Admin/SuperAdmin browser verification passed. Updated source files copied and hash-verified in the original project. Publish the latest project to webpos.runasp.net for live verification. No schema change or migration is required for this update.
