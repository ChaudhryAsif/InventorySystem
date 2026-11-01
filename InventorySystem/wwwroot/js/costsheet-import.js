/* Excel fills the existing form. Saving always remains a separate user action. */
let excelImportActive = false;
function clearCostSheetExcel() {
    excelImportActive = false;
    document.getElementById('costSheetExcelFile').value = '';
    document.getElementById('excelImportStatus').textContent = '';
    document.getElementById('excelImportNotes').textContent = '';
    document.getElementById('excelImportNotes').hidden = true;
}
async function importCostSheetExcel() {
    const status = document.getElementById('excelImportStatus');
    const file = document.getElementById('costSheetExcelFile').files[0];
    if (!settingsLoaded) { status.textContent = 'Wait for costing settings to load, then try again.'; return; }
    if (!file || !/\.xlsx$/i.test(file.name) || file.size === 0 || file.size > 5 * 1024 * 1024) {
        status.textContent = 'Choose an Excel .xlsx file up to 5 MB.'; return;
    }
    const button = document.getElementById('importExcelBtn'); button.disabled = true;
    status.textContent = 'Reading Excel cost sheet…';
    try {
        const body = new FormData(); body.append('file', file); body.append('costSheetId', getVal('editCostSheetId') || '0');
        const response = await fetch('/CostSheet/ImportExcel', { method: 'POST', body });
        const json = await response.json().catch(() => null);
        if (!response.ok || !json?.success) throw new Error(json?.message || 'The upload could not be read. Refresh and try again with a .xlsx file up to 5 MB.');
        applyCostSheetExcel(json.preview);
    } catch (error) { status.textContent = error.message; }
    finally { button.disabled = false; }
}

function applyCostSheetExcel(imported) {
    const d = imported.data;
    // Keep the current sheet identity and branch, but clear old stock links and extras.
    document.getElementById('plyBody').replaceChildren(); plyCounter = 0;
    document.getElementById('matBody').replaceChildren();
    ['finishedItemId','glueItemId','silicateItemId'].forEach(id => setSelect(id, ''));
    legacyAdhesiveReview = false;
    const fields = {
        sheetDate: d.sheetDate?.split('T')[0] || '', itemName: d.itemName, dimL: d.length, dimW: d.width, dimH: d.height,
        dimFlap: d.flap, autoFlapGap: d.autoFlapGap, printingColors: d.printingColors,
        glueRate: d.glueRate, silicateRate: d.silicateRate, labourRate: d.labourRate, energyRate: d.energyRate,
        bindingPins: d.bindingPins, freightRate: d.freightRate || 0, dickelCost: 0, wastePct: d.wastePct, adminExpPct: d.adminExpPct,
        sellingDistPct: d.sellingDistPct, repairMaintPct: d.repairMaintPct, storeSparesPct: d.storeSparesPct, mfgCostPct: d.mfgCostPct,
        commPersonA: d.commPersonA, commPersonAPct: d.commPersonAPct, commPersonB: d.commPersonB, commPersonBPct: d.commPersonBPct,
        whTaxPct: d.whTaxPct, gstPct: d.gstPercentage, profitPct: d.profitPct, moq: d.moq, taxPct: d.taxPct, remarks: d.remarks
    };
    Object.entries(fields).forEach(([id, value]) => setVal(id, value ?? ''));
    Object.entries({costingMethod:'Client', status:'Draft', boxStyle:d.boxStyle, sizeType:d.sizeType, sizeUnit:d.sizeUnit,
        printingType:d.printingType, hasLamination:d.hasLamination ? 'true' : 'false', bindingType:d.bindingType,
        finishedItemId:d.finishedItemId || ''}).forEach(([id,value]) => setSelect(id,value));
    restoreClientOptions(d.clientCosting); applyCostingMethod(false);
    d.plies.forEach((p,i) => {
        addPlyRow(p.plyName || '',p.specification,p.gsm,p.rate,'',p.itemId || '',d.clientCosting.flutedLayers[i],d.clientCosting.layerFluteFactors?.[i] ?? d.clientCosting.fluteFactor);
        const row = document.getElementById('plyBody').lastElementChild;
        row.dataset.excelRate = 'true';
        row.cells[5].querySelector('input').title = 'Imported Excel rate per KG — remains editable and is kept when selecting an inventory item';
    });
    recalcAll();
    excelImportActive = true;
    updateExcelImportGuidance();
    document.getElementById('excelImportStatus').textContent = 'Data imported. Review before saving.';
    document.getElementById('itemName').focus();
}

function updateExcelImportGuidance() {
    if (!excelImportActive) return;
    const missing = [];
    if (!getVal('finishedItemId')) missing.push('finished box');
    if ([...document.querySelectorAll('#plyBody tr')].some(row => !row.cells[1].querySelector('input').value)) missing.push('paper layers');
    if (Number(getVal('glueKgPerBox')) > 0 && !getVal('glueItemId')) missing.push('glue');
    if (Number(getVal('silicateKgPerBox')) > 0 && !getVal('silicateItemId')) missing.push('silicate');
    if (getVal('hasLamination') === 'true') {
        if (Number(getVal('client_glueLaminationApplications')) > 0 && !getVal('client_glueLaminationItemId')) missing.push('lamination glue');
        if (Number(getVal('client_laminationChemicalApplications')) > 0 && !getVal('client_laminationChemicalItemId')) missing.push('lamination chemicals');
    }
    const notes = document.getElementById('excelImportNotes');
    notes.textContent = missing.length ? 'Inventory links for production: ' + missing.join(', ') + '.' : '';
    notes.hidden = missing.length === 0;
}
document.addEventListener('change', updateExcelImportGuidance);
