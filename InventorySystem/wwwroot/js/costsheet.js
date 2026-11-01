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
let settings = {};
let settingsLoaded = false;
let legacyAdhesiveReview = false;
let materialOptions = [];
let automaticApplications=true,restoringSheet=false;

// ════════════════════════════════════════════════════════════════════════════
// INIT
// ════════════════════════════════════════════════════════════════════════════
window.addEventListener('DOMContentLoaded', async function () {
    if (!(await loadSettings())) return;
    await loadMaterialOptions();

    const editId = parseInt(document.getElementById('editCostSheetId').value) || 0;

    if (editId > 0) {
        await loadExistingSheet(editId);
    } else {
        loadNextSheetNumber();
        applySettingsToForm();
        applyCostingMethod(true);
        // Start with 3 blank rows — user uses 🔍 to pick items from purchase
        addPlyRow();
        addPlyRow();
        addPlyRow();
    }

    document.getElementById('addPlyBtn').addEventListener('click', () => addPlyRow());
    document.getElementById('hasLamination').addEventListener('change',()=>{automaticApplications=true;syncApplications();recalcAll();});
    document.getElementById('addMatBtn').addEventListener('click', () => addMatRow());
});

// ════════════════════════════════════════════════════════════════════════════
// SETTINGS
// ════════════════════════════════════════════════════════════════════════════
async function loadSettings() {
    try {
        const res = await fetch('/CostSheet/GetSettings');
        if (!res.ok) throw new Error('Settings unavailable');
        settings = await res.json(); settingsLoaded = true; return true;
    } catch {
        await alertDialog('Saved costing rates could not be loaded. Refresh or contact the administrator; saving is disabled until rates load.', {type:'warning'});
        return false;
    }
}

async function loadMaterialOptions() {
    const res=await fetch('/CostSheet/GetMaterialOptions');if(!res.ok)throw new Error('Material options unavailable');
    const data=await res.json();if(!data.success)throw new Error('Material options unavailable');
    installMaterialOptions(data.items);
}
function installMaterialOptions(items){
    materialOptions=items.filter(i=>i.type==='RawMaterial');
    const finished=document.getElementById('finishedItemId'),value=finished.value;
    finished.replaceChildren(new Option('Not linked',''));for(const item of items.filter(i=>i.type==='FinishedGoods'&&i.unit==='pcs'))finished.add(new Option(item.name+' ('+item.unit+')',item.id));finished.value=value;
    fillRoleOptions('glueItemId','Glue');fillRoleOptions('silicateItemId','Silicate');
    fillRoleOptions('client_glueLaminationItemId','LaminationGlue');fillRoleOptions('client_laminationChemicalItemId','LaminationChemical');
}
function fillRoleOptions(id,role,saved){
    const select=document.getElementById(id),value=String(saved??select.value??'');
    const options=materialOptions.filter(i=>i.unit==='kg'&&i.materialRole===role);
    select.replaceChildren(new Option('Select '+role.replace(/([a-z])([A-Z])/g,'$1 $2')+' stock item',''));
    for(const item of options)select.add(new Option(item.name+' (KG)',String(item.id)));
    if(value&&!options.some(i=>String(i.id)===value)){
        const old=materialOptions.find(i=>String(i.id)===value);
        select.add(new Option((old?.name||'Item #'+value)+' (saved — review material use)',value));
    }
    select.value=value|| (options.length===1?String(options[0].id):'');
}
function syncApplications(){
    if(!isClientCosting()||!automaticApplications)return;
    const count=document.getElementById('plyBody').rows.length;
    setVal('client_glueApplications',Math.max(0,Math.floor((count-1)/2)));setVal('client_silicateApplications',getVal('client_glueApplications'));
    const lam=getVal('hasLamination')==='true'?1:0;setVal('client_glueLaminationApplications',lam);setVal('client_laminationChemicalApplications',lam);
}
async function refreshLatestRates(){
    const button=document.getElementById('refreshRatesBtn'),notice=document.getElementById('refreshRatesStatus');button.disabled=true;notice.textContent='Fetching latest rates…';
    try{
        const responses=await Promise.all([fetch('/CostSheet/GetSettings'),fetch('/CostSheet/GetMaterialOptions')]);
        if(responses.some(r=>!r.ok))throw new Error('Latest rates could not be fetched. Your sheet has been kept.');
        const [latest,data]=await Promise.all(responses.map(r=>r.json()));if(!data.success||!Array.isArray(data.items))throw new Error('Latest material rates unavailable. Your sheet has been kept.');
        settings=latest;settingsLoaded=true;installMaterialOptions(data.items);applySettingsToForm();
        const byItem=new Map(data.items.map(i=>[String(i.id),i]));let changed=0,missing=0;
        function price(id,set){if(!id)return;const item=byItem.get(String(id));if(item?.lastPurchasePrice!=null){set(item.lastPurchasePrice);changed++;}else missing++;}
        for(const row of document.querySelectorAll('#plyBody tr'))price(row.cells[1].querySelector('input').value,v=>row.cells[5].querySelector('input').value=v);
        for(const row of document.querySelectorAll('#matBody tr'))price(row.querySelector('.mat-item').value,v=>row.querySelector('.mat-rate').value=v);
        for(const [item,rate] of [['glueItemId','glueRate'],['silicateItemId','silicateRate'],['client_glueLaminationItemId','client_glueLaminationRate'],['client_laminationChemicalItemId','client_laminationChemicalRate']])price(getVal(item),v=>setVal(rate,v));
        automaticApplications=true;syncApplications();recalcAll();
        notice.textContent='Latest costing settings and '+changed+' material rates applied. Dimensions, quantities and specifications were kept. Save the sheet to retain these changes.'+(missing?' '+missing+' linked items have no current purchase price; their entered rates were kept.':'');
    }catch(e){notice.textContent=e.message;}finally{button.disabled=false;}
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
function addPlyRow(plyName = '', spec = '', gsm = '', rate = '', kgs = '', itemId = '', fluted = null, fluteFactor = null) {
    if(!restoringSheet)automaticApplications=true;
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
                   title="Paper GSM" oninput="calcPlyRow(this)">
            <select class="ply-input ply-fluted" style="display:${isClientCosting() ? '' : 'none'}" aria-label="Layer type" onchange="recalcAll()">
                <option value="false"${(fluted ?? (plyCounter % 2 === 0)) ? '' : ' selected'}>Plain</option>
                <option value="true"${(fluted ?? (plyCounter % 2 === 0)) ? ' selected' : ''}>Fluted</option>
            </select><input type="number" class="ply-input ply-factor" aria-label="Flute factor" title="Flute factor for this layer" min="1" max="10" step="0.01" value="${fluteFactor ?? (plyCounter === 4 ? 1.35 : Number(getVal('client_fluteFactor')) || 1.45)}" oninput="recalcAll()" style="display:${isClientCosting() && clientFormulaVersion === 2 ? '' : 'none'}"></td>
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
    if (isClientCosting()) recalcClientCosting();

    if (rate !== '' || kgs !== '') {
        calcPlyRow(tr.cells[5].querySelector('input'));
    }
}

function calcPlyRow(input) {
    if (isClientCosting()) { recalcClientCosting(); return; }
    const row = input.closest('tr');
    const rate = parseFloat(row.cells[5].querySelector('input').value) || 0;
    const kgs = parseFloat(row.cells[6].querySelector('input').value) || 0;
    row.cells[7].querySelector('input').value = (rate * kgs).toFixed(2);
    recalcAll();
}

async function removePlyRow(btn) {
    automaticApplications=true;
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
    if (isClientCosting()) { recalcClientCosting(); return; }
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
// ── Other materials table (kg or pcs, cost only) ──
function addMatRow(itemId = '', qty = '', rate = '') {
    const tr = document.createElement('tr');
    const options = materialOptions.map(i =>
        `<option value="${i.id}" data-unit="${escAttr(i.unit)}"${String(i.id) === String(itemId) ? ' selected' : ''}>${escHtml(i.name)} (${escHtml(i.unit)})</option>`).join('');
    tr.innerHTML = `
        <td></td>
        <td><select class="ply-input mat-item" style="width:100%;" onchange="calcMatRow(this)"><option value="">Select raw material</option>${options}</select></td>
        <td class="mat-unit"></td>
        <td><input type="number" class="ply-input mat-qty" min="0" step="0.000001" value="${qty}" oninput="calcMatRow(this)"></td>
        <td><input type="number" class="ply-input mat-rate" min="0" step="0.01" value="${rate}" oninput="calcMatRow(this)"></td>
        <td><input type="number" class="ply-input calc mat-cost" value="0" readonly></td>
        <td><button type="button" class="btn-remove-ply" onclick="removeMatRow(this)">✖</button></td>`;
    document.getElementById('matBody').appendChild(tr);
    rebuildMatNumbers();
    calcMatRow(tr.querySelector('.mat-item'));
}

function calcMatRow(el) {
    const row = el.closest('tr');
    const sel = row.querySelector('.mat-item');
    row.querySelector('.mat-unit').textContent = sel.selectedOptions[0]?.dataset.unit || '';
    const qty = parseFloat(row.querySelector('.mat-qty').value) || 0;
    const rate = parseFloat(row.querySelector('.mat-rate').value) || 0;
    row.querySelector('.mat-cost').value = (qty * rate).toFixed(2);
    recalcAll();
}

function removeMatRow(btn) {
    btn.closest('tr').remove();
    rebuildMatNumbers();
    recalcAll();
}

function rebuildMatNumbers() {
    document.querySelectorAll('#matBody tr').forEach((r, i) => { r.cells[0].textContent = i + 1; });
}

function recalcAll() {
    if (!isClientCosting()) document.querySelectorAll('#plyBody tr').forEach(r => { r.cells[6].querySelector('input').readOnly=false; r.querySelector('.ply-fluted').style.display='none'; r.querySelector('.ply-factor').style.display='none'; });
    if (isClientCosting()) { recalcClientCosting(); return; }
    if (getVal('hasLamination') !== 'true') setVal('laminationCost', 0);

    // 0. Other materials cost (not part of box weight)
    let matCostTotal = 0;
    document.querySelectorAll('#matBody .mat-cost').forEach(c => { matCostTotal += parseFloat(c.value) || 0; });
    setVal('otherMaterialsCost', matCostTotal.toFixed(2));

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
    const glueKg = parseFloat(getVal('glueKgPerBox')) || 0;
    const silicateKg = parseFloat(getVal('silicateKgPerBox')) || 0;
    const glueSilKg = glueKg + silicateKg;
    setVal('glueSilicateKg', glueSilKg);
    const glueSilCost = glueKg * glueRate + silicateKg * silicateRate;
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
    const paperCost = plyCostTotal + matCostTotal + glueSilCost + laminCost + dickelCost + printingCost;
    setVal('paperCost', paperCost.toFixed(2));

    // 6. Variable costs
    const weight = totalKg;
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
    if (!settingsLoaded) { await alertDialog('Refresh to load saved costing rates first.'); return; }
    if (legacyAdhesiveReview && !(n('glueKgPerBox') > 0 || n('silicateKgPerBox') > 0)) { await alertDialog('This older sheet has a combined adhesive estimate. Enter separate glue/silicate kg per box and select their stock items.'); return; }
    recalcAll();
    document.getElementById('status').value = status;

    if (!getVal('itemName')) {
        await alertDialog('Please enter an Item / Product Name.', { type: 'warning' }); return;
    }

    // Costing may be saved before inventory records are linked.
    const hasPly = [...document.querySelectorAll('#plyBody tr')].some(row =>
        Number(row.cells[4].querySelector('input').value) > 0 || Number(row.cells[6].querySelector('input').value) > 0);
    if (!hasPly && status === 'Final') {
        await alertDialog('Enter at least one paper layer before saving as Final.', { type: 'warning' }); return;
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

    const materials = [];
    document.querySelectorAll('#matBody tr').forEach((row, i) => {
        materials.push({
            SrNo: i + 1,
            ItemId: parseInt(row.querySelector('.mat-item').value) || null,
            Quantity: parseFloat(row.querySelector('.mat-qty').value) || null,
            Rate: parseFloat(row.querySelector('.mat-rate').value) || null,
            Cost: parseFloat(row.querySelector('.mat-cost').value) || null
        });
    });

    const payload = {
        ClientCosting: isClientCosting() ? getClientOptions() : null,
        CostSheetId: parseInt(getVal('editCostSheetId')) || 0,
        SheetDate: getVal('sheetDate') || null,
        FinishedItemId: parseInt(getVal('finishedItemId')) || null,
        GlueItemId: parseInt(getVal('glueItemId')) || null, SilicateItemId: parseInt(getVal('silicateItemId')) || null,
        GlueKgPerBox: n('glueKgPerBox'), SilicateKgPerBox: n('silicateKgPerBox'),
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

        Plies: plies,
        Materials: materials
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
    restoringSheet=true;
    try {
        const res = await fetch(`/CostSheet/GetById/${id}`);
        const json = await res.json();
        if (!json.success) { await alertDialog('Could not load cost sheet.', { type: 'error' }); return; }

        const d = json.data;

        setVal('sheetNo', d.costSheetId);
        setVal('sheetDate', d.sheetDate ? d.sheetDate.split('T')[0] : '');
        setSelect('status', d.status || 'Draft');
        setSelect('branchId', d.branchID || 1);
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
        setSelect('finishedItemId', d.finishedItemId);fillRoleOptions('glueItemId','Glue',d.glueItemId||'');fillRoleOptions('silicateItemId','Silicate',d.silicateItemId||'');
        setVal('glueKgPerBox', d.glueKgPerBox ?? 0); setVal('silicateKgPerBox', d.silicateKgPerBox ?? 0);
        legacyAdhesiveReview = d.glueSilicateKg > 0 && d.glueKgPerBox == null && d.silicateKgPerBox == null;
        if (legacyAdhesiveReview) await alertDialog('Review this older sheet: separate its combined adhesive weight into glue and silicate KG per box before saving.');
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

        setSelect('costingMethod', d.clientCosting ? 'Client' : 'Manual');
        if (d.clientCosting) restoreClientOptions(d.clientCosting);
        applyCostingMethod(false);
        // Plies — restore from saved data including itemId
        if (d.plies && d.plies.length > 0) {
            d.plies.forEach((p, i) =>
                addPlyRow(
                    p.plyName || '',
                    p.specification || '',
                    p.gSM ?? '',
                    p.rate ?? '',
                    p.kGs ?? '',
                    p.itemId ?? '',
                    d.clientCosting?.flutedLayers?.[i] ?? null,
                    d.clientCosting?.layerFluteFactors?.[i] ?? d.clientCosting?.fluteFactor ?? null
                )
            );
        } else {
            addPlyRow();
        }

        document.getElementById('matBody').innerHTML = '';
        (d.materials || []).filter(x => !d.clientCosting || (x.itemId !== d.clientCosting.glueLaminationItemId && x.itemId !== d.clientCosting.laminationChemicalItemId))
            .forEach(x => addMatRow(x.itemId, x.quantity ?? '', x.rate ?? ''));

        recalcAll();
    } catch (err) {
        console.error(err);
        await alertDialog('❌ Error loading cost sheet data.');
    }finally{restoringSheet=false;}
}


// ════════════════════════════════════════════════════════════════════════════
// RESET FORM
// ════════════════════════════════════════════════════════════════════════════
async function resetForm() {
    if (!(await confirmDialog('Reset form? All unsaved changes will be lost.'))) return;
    if (typeof clearCostSheetExcel === 'function') clearCostSheetExcel();

    setVal('sheetDate', new Date().toISOString().split('T')[0]);
    setSelect('status', 'Draft');
    setSelect('branchId', '1');
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
    legacyAdhesiveReview = false;
    setVal('finishedItemId', ''); setVal('glueItemId', ''); setVal('silicateItemId', '');
    setVal('glueKgPerBox', 0); setVal('silicateKgPerBox', 0);
    setVal('glueRate', 0);
    setVal('silicateRate', 0);
    setVal('glueSilicateKg', 0);

    // FIX: Reset to 3 blank rows — no hardcoded names
    document.getElementById('matBody').innerHTML = '';
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
    setSelect('costingMethod', 'Client'); restoreClientOptions({}); applyCostingMethod(true);
    loadNextSheetNumber();
    document.getElementById('editCostSheetId').value = '0';
}

// ════════════════════════════════════════════════════════════════════════════
// PLY ITEM LOOKUP
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
    // Inventory mapping must not replace a quotation's imported Excel rate.
    const keepExcelRate = currentPlyRow.dataset.excelRate === 'true';
    if (!keepExcelRate) currentPlyRow.cells[5].querySelector('input').value =
        parseFloat(selectedPlyItem.lastPurPrice || 0).toFixed(2);

    if (!keepExcelRate && (!selectedPlyItem.lastPurPrice || selectedPlyItem.lastPurPrice === 0)) {
        await alertDialog(`⚠️ "${escapeHtml(selectedPlyItem.itemName)}" has no purchase history.\nRate set to 0 — please enter manually.`);
    }
    if ((selectedPlyItem.currentStock || 0) <= 0) {
        await alertDialog(`⚠️ "${escapeHtml(selectedPlyItem.itemName)}" has zero stock.\nPlease create a Purchase Invoice first.`);
    }

    // Auto-focus KGs field for fast entry
    const kgInput = currentPlyRow.cells[6].querySelector('input');
    setTimeout(() => { if (kgInput.isConnected) kgInput.focus(); }, 100);

    calcPlyRow(currentPlyRow.cells[5].querySelector('input'));
    if (typeof updateExcelImportGuidance === 'function') updateExcelImportGuidance();
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
    if (!el || el.type === 'hidden') return;
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
// Client workbook adapter. Existing recipes stay on their saved calculation method.
function isClientCosting() { return document.getElementById('costingMethod')?.value === 'Client'; }
let clientFormulaVersion = 2;
function getClientOptions() {
    syncApplications();
    const c = {version:clientFormulaVersion,automaticApplications};
    document.querySelectorAll('[data-client-option]').forEach(e => c[e.dataset.clientOption] = Number(e.value) || 0);
    document.querySelectorAll('[data-client-stock]').forEach(e => c[e.dataset.clientStock] = Number(e.value) || null);
    c.flutedLayers = [...document.querySelectorAll('#plyBody .ply-fluted')].map(e => e.value === 'true');
    c.layerFluteFactors = clientFormulaVersion === 2 ? [...document.querySelectorAll('#plyBody .ply-factor')].map(e => Number(e.value) || 0) : [];
    return c;
}
function restoreClientOptions(c) {
    automaticApplications=c.automaticApplications??c.AutomaticApplications??false;
    clientFormulaVersion = c.version ?? c.Version ?? 2;
    document.querySelectorAll('[data-client-option]').forEach(e => e.value = c[e.dataset.clientOption] ?? e.defaultValue);
    fillRoleOptions('client_glueLaminationItemId','LaminationGlue',c.glueLaminationItemId||'');fillRoleOptions('client_laminationChemicalItemId','LaminationChemical',c.laminationChemicalItemId||'');
}
function applyCostingMethod(defaults) {
    const client = isClientCosting();
    document.getElementById('clientCostingPanel').hidden = !client;
    document.querySelectorAll('[data-client-weight-label]').forEach(e => e.textContent = e.dataset.clientWeightLabel + (client ? ' Rate / paper KG' : ' Rate'));
    document.querySelectorAll('[data-client-rate-label]').forEach(e => e.textContent = e.dataset.clientRateLabel + (client ? ' / paper KG' : ' %'));
    ['commPersonAPct','commPersonBPct','taxPct'].forEach(id => document.getElementById(id).readOnly = false);
    document.getElementById('additionalTaxLabel').textContent = client ? 'Additional Tax % (on base cost)' : 'Tax % (on Final)';
    document.getElementById('clientFormulaNote').textContent = clientFormulaVersion === 2 ? 'Uses the latest client workbook formulas. GST is shown separately and is excluded from the final rate.' : 'Uses the earlier saved client formulas, including GST on cost plus profit.';
    document.getElementById('clientFormulaDetails').textContent = clientFormulaVersion === 2 ? 'Final rate follows the workbook: freight is included twice, additional tax before MOQ, and withholding after MOQ. Commission A uses operating cost × input; Commission B is zero and neither commission is added to the final rate.' : 'Commissions, profit and additional tax use base cost percentages. GST uses cost plus profit.';
    document.getElementById('clientGstLabel').textContent = clientFormulaVersion === 2 ? 'GST amount (shown separately)' : 'GST amount (cost + profit)';
    document.getElementById('finalRateLabel').textContent = clientFormulaVersion === 2 && client ? 'Final Rate' : 'Final Rate (W/ GST)';
    if(clientFormulaVersion === 2 && client) document.querySelector('[data-client-weight-label="Freight"]').textContent = 'Freight';
    ['glueKgPerBox','silicateKgPerBox','printingCost','laminationCost','sheetWidthIn','sheetLengthIn','withFlap','adjWidthIn','adjLength1In','adjLength2In','adjWidthMm','adjLengthMm','totalSheetSqIn'].forEach(id => document.getElementById(id).readOnly = client);
    if (defaults && client) {
        const rates = {labourRate:10,energyRate:5,freightRate:0,wastePct:5.25,adminExpPct:2.5,sellingDistPct:2.5,repairMaintPct:2.25,storeSparesPct:2.25,mfgCostPct:2.75,
            glueRate:50,silicateRate:45,profitPct:4.75,gstPct:0,moq:1,bindingPins:0};
        Object.entries(rates).forEach(([id,v]) => setVal(id,v));
    }
    [...document.getElementById('boxStyle').options].forEach(o => o.disabled = client && o.value !== 'Regular Slotted Carton (RSC)');
    if (client && getVal('boxStyle') !== 'Regular Slotted Carton (RSC)') setSelect('boxStyle','Regular Slotted Carton (RSC)');
    const tax=document.getElementById('taxPct').closest('.cs-fg');document.getElementById(client?'clientRatesInputs':'manualTaxLocation').append(tax);
    recalcAll();
}
function recalcClientCosting() {
    const c = getClientOptions(), m = {};
    const fields = {length:'dimL',width:'dimW',height:'dimH',flap:'dimFlap',autoFlapGap:'autoFlapGap',printingColors:'printingColors',glueRate:'glueRate',silicateRate:'silicateRate',labourRate:'labourRate',energyRate:'energyRate',freightRate:'freightRate',bindingPins:'bindingPins',bindingCost:'bindingCost',dickelCost:'dickelCost',wastePct:'wastePct',adminExpPct:'adminExpPct',sellingDistPct:'sellingDistPct',repairMaintPct:'repairMaintPct',storeSparesPct:'storeSparesPct',mfgCostPct:'mfgCostPct',profitPct:'profitPct',gstPct:'gstPct',whTaxPct:'whTaxPct',moq:'moq',commPersonAPct:'commPersonAPct',commPersonBPct:'commPersonBPct',taxPct:'taxPct'};
    Object.entries(fields).forEach(([key,id]) => m[key] = Number(getVal(id)) || 0);
    m.sizeUnit = getVal('sizeUnit'); m.bindingType = getVal('bindingType'); m.hasLamination = getVal('hasLamination') === 'true';
    m.otherMaterialsCost = [...document.querySelectorAll('#matBody tr')].filter(r => {
        const id = Number(r.querySelector('.mat-item').value);
        return id !== c.glueLaminationItemId && id !== c.laminationChemicalItemId;
    }).reduce((sum,r) => sum+(Number(r.querySelector('.mat-qty').value)||0)*(Number(r.querySelector('.mat-rate').value)||0),0);
    const rows = [...document.querySelectorAll('#plyBody tr')];
    m.plies = rows.map(r => ({gsm:Number(r.cells[4].querySelector('input').value)||0,rate:Number(r.cells[5].querySelector('input').value)||0}));
    const out = ClientCartonCosting.calculate(m,c);
    rows.forEach((r,i) => {
        r.cells[6].querySelector('input').readOnly = true;
        r.cells[6].querySelector('input').value = out.plies[i].kg.toFixed(6);
        r.cells[7].querySelector('input').value = out.plies[i].cost.toFixed(2);
        r.querySelector('.ply-fluted').style.display = '';
        r.querySelector('.ply-factor').style.display = clientFormulaVersion === 2 && c.flutedLayers[i] ? '' : 'none';
    });
    Object.entries(out).forEach(([key,value]) => {
        if (typeof value !== 'number') return;
        const e = document.getElementById(key) || document.getElementById('client_'+key);
        if (e) e.value = value.toFixed(key.endsWith('Kg') || key.endsWith('PerBox') ? 6 : key.includes('In') || key.endsWith('Mm') || key === 'area' ? 6 : 2);
    });
    setVal('totalSheetSqIn',out.area.toFixed(6)); setVal('otherMaterialsCost',m.otherMaterialsCost.toFixed(2));
    document.getElementById('bindingCost').readOnly = m.bindingType === 'None' || m.bindingType === 'Pins';
    document.getElementById('finalCostDisplay').textContent = ClientCartonCosting.round(out.finalCostWOGST).toFixed(2);
    document.getElementById('finalRateDisplay').textContent = ClientCartonCosting.round(out.finalRateWithGST).toFixed(2);
}
