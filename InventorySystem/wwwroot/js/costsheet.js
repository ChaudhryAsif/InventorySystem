// ═══════════════════════════════════════════════════════════════════════════
//  costsheet.js  —  Cost Sheet full calculation engine
//
//  Column map for ply table rows:
//   cells[0] #
//   cells[1] Item ID  (readonly, filled by 🔍 lookup)
//   cells[2] Item Name  (readonly, filled by 🔍 lookup)
//   cells[3] Specification  (editable text)
//   cells[4] GSM  (editable number)
//   cells[5] Rate/KG  (editable — auto-filled from last purchase price)
//   cells[6] KGs Used  (editable)
//   cells[7] Cost  (readonly — Rate × KGs)
//   cells[8] Remove button
// ═══════════════════════════════════════════════════════════════════════════

let plyCounter = 0;
let selectedCustomer = null;
let allCustomers = [];
let settings = {};

// ════════════════════════════════════════════════════════════════════════════
// INIT
// ════════════════════════════════════════════════════════════════════════════
window.addEventListener('DOMContentLoaded', async function () {
    await loadSettings();

    const editId = parseInt(document.getElementById('editCostSheetId').value) || 0;

    if (editId > 0) {
        await loadExistingSheet(editId);
    } else {
        loadNextSheetNumber();
        applySettingsToForm();
        // Start with 3 blank rows — user uses 🔍 to pick items from purchase
        addPlyRow();
        addPlyRow();
        addPlyRow();
    }

    document.getElementById('addPlyBtn').addEventListener('click', () => addPlyRow());
});

// ════════════════════════════════════════════════════════════════════════════
// SETTINGS
// ════════════════════════════════════════════════════════════════════════════
async function loadSettings() {
    try {
        const res = await fetch('/CostSheet/GetSettings');
        settings = await res.json();
    } catch {
        settings = {
            labourRate: 5, energyRate: 5.08,
            wastePercentage: 6, adminExpPercentage: 1.52,
            sellingDistPercentage: 1, repairMaintenancePercentage: 1.5,
            storeSparesPercentage: 1.5, manufacturingCostPercentage: 1.72,
            defaultFreightRate: 0.5, defaultProfitPct: 11
        };
    }
}

function applySettingsToForm() {
    setVal('labourRate', settings.labourRate);
    setVal('energyRate', settings.energyRate);
    setVal('wastePct', settings.wastePercentage);
    setVal('adminExpPct', settings.adminExpPercentage);
    setVal('sellingDistPct', settings.sellingDistPercentage);
    setVal('repairMaintPct', settings.repairMaintenancePercentage);
    setVal('storeSparesPct', settings.storeSparesPercentage);
    setVal('mfgCostPct', settings.manufacturingCostPercentage);
    setVal('freightRate', settings.defaultFreightRate);
    setVal('profitPct', settings.defaultProfitPct);
    recalcAll();
}

// ════════════════════════════════════════════════════════════════════════════
// SHEET NUMBER
// ════════════════════════════════════════════════════════════════════════════
function loadNextSheetNumber() {
    fetch('/CostSheet/GetNextSheetNumber')
        .then(r => r.json())
        .then(d => setVal('sheetNo', d.sheetNo))
        .catch(() => setVal('sheetNo', '–'));
}

// ════════════════════════════════════════════════════════════════════════════
// PLY TABLE
// ════════════════════════════════════════════════════════════════════════════
function addPlyRow(plyName = '', spec = '', gsm = '', rate = '', kgs = '', itemId = '') {
    plyCounter++;
    const tbody = document.getElementById('plyBody');
    const tr = document.createElement('tr');
    tr.dataset.plyId = plyCounter;

    tr.innerHTML = `
        <td style="text-align:center;color:#6b7280;font-size:.8rem;">${plyCounter}</td>
        <td>
            <div style="display:flex;align-items:center;gap:.2rem;">
                <input type="text" class="ply-input" placeholder="ID"
                       value="${escAttr(itemId)}" readonly
                       style="width:45px;text-align:center;font-size:.75rem;">
                <button type="button" class="item-lookup-icon"
                        onclick="openPlyItemLookup(this)"
                        title="Search raw material item">🔍</button>
            </div>
        </td>
        <td><input type="text" class="ply-input" placeholder="Click 🔍 to select item"
                   value="${escAttr(plyName)}" readonly></td>
        <td><input type="text" class="ply-input" placeholder="e.g. CMP, Coated"
                   value="${escAttr(spec)}"></td>
        <td><input type="number" class="ply-input" placeholder="e.g. 250"
                   value="${gsm}" step="0.01" min="0"
                   title="GSM = paper quality (grams/m²) — informational only"></td>
        <td><input type="number" class="ply-input" placeholder="0.00"
                   value="${rate}" step="0.01" min="0"
                   oninput="calcPlyRow(this)"
                   title="Rate per KG — auto-filled from last purchase price"></td>
        <td><input type="number" class="ply-input" placeholder="0.000"
                   value="${kgs}" step="0.001" min="0"
                   oninput="calcPlyRow(this)"></td>
        <td><input type="number" class="ply-input calc" placeholder="0.00"
                   value="0" step="0.01" readonly></td>
        <td style="text-align:center;">
            <button type="button" class="btn-remove-ply"
                    onclick="removePlyRow(this)">×</button>
        </td>
    `;

    tbody.appendChild(tr);

    if (rate !== '' || kgs !== '') {
        calcPlyRow(tr.cells[5].querySelector('input'));
    }
}

function calcPlyRow(input) {
    const row = input.closest('tr');
    const rate = parseFloat(row.cells[5].querySelector('input').value) || 0;
    const kgs = parseFloat(row.cells[6].querySelector('input').value) || 0;
    row.cells[7].querySelector('input').value = (rate * kgs).toFixed(2);
    recalcAll();
}

async function removePlyRow(btn) {
    if (document.getElementById('plyBody').rows.length <= 1) {
        await alertDialog('At least one ply layer is required!', { type: 'warning' });
        return;
    }
    if (await confirmDialog('Remove this ply layer?', { danger: true })) {
        btn.closest('tr').remove();
        rebuildPlyNumbers();
        recalcAll();
    }
}

function rebuildPlyNumbers() {
    document.querySelectorAll('#plyBody tr').forEach((row, i) => {
        row.cells[0].textContent = i + 1;
    });
    plyCounter = document.getElementById('plyBody').rows.length;
}

// ════════════════════════════════════════════════════════════════════════════
// DIMENSIONS
// ════════════════════════════════════════════════════════════════════════════
function recalcDimensions() {
    const unit = getVal('sizeUnit');
    const L = parseFloat(getVal('dimL')) || 0;
    const W = parseFloat(getVal('dimW')) || 0;
    const H = parseFloat(getVal('dimH')) || 0;
    const flap = parseFloat(getVal('dimFlap')) || 0;
    const gap = parseFloat(getVal('autoFlapGap')) || 0;

    if (L === 0 && W === 0 && H === 0) return;

    const k = unit === 'in' ? 25.4 : 1;
    const Lmm = L * k, Wmm = W * k, Hmm = H * k;
    const flapActMm = flap > 0 ? flap * k : Wmm / 2;

    const swMm = Hmm + Wmm + gap;
    const slMm = 2 * (Lmm + Wmm) + (flapActMm * 2);
    const swIn = swMm / 25.4;
    const slIn = slMm / 25.4;
    const wfIn = flapActMm / 25.4;

    const adjWMm = swMm + 2;
    const adjLMm = slMm + 3;
    const adjWIn = adjWMm / 25.4;
    const adjL1In = adjLMm / 25.4;
    const adjL2In = adjL1In / 2;
    const sqIn = adjWIn * adjL1In;

    setVal('sheetWidthIn', swIn.toFixed(2));
    setVal('sheetLengthIn', slIn.toFixed(2));
    setVal('withFlap', wfIn.toFixed(2));
    setVal('adjWidthIn', adjWIn.toFixed(2));
    setVal('adjLength1In', adjL1In.toFixed(2));
    setVal('adjLength2In', adjL2In.toFixed(2));
    setVal('adjWidthMm', adjWMm.toFixed(0));
    setVal('adjLengthMm', adjLMm.toFixed(0));
    setVal('totalSheetSqIn', sqIn.toFixed(0));

    recalcAll();
}

// ════════════════════════════════════════════════════════════════════════════
// MAIN CALCULATION ENGINE
// ════════════════════════════════════════════════════════════════════════════
function recalcAll() {

    // 1. Ply cost total
    let plyCostTotal = 0;
    let totalKg = 0;
    document.querySelectorAll('#plyBody tr').forEach(row => {
        plyCostTotal += parseFloat(row.cells[7].querySelector('input').value) || 0;
        totalKg += parseFloat(row.cells[6].querySelector('input').value) || 0;
    });

    // 2. Glue + Silicate
    const glueRate = parseFloat(getVal('glueRate')) || 0;
    const silicateRate = parseFloat(getVal('silicateRate')) || 0;
    const glueSilKg = parseFloat(getVal('glueSilicateKg')) || 0;
    const glueSilCost = glueSilKg * (glueRate + silicateRate);
    setVal('glueSilicateCost', glueSilCost.toFixed(2));
    totalKg += glueSilKg;

    // 3. Lamination
    const laminCost = getVal('hasLamination') === 'true'
        ? (parseFloat(getVal('laminationCost')) || 0)
        : 0;

    // 4. Dickel + Printing
    const dickelCost = parseFloat(getVal('dickelCost')) || 0;
    const printingCost = parseFloat(getVal('printingCost')) || 0;

    // 5. Paper Cost Total
    const paperCost = plyCostTotal + glueSilCost + laminCost + dickelCost + printingCost;
    setVal('paperCost', paperCost.toFixed(2));

    // 6. Variable costs
    const weight = totalKg > 0 ? totalKg : 1;
    const labourRate = parseFloat(getVal('labourRate')) || 0;
    const energyRate = parseFloat(getVal('energyRate')) || 0;
    const freightRate = parseFloat(getVal('freightRate')) || 0;

    const labourCost = labourRate * weight;
    const energyCost = energyRate * weight;
    const freightCost = freightRate * weight;

    setVal('labourCost', labourCost.toFixed(2));
    setVal('energyCost', energyCost.toFixed(2));
    setVal('freightCost', freightCost.toFixed(2));

    // 7. Binding
    const bindingType = getVal('bindingType');
    let bindingCost = 0;
    if (bindingType === 'Pins') {
        const pins = parseFloat(getVal('bindingPins')) || 0;
        bindingCost = pins * 0.05;
    }
    const bcInput = document.getElementById('bindingCost');
    if (bindingType === 'None') {
        bcInput.value = '0';
        bcInput.readOnly = true;
    } else if (bindingType === 'Pins') {
        bcInput.value = bindingCost.toFixed(2);
        bcInput.readOnly = true;
    } else {
        bcInput.readOnly = false;
        bindingCost = parseFloat(bcInput.value) || 0;
    }

    // 8. Sub-Total
    const subTotal = paperCost + labourCost + energyCost + bindingCost + freightCost;
    setVal('subTotal', subTotal.toFixed(2));

    // 9. Overhead costs (% of subTotal)
    const wasteCost = pctOf(subTotal, 'wastePct');
    const adminExpCost = pctOf(subTotal, 'adminExpPct');
    const sellingDistCost = pctOf(subTotal, 'sellingDistPct');
    const repairMaintCost = pctOf(subTotal, 'repairMaintPct');
    const storeSparesCost = pctOf(subTotal, 'storeSparesPct');
    const mfgCostValue = pctOf(subTotal, 'mfgCostPct');

    setVal('wasteCost', wasteCost.toFixed(2));
    setVal('adminExpCost', adminExpCost.toFixed(2));
    setVal('sellingDistCost', sellingDistCost.toFixed(2));
    setVal('repairMaintCost', repairMaintCost.toFixed(2));
    setVal('storeSparesCost', storeSparesCost.toFixed(2));
    setVal('mfgCostValue', mfgCostValue.toFixed(2));

    const mfgTotal = subTotal + wasteCost + adminExpCost + sellingDistCost
        + repairMaintCost + storeSparesCost + mfgCostValue;
    setVal('manufacturingTotal', mfgTotal.toFixed(2));

    // 10. Commission
    const commACost = pctOf(mfgTotal, 'commPersonAPct');
    const commBCost = pctOf(mfgTotal, 'commPersonBPct');
    setVal('commPersonACost', commACost.toFixed(2));
    setVal('commPersonBCost', commBCost.toFixed(2));

    // 11. Profit
    const afterComm = mfgTotal + commACost + commBCost;
    const profitAmount = pctOf(afterComm, 'profitPct');
    setVal('profitAmount', profitAmount.toFixed(2));

    // 12. Final Cost W/O GST
    const whTax = pctOf(afterComm, 'whTaxPct');
    const finalWOGST = afterComm + profitAmount + whTax;
    setVal('finalCostWOGST', finalWOGST.toFixed(2));
    document.getElementById('finalCostDisplay').textContent = finalWOGST.toFixed(2);

    // 13. Final Rate with GST
    const gstAmt = pctOf(finalWOGST, 'gstPct');
    const taxAmt = pctOf(finalWOGST, 'taxPct');
    const finalWithGST = finalWOGST + gstAmt + taxAmt;
    setVal('finalRateWithGST', finalWithGST.toFixed(2));
    document.getElementById('finalRateDisplay').textContent = finalWithGST.toFixed(2);
}

// ════════════════════════════════════════════════════════════════════════════
// SAVE  (Draft / Final)
// ════════════════════════════════════════════════════════════════════════════
function saveDraft() { doSave('Draft'); }
function saveFinal() { doSave('Final'); }

async function doSave(status) {
    document.getElementById('status').value = status;

    if (!getVal('itemName')) {
        await alertDialog('Please enter an Item / Product Name.', { type: 'warning' }); return;
    }

    // Validate at least one ply has an item selected
    let hasValidPly = false;
    document.querySelectorAll('#plyBody tr').forEach(row => {
        if (row.cells[1].querySelector('input').value.trim() !== '') hasValidPly = true;
    });
    if (!hasValidPly) {
        await alertDialog('Please select at least one item in the Ply/Paper Layers section using the 🔍 button.', { type: 'warning' });
        return;
    }

    // Build plies array
    const plies = [];
    document.querySelectorAll('#plyBody tr').forEach((row, i) => {
        plies.push({
            SrNo: i + 1,
            ItemId: parseInt(row.cells[1].querySelector('input').value) || null,
            PlyName: row.cells[2].querySelector('input').value || '',
            Specification: row.cells[3].querySelector('input').value || '',
            GSM: parseFloat(row.cells[4].querySelector('input').value) || null,
            Rate: parseFloat(row.cells[5].querySelector('input').value) || null,
            KGs: parseFloat(row.cells[6].querySelector('input').value) || null,
            Cost: parseFloat(row.cells[7].querySelector('input').value) || null
        });
    });

    const payload = {
        CostSheetId: parseInt(getVal('editCostSheetId')) || 0,
        SheetDate: getVal('sheetDate') || null,
        CustomerId: getVal('customerId') || null,
        ItemName: getVal('itemName') || null,
        BranchID: parseInt(getVal('branchId')) || 1,
        Status: status,

        BoxStyle: getVal('boxStyle') || null,
        SizeType: getVal('sizeType') || null,
        SizeUnit: getVal('sizeUnit') || null,
        Length: n('dimL'), Width: n('dimW'),
        Height: n('dimH'), Flap: n('dimFlap'),
        AutoFlapGap: n('autoFlapGap'),
        SheetWidthIn: n('sheetWidthIn'), SheetLengthIn: n('sheetLengthIn'),
        WithFlap: n('withFlap'),
        AdjWidthIn: n('adjWidthIn'), AdjLength1In: n('adjLength1In'),
        AdjLength2In: n('adjLength2In'), AdjWidthMm: n('adjWidthMm'),
        AdjLengthMm: n('adjLengthMm'), TotalSheetSqIn: n('totalSheetSqIn'),

        PrintingType: getVal('printingType') || null,
        PrintingColors: parseInt(getVal('printingColors')) || null,
        PrintingCost: n('printingCost'),
        DickelCost: n('dickelCost'),

        HasLamination: getVal('hasLamination') === 'true',
        LaminationCost: n('laminationCost'),
        GlueRate: n('glueRate'),
        SilicateRate: n('silicateRate'),
        GlueSilicateKg: n('glueSilicateKg'),
        GlueSilicateCost: n('glueSilicateCost'),
        PaperCost: n('paperCost'),

        LabourRate: n('labourRate'), LabourCost: n('labourCost'),
        EnergyRate: n('energyRate'), EnergyCost: n('energyCost'),
        BindingType: getVal('bindingType') || null,
        BindingPins: parseInt(getVal('bindingPins')) || null,
        BindingCost: n('bindingCost'),
        FreightRate: n('freightRate'), FreightCost: n('freightCost'),
        SubTotal: n('subTotal'),

        WastePct: n('wastePct'), WasteCost: n('wasteCost'),
        AdminExpPct: n('adminExpPct'), AdminExpCost: n('adminExpCost'),
        SellingDistPct: n('sellingDistPct'), SellingDistCost: n('sellingDistCost'),
        RepairMaintPct: n('repairMaintPct'), RepairMaintCost: n('repairMaintCost'),
        StoreSparesPct: n('storeSparesPct'), StoreSparesCost: n('storeSparesCost'),
        MfgCostPct: n('mfgCostPct'), MfgCostValue: n('mfgCostValue'),
        ManufacturingTotal: n('manufacturingTotal'),

        CommPersonA: getVal('commPersonA') || null,
        CommPersonAPct: n('commPersonAPct'), CommPersonACost: n('commPersonACost'),
        CommPersonB: getVal('commPersonB') || null,
        CommPersonBPct: n('commPersonBPct'), CommPersonBCost: n('commPersonBCost'),

        WHTaxPct: n('whTaxPct'),
        GSTPercentage: n('gstPct'),
        ProfitPct: n('profitPct'), ProfitAmount: n('profitAmount'),
        FinalCostWOGST: n('finalCostWOGST'),
        MOQ: parseInt(getVal('moq')) || null,
        TaxPct: n('taxPct'),
        FinalRateWithGST: n('finalRateWithGST'),
        Remarks: getVal('remarks') || null,

        Plies: plies
    };

    const btnDraft = document.querySelector('[onclick="saveDraft()"]');
    const btnFinal = document.querySelector('[onclick="saveFinal()"]');
    btnDraft.disabled = btnFinal.disabled = true;
    btnDraft.textContent = '⏳ Saving...';

    fetch('/CostSheet/Save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(r => r.json())
        .then(async data => {
            if (data.success) {
                await alertDialog(`✅ ${data.message}`);
                document.getElementById('editCostSheetId').value = data.id;
                setVal('sheetNo', data.id);
            } else {
                await alertDialog('❌ ' + (data.message || 'Unknown error'));
            }
        })
        .catch(async err => {
            console.error(err);
            await alertDialog('❌ Error saving cost sheet. Please try again.');
        })
        .finally(() => {
            btnDraft.disabled = btnFinal.disabled = false;
            btnDraft.textContent = '💾 Save as Draft';
            btnFinal.textContent = '✅ Save as Final';
        });
}

// ════════════════════════════════════════════════════════════════════════════
// LOAD EXISTING SHEET (Edit mode)
// ════════════════════════════════════════════════════════════════════════════
async function loadExistingSheet(id) {
    try {
        const res = await fetch(`/CostSheet/GetById/${id}`);
        const json = await res.json();
        if (!json.success) { await alertDialog('Could not load cost sheet.', { type: 'error' }); return; }

        const d = json.data;

        setVal('sheetNo', d.costSheetId);
        setVal('sheetDate', d.sheetDate ? d.sheetDate.split('T')[0] : '');
        setSelect('status', d.status || 'Draft');
        setSelect('branchId', d.branchID || 1);
        setVal('customerId', d.customerId || '');
        setVal('customerName', d.customerId ? await getCustomerName(d.customerId) : '');
        setVal('itemName', d.itemName || '');

        setSelect('boxStyle', d.boxStyle || '');
        setSelect('sizeType', d.sizeType || 'External');
        setSelect('sizeUnit', d.sizeUnit || 'mm');
        setVal('dimL', d.length || '');
        setVal('dimW', d.width || '');
        setVal('dimH', d.height || '');
        setVal('dimFlap', d.flap || '');
        setVal('autoFlapGap', d.autoFlapGap ?? 0);
        setVal('sheetWidthIn', d.sheetWidthIn || '');
        setVal('sheetLengthIn', d.sheetLengthIn || '');
        setVal('withFlap', d.withFlap || '');
        setVal('adjWidthIn', d.adjWidthIn || '');
        setVal('adjLength1In', d.adjLength1In || '');
        setVal('adjLength2In', d.adjLength2In || '');
        setVal('adjWidthMm', d.adjWidthMm || '');
        setVal('adjLengthMm', d.adjLengthMm || '');
        setVal('totalSheetSqIn', d.totalSheetSqIn || '');

        setSelect('printingType', d.printingType || '');
        setVal('printingColors', d.printingColors ?? 0);
        setVal('printingCost', d.printingCost ?? 0);
        setVal('dickelCost', d.dickelCost ?? 0);

        setSelect('hasLamination', d.hasLamination ? 'true' : 'false');
        setVal('laminationCost', d.laminationCost ?? 0);
        setVal('glueRate', d.glueRate ?? 0);
        setVal('silicateRate', d.silicateRate ?? 0);
        setVal('glueSilicateKg', d.glueSilicateKg ?? 0);

        setVal('labourRate', d.labourRate ?? 0);
        setVal('energyRate', d.energyRate ?? 0);
        setSelect('bindingType', d.bindingType || 'None');
        setVal('bindingPins', d.bindingPins ?? 0);
        setVal('freightRate', d.freightRate ?? 0);

        setVal('wastePct', d.wastePct ?? 0);
        setVal('adminExpPct', d.adminExpPct ?? 0);
        setVal('sellingDistPct', d.sellingDistPct ?? 0);
        setVal('repairMaintPct', d.repairMaintPct ?? 0);
        setVal('storeSparesPct', d.storeSparesPct ?? 0);
        setVal('mfgCostPct', d.mfgCostPct ?? 0);

        setVal('commPersonA', d.commPersonA || '');
        setVal('commPersonAPct', d.commPersonAPct ?? 0);
        setVal('commPersonB', d.commPersonB || '');
        setVal('commPersonBPct', d.commPersonBPct ?? 0);
        setVal('whTaxPct', d.whTaxPct ?? 0);
        setVal('gstPct', d.gSTPercentage ?? 0);
        setVal('profitPct', d.profitPct ?? 0);
        setVal('moq', d.mOQ ?? 0);
        setVal('taxPct', d.taxPct ?? 0);
        setVal('remarks', d.remarks || '');

        // Plies — restore from saved data including itemId
        if (d.plies && d.plies.length > 0) {
            d.plies.forEach(p =>
                addPlyRow(
                    p.plyName || '',
                    p.specification || '',
                    p.gSM ?? '',
                    p.rate ?? '',
                    p.kGs ?? '',
                    p.itemId ?? ''
                )
            );
        } else {
            addPlyRow();
        }

        recalcAll();
    } catch (err) {
        console.error(err);
        await alertDialog('❌ Error loading cost sheet data.');
    }
}

async function getCustomerName(customerId) {
    try {
        const res = await fetch('/CostSheet/GetCustomers');
        const json = await res.json();
        const c = json.customers?.find(x => x.partyId.toString() === customerId.toString());
        return c ? c.partyName : customerId;
    } catch { return customerId; }
}

// ════════════════════════════════════════════════════════════════════════════
// RESET FORM
// ════════════════════════════════════════════════════════════════════════════
async function resetForm() {
    if (!(await confirmDialog('Reset form? All unsaved changes will be lost.'))) return;

    setVal('sheetDate', new Date().toISOString().split('T')[0]);
    setSelect('status', 'Draft');
    setSelect('branchId', '1');
    setVal('customerId', '');
    setVal('customerName', '');
    setVal('itemName', '');

    ['dimL', 'dimW', 'dimH', 'dimFlap', 'autoFlapGap',
        'sheetWidthIn', 'sheetLengthIn', 'withFlap',
        'adjWidthIn', 'adjLength1In', 'adjLength2In',
        'adjWidthMm', 'adjLengthMm', 'totalSheetSqIn'].forEach(id => setVal(id, ''));

    setSelect('printingType', '');
    setVal('printingColors', 0);
    setVal('printingCost', 0);
    setVal('dickelCost', 0);
    setSelect('hasLamination', 'false');
    setVal('laminationCost', 0);
    setVal('glueRate', 0);
    setVal('silicateRate', 0);
    setVal('glueSilicateKg', 0);

    // FIX: Reset to 3 blank rows — no hardcoded names
    document.getElementById('plyBody').innerHTML = '';
    plyCounter = 0;
    addPlyRow();
    addPlyRow();
    addPlyRow();

    setSelect('bindingType', 'None');
    setVal('bindingPins', 0);
    setVal('commPersonA', ''); setVal('commPersonAPct', 0);
    setVal('commPersonB', ''); setVal('commPersonBPct', 0);
    setVal('whTaxPct', 0);
    setVal('gstPct', 0);
    setVal('moq', 0);
    setVal('taxPct', 0);
    setVal('remarks', '');

    applySettingsToForm();
    loadNextSheetNumber();
    document.getElementById('editCostSheetId').value = '0';
}

// ════════════════════════════════════════════════════════════════════════════
// CUSTOMER MODAL
// ════════════════════════════════════════════════════════════════════════════
function openCustomerModal() {
    selectedCustomer = null;
    document.getElementById('selectCustomerBtn').disabled = true;
    document.getElementById('customerSearchInput').value = '';
    document.getElementById('customerModal').classList.add('show');
    loadCustomers();
}

function closeCustomerModal() {
    document.getElementById('customerModal').classList.remove('show');
    selectedCustomer = null;
    document.getElementById('selectCustomerBtn').disabled = true;
    document.querySelectorAll('#customerTableBody tr').forEach(r => r.classList.remove('selected'));
}

function loadCustomers() {
    const tbody = document.getElementById('customerTableBody');
    tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:2rem;">Loading...</td></tr>';

    fetch('/CostSheet/GetCustomers')
        .then(r => r.json())
        .then(data => {
            if (data.success && data.customers?.length) {
                allCustomers = data.customers;
                renderCustomers(allCustomers);
            } else {
                tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:2rem;color:#999;">No customers found.</td></tr>';
            }
        })
        .catch(() => {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:2rem;color:#dc3545;">Error loading customers.</td></tr>';
        });
}

function renderCustomers(list) {
    const tbody = document.getElementById('customerTableBody');
    if (!list.length) {
        tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:2rem;color:#999;">No customers found.</td></tr>';
        return;
    }
    tbody.innerHTML = '';
    list.forEach(c => {
        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${c.partyId}</td>
            <td>${escHtml(c.partyName)}</td>
            <td><span class="party-badge party-badge-${c.partyType}">${capitalize(c.partyType)}</span></td>
            <td>${c.phone || '–'}</td>
            <td>${c.email || '–'}</td>
        `;
        tr.addEventListener('click', function () { selectCustomerRow(this, c); });
        tbody.appendChild(tr);
    });
}

function selectCustomerRow(row, customer) {
    document.querySelectorAll('#customerTableBody tr').forEach(r => r.classList.remove('selected'));
    row.classList.add('selected');
    selectedCustomer = customer;
    document.getElementById('selectCustomerBtn').disabled = false;
}

function selectCustomer() {
    if (!selectedCustomer) return;
    setVal('customerId', selectedCustomer.partyId);
    setVal('customerName', selectedCustomer.partyName);
    closeCustomerModal();
}

function searchCustomers() {
    const q = document.getElementById('customerSearchInput').value.toLowerCase();
    if (!q) { renderCustomers(allCustomers); return; }
    renderCustomers(allCustomers.filter(c =>
        c.partyName?.toLowerCase().includes(q) ||
        c.phone?.includes(q) ||
        c.email?.toLowerCase().includes(q)
    ));
}

document.getElementById('customerModal').addEventListener('click', function (e) {
    if (e.target === this) closeCustomerModal();
});
document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && document.getElementById('customerModal').classList.contains('show'))
        closeCustomerModal();
});

// ════════════════════════════════════════════════════════════════════════════
// PLY ITEM LOOKUP MODAL
// ════════════════════════════════════════════════════════════════════════════
let allPlyItems = [];
let selectedPlyItem = null;
let currentPlyRow = null;

function openPlyItemLookup(btn) {
    currentPlyRow = btn.closest('tr');
    selectedPlyItem = null;
    document.getElementById('selectPlyItemBtn').disabled = true;
    document.getElementById('plyItemSearch').value = '';
    document.getElementById('plyCategoryFilter').value = '';
    document.getElementById('plyItemModal').classList.add('show');
    loadPlyItems();
}

function closePlyItemModal() {
    document.getElementById('plyItemModal').classList.remove('show');
    selectedPlyItem = null;
    currentPlyRow = null;
    document.getElementById('selectPlyItemBtn').disabled = true;
    document.querySelectorAll('#plyItemTableBody tr')
        .forEach(r => r.classList.remove('selected'));
}

function loadPlyItems() {
    const tbody = document.getElementById('plyItemTableBody');
    tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:2rem;">Loading...</td></tr>';

    fetch('/CostSheet/GetItemsForPly')
        .then(r => r.json())
        .then(data => {
            if (data.success && data.items?.length) {
                allPlyItems = data.items;
                loadPlyCategoryFilter(data.items);
                renderPlyItems(data.items);
            } else {
                document.getElementById('plyItemEmptyState').style.display = 'block';
                tbody.innerHTML = '';
            }
        })
        .catch(() => {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:2rem;color:#dc3545;">Error loading items.</td></tr>';
        });
}

function loadPlyCategoryFilter(items) {
    const sel = document.getElementById('plyCategoryFilter');
    const categories = [...new Set(items.map(i => i.categoryName).filter(Boolean))].sort();
    sel.innerHTML = '<option value="">All Categories</option>';
    categories.forEach(cat => {
        const opt = document.createElement('option');
        opt.value = opt.textContent = cat;
        sel.appendChild(opt);
    });
}

function filterPlyItems() {
    const search = document.getElementById('plyItemSearch').value.toLowerCase();
    const category = document.getElementById('plyCategoryFilter').value;

    const filtered = allPlyItems.filter(item => {
        const matchText = !search ||
            item.itemName?.toLowerCase().includes(search) ||
            item.categoryName?.toLowerCase().includes(search);
        const matchCat = !category || item.categoryName === category;
        return matchText && matchCat;
    });
    renderPlyItems(filtered);
}

function renderPlyItems(items) {
    const tbody = document.getElementById('plyItemTableBody');
    const empty = document.getElementById('plyItemEmptyState');

    if (!items.length) {
        tbody.innerHTML = '';
        empty.style.display = 'block';
        return;
    }
    empty.style.display = 'none';
    tbody.innerHTML = '';

    items.forEach(item => {
        const stock = item.currentStock || 0;
        let stockBadge;
        if (stock > 50) stockBadge = `<span class="stock-badge stock-badge-high">${stock}</span>`;
        else if (stock > 0) stockBadge = `<span class="stock-badge stock-badge-low">${stock}</span>`;
        else stockBadge = `<span class="stock-badge stock-badge-out">${stock}</span>`;

        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${item.itemId}</td>
            <td><strong>${escHtml(item.itemName)}</strong></td>
            <td>${escHtml(item.categoryName)}</td>
            <td style="text-align:right;font-weight:600;color:#1e40af;">
                ${parseFloat(item.lastPurPrice || 0).toFixed(2)}
            </td>
            <td style="text-align:center;">${stockBadge}</td>
        `;
        tr.addEventListener('click', function () { selectPlyItemRow(this, item); });
        tbody.appendChild(tr);
    });
}

function selectPlyItemRow(row, item) {
    document.querySelectorAll('#plyItemTableBody tr')
        .forEach(r => r.classList.remove('selected'));
    row.classList.add('selected');
    selectedPlyItem = item;
    document.getElementById('selectPlyItemBtn').disabled = false;
}

async function confirmPlyItem() {
    if (!selectedPlyItem || !currentPlyRow) return;

    // Fill ItemId, Item Name, Rate from selected purchase item
    currentPlyRow.cells[1].querySelector('input').value = selectedPlyItem.itemId;
    currentPlyRow.cells[2].querySelector('input').value = selectedPlyItem.itemName;
    currentPlyRow.cells[5].querySelector('input').value =
        parseFloat(selectedPlyItem.lastPurPrice || 0).toFixed(2);

    if (!selectedPlyItem.lastPurPrice || selectedPlyItem.lastPurPrice === 0) {
        await alertDialog(`⚠️ "${selectedPlyItem.itemName}" has no purchase history.\nRate set to 0 — please enter manually.`);
    }
    if ((selectedPlyItem.currentStock || 0) <= 0) {
        await alertDialog(`⚠️ "${selectedPlyItem.itemName}" has zero stock.\nPlease create a Purchase Invoice first.`);
    }

    // Auto-focus KGs field for fast entry
    setTimeout(() => currentPlyRow.cells[6].querySelector('input').focus(), 100);

    calcPlyRow(currentPlyRow.cells[5].querySelector('input'));
    closePlyItemModal();
}

document.getElementById('plyItemModal').addEventListener('click', function (e) {
    if (e.target === this) closePlyItemModal();
});
document.addEventListener('keydown', e => {
    if (e.key === 'Escape' &&
        document.getElementById('plyItemModal').classList.contains('show'))
        closePlyItemModal();
});

// ════════════════════════════════════════════════════════════════════════════
// UTILITY HELPERS
// ════════════════════════════════════════════════════════════════════════════
function getVal(id) {
    const el = document.getElementById(id);
    return el ? el.value : '';
}

function setVal(id, val) {
    const el = document.getElementById(id);
    if (el) el.value = val ?? '';
}

function setSelect(id, val) {
    const el = document.getElementById(id);
    if (!el) return;
    const opt = [...el.options].find(o => o.value == val || o.text == val);
    if (opt) el.value = opt.value;
}

function n(id) {
    const v = parseFloat(getVal(id));
    return isNaN(v) ? null : v;
}

function pctOf(base, pctInputId) {
    const pct = parseFloat(getVal(pctInputId)) || 0;
    return (base * pct) / 100;
}

function escHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;')
        .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function escAttr(str) {
    if (!str) return '';
    return String(str).replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

function capitalize(s) {
    if (!s) return '';
    return s.charAt(0).toUpperCase() + s.slice(1);
}