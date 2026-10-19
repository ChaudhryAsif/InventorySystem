# First-stage manufacturing flow

The operating sequence is Purchase → Cost Sheet → Production Order → Record Boxes Produced → Sale → Payment. Branch 1 is used automatically.

## Setup

1. Register paper, glue and silicate as Raw Material, stock unit kg. Register each saleable box specification as Finished Boxes, stock unit pcs.
1a. There are two item types: Raw Material (anything bought, in kg or pcs) and Finished Goods (made, pcs). The old General type was converted to Raw Material by migration ManufacturingOtherMaterials; units and stock were not changed. Cost sheets have an Other Materials section for any raw material (kg or pcs, such as packing tape): cost is added to material cost, box weight is not, and production deducts them in each item's own unit (pcs are rounded up to whole pieces).
2. Existing items were previously General/pcs; review them. Do not assume an old reel count is kilograms. Unit changes are blocked while stock exists: use documented stock adjustments to reconcile old quantities, reclassify the item at zero stock, then establish its verified kg balance. Keep that adjustment history.
3. Purchases use total net kilograms and price per kg for materials. Optional reel/drum counts describe packaging only; the system never guesses a weight from the number of reels or drums. Finished goods use whole pieces.
4. Set customer/supplier opening balances once. A positive customer balance is receivable; a positive supplier balance is payable. For a party marked both, positive opening is receivable and negative opening is payable. Do not also enter the same opening as an accounting voucher.

## Cost sheets and orders

- All material estimates are kg per ONE box. Paper layers may repeat the same paper item; production aggregates them. Link separate glue/silicate items and kg-per-box estimates when used. Estimates allow six decimal places; stock postings round up to the nearest gram.
- Link a finished-box item and an active customer before finalizing. Server calculations replace submitted line costs and totals. Saved settings must load; browser defaults are no longer silently substituted.
- Existing sheets with only a combined adhesive weight must be reviewed and split into glue/silicate estimates before reuse. Historical quantities are not silently converted.
- Creating or explicitly saving an unstarted order copies its material quantities, rates, output item, branch and selling rate. Editing the source cost sheet later does not change that saved plan.

## Record production

- Open Record Production from the order and enter boxes actually produced. Staff do not enter measured material usage.
- The server computes estimated paper/glue/silicate consumption from the saved plan, checks stock, deducts all materials and adds finished boxes in one transaction.
- One posting closes the order. Output must be a positive whole number up to the ordered quantity. A smaller output closes it at that quantity; create a new order for the remaining requirement.
- Duplicate posting is rejected. Completed orders cannot be edited or deleted. Reverse Production requires a reason and enough finished stock to remove the full output; it restores estimated materials and retains the posting history. A reversed order stays closed; use a new order for the correction.
- Legacy orders with actual consumption are not automatically converted or posted again. Reconcile them separately. Pending legacy orders with no consumption can be edited and saved after their cost sheet/item setup is corrected.
- Material stock remains an estimate. Compare it with physical stock periodically and post documented adjustments.

## Sales, dues and payments

- Sales deduct available finished stock. Cash sales are fully settled. Credit sales remain outstanding; use Receive Money for later collections, including partial payments. Pay Money works the same way for credit purchases.
- Receive Money accepts customer collections and supplier refunds. Pay Money accepts supplier payments and customer refunds. Both screens show the type of balance and reject the wrong direction, excess amounts and fractions smaller than one paisa. Advances remain an administrator voucher operation.
- Cancelling a paid credit invoice keeps the separate payment history and exposes the resulting refund due. Outstanding and aging reports include that refund in the correct Receivable/Payable direction. Partial refunds reduce it; voiding the refund restores it. Settlements remain against the party balance, not allocated to individual invoices.
- Party statements, outstanding reports and aging use the same party movements and opening balance. Invoice GL entries are not counted a second time. Only receivable/payable entries from manual vouchers affect party dues; cash/bank tags do not create artificial receivables.
- New invoices post to the general ledger by default. Existing invoices created with GL posting disabled are not silently backfilled; reconcile those historical periods before relying on financial statements. Manufacturing inventory valuation, WIP and COGS accounting are outside this first-stage quantity workflow; a quotation estimate is not a verified financial valuation.

## Database rollout

Back up and verify the target database and migration history. The accompanying ManufacturingFlow.sql is an idempotent upgrade from 20261001072449_CancellationReasons through EstimatedProductionFlow and ManufacturingWeightPrecision. It adds the new fields and increases weight precision. It does not merge old stock or infer old item units. Inspect very large existing decimal values before precision changes, because the number of integer digits is reduced.

Apply the reviewed migration before starting the updated application. No production database migration or deployment was performed during implementation. Use a restored test database for the first rehearsal and verify opening stock, customer/supplier dues, production, sale, returns, payment and reversal before rollout.

## Verification

- Solution builds successfully.
- 98 non-SQL tests pass, including estimated production, duplicate/shortage/reversal checks, snapshot stability, cash/credit sales, opening balances and partial settlements.
- An isolated in-memory HTTP host rendered 16 distinct pages (including populated production lists and purchase printing) and exercised authenticated/CSRF-protected Purchase → Cost Sheet → Order → Record Production → Credit Sale → Collection.
- The example purchased 1,000 kg paper and 100 kg glue, produced 1,000 boxes using 500 kg paper and 20 kg glue, sold 100 boxes for Rs. 15,000, and collected Rs. 5,000. Ending balances: paper 500 kg, glue 80 kg, boxes 900, customer due Rs. 10,000.
- Changed external scripts and rendered inline scripts pass JavaScript syntax checks.
- SQL-backed integration/migration execution remains unverified because LocalDB registry access is unavailable. The SQL regression case was updated for the new one-posting production flow.

## Credit/refund follow-up

The isolated HTTP check also cancelled a part-paid credit sale, refunded Rs. 5,000 to its customer, cancelled a part-paid credit purchase, and received its Rs. 3,000 supplier refund in two parts. Replaying the final refund request returned the stored result without creating another payment. No additional database migration is required for these refund changes; the manufacturing migration above is still required if it has not been applied.
