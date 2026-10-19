# Group contact details and delete access

Apply GroupContactDetails.sql to the test database before publishing the latest project to webpos.runasp.net. Migration: 20261019113219_GroupContactDetails. It adds nullable Address (500 characters) and ContactNumber (32 characters) columns to ItemCategory. Existing descriptions are preserved. If the earlier ClientCartonCosting migration has not been applied, run ClientCartonCosting.sql first; both migrations are included in the project.

Address / Location and Contact Number appear in Add/Edit Group, the list and search. Both fields can be left blank for existing groups. Save trims surrounding whitespace; overlength values are rejected. Group-status confirmation includes these fields to prevent stale reviews.

Delete is visible only to SuperAdmin in initial and refreshed lists. The endpoint is also restricted to SuperAdmin; Admin direct calls are denied. Groups containing linked items remain protected from deletion. Edit remains visible; locking editing after setup is deferred as requested.

Validation: 222 focused checks passed through the direct test runner. Local browser checks verified Admin create, update, reload and no Delete button; SuperAdmin Delete visibility both on initial page and after list refresh. The hosted migration has not been executed, and live verification awaits deployment.
