# Confirmed client carton costing

Publish the updated project to webpos.runasp.net for live verification. This follow-up requires no new database migration. If ClientCartonCosting.sql has not yet been run, apply that earlier script first.

Rules:
- Lamination No: sheet charge, glue-lamination and lamination-chemical costs and stock estimates are zero. General glue and silicate remain in use.
- Weight-based charges: paper KG x entered rate per KG. 0.5 KG x 10 = Rs. 5.00.
- Commission A and B: each is base cost x entered percentage / 100.
- Profit and additional tax: each is base cost x entered percentage / 100.
- GST: (base cost + profit) x GST percentage / 100. Commission is added separately and does not compound into profit or GST.
- Price: (base cost + commissions + profit) / MOQ; add GST, additional tax and withholding afterward, retaining Excel's MOQ placement. Withholding retains Excel's operating-cost basis.
- Existing manual recipes retain their manual method. Open and save existing client-method recipes to recalculate them with the confirmed rules; already saved totals and production-order snapshots are not silently rewritten.

Reference sample (original dimensions and rates, commission/additional tax zero):
- Lamination No: base 80.53, profit 4.03, pre-GST 84.55, GST 15.22, final Rs. 99.77.
- Lamination Yes: base 89.08, profit 4.45, pre-GST 93.54, GST 16.84, final Rs. 110.38.

Validation: 214 focused checks passed using the direct test runner, including controller save/reload/update, percentage validation, both production stock paths, exact base-cost commission/tax calculations, and 0.5 KG labour example. JavaScript fixtures passed with matching results. Local browser save/reopen verified commission A 5%, B 2%, additional tax 10% with lamination Yes (Rs. 125.52), then No without lamination stock links (Rs. 99.77 with commissions/additional tax zero). Printed results verified. Live verification awaits deployment.
