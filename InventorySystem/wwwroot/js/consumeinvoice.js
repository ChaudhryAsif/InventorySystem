// ── Consume Voucher – column map ────────────────────────────────────────────
//  cells[0]  #  (row number)
//  cells[1]  Item ID input  +  🔍 lookup button
//  cells[2]  Item Name  (readonly)
//  cells[3]  Avail. Stock  (readonly)
//  cells[4]  Qty Consumed   data-field="qty"
//  cells[5]  Remove button
// ───────────────────────────────────────────────────────────────────────────

// Self-contained placeholder — via.placeholder.com is no longer online, so items
// without a real photo must fall back to something that doesn't depend on the network.
const NO_IMAGE_PLACEHOLDER = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='100'%3E%3Crect width='100' height='100' fill='%23e2e8f0'/%3E%3Ccircle cx='38' cy='36' r='8' fill='%23ffffff'/%3E%3Cpath d='M20 78 L42 50 L58 68 L72 48 L90 78 Z' fill='%23ffffff'/%3E%3C/svg%3E";

let rowCounter   = 0;
let selectedItem = null;
let allItems     = [];
let currentRow   = null;
let editId       = null;   // set when editing an existing voucher (?id=)

// ── Init ────────────────────────────────────────────────────────────────────
window.addEventListener('DOMContentLoaded', function () {
    const id = new URLSearchParams(window.location.search).get('id');
    if (id) {
        editId = parseInt(id);
        loadVoucherForEdit(editId);
    } else {
        addNewRow();
        loadNextVoucherNumber();
    }
});

// ── Load an existing voucher into the form (edit mode) ───────────────────────
async function loadVoucherForEdit(id) {
    try {
        const res = await fetch('/Consume/GetConsumeById?id=' + id);
        if (!res.ok) { await alertDialog('Voucher not found.'); window.location.href = '/Consume/List'; return; }
        const inv = await res.json();

        const header = document.querySelector('.page-header h2');
        if (header) header.textContent = '✏️ Edit Consumption Voucher #' + inv.consumeId;

        document.getElementById('voucherNo').value = inv.consumeId;
        document.getElementById('refNo').value      = inv.refNo || '';
        if (inv.consumeDate) document.getElementById('voucherDate').value = inv.consumeDate;
        document.getElementById('purpose').value  = inv.purpose || '';
        document.getElementById('remarks').value  = inv.remarks || '';

        document.getElementById('itemsBody').innerHTML = '';
        rowCounter = 0;
        (inv.items || []).forEach(it => {
            addNewRow();
            const row = document.querySelector('#itemsBody tr:last-child');
            const avail = parseFloat(it.availStock) || 0;
            row.cells[1].querySelector('input').value = it.itemId;
            row.cells[2].querySelector('input').value = it.descr || '';
            row.cells[3].querySelector('input').value = avail;
            const qtyInput = row.cells[4].querySelector('input');
            qtyInput.value = it.quantity;
            qtyInput.max   = avail;
        });
        if ((inv.items || []).length === 0) addNewRow();

        calculateTotals();
    } catch (e) {
        console.error(e);
        await alertDialog('Error loading voucher for edit.');
        window.location.href = '/Consume/List';
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// ROW MANAGEMENT
// ══════════════════════════════════════════════════════════════════════════════

document.getElementById('addRowBtn').addEventListener('click', addNewRow);

function addNewRow() {
    rowCounter++;
    const tbody  = document.getElementById('itemsBody');
    const newRow = document.createElement('tr');

    newRow.innerHTML = `
        <td>${rowCounter}</td>
        <td>
            <div style="display:flex;align-items:center;gap:0.3rem;">
                <input type="text" class="table-input" placeholder="ID" readonly style="flex:1;">
                <button type="button" class="item-lookup-icon" onclick="openItemLookup(this)">🔍</button>
            </div>
        </td>
        <td><input type="text" class="table-input" placeholder="Item name" readonly></td>
        <td><input type="text" class="table-input stock-avail" value="0" readonly
                   style="background:#e8f5e9;color:#2e7d32;font-weight:600;text-align:center;"></td>
        <td><input type="number" class="table-input" value="0" min="0" step="0.01"
                   data-field="qty" style="text-align:right;"></td>
        <td><button type="button" class="btn-remove">×</button></td>
    `;

    tbody.appendChild(newRow);

    // Live qty validation on each keystroke
    newRow.querySelector('input[data-field="qty"]').addEventListener('input', function () {
        validateQty(newRow);
        calculateTotals();
    });

    newRow.querySelector('.btn-remove').addEventListener('click', function () {
        removeRow(newRow);
    });
}

async function removeRow(row) {
    if (document.getElementById('itemsBody').rows.length > 1) {
        if (await confirmDialog('Remove this item?', { danger: true })) {
            row.remove();
            updateRowNumbers();
            calculateTotals();
        }
    } else {
        await alertDialog('At least one row is required!');
    }
}

function updateRowNumbers() {
    document.querySelectorAll('#itemsBody tr').forEach((row, index) => {
        row.cells[0].textContent = index + 1;
    });
    rowCounter = document.getElementById('itemsBody').rows.length;
}

// ══════════════════════════════════════════════════════════════════════════════
// VALIDATION & TOTALS
// ══════════════════════════════════════════════════════════════════════════════

function validateQty(row) {
    const qtyInput = row.cells[4].querySelector('input');
    const avail    = parseFloat(row.cells[3].querySelector('input').value) || 0;
    let   qty      = parseFloat(qtyInput.value) || 0;

    if (avail > 0 && qty > avail) {
        qtyInput.value         = avail;
        qtyInput.style.outline = '2px solid #dc3545';
        qtyInput.title         = `⚠️ Max available stock: ${avail}`;
    } else {
        qtyInput.style.outline = '';
        qtyInput.title         = avail > 0 ? `Max available: ${avail}` : '';
    }
}

function calculateTotals() {
    const rows = document.querySelectorAll('#itemsBody tr');
    let totalItems = 0;
    let totalQty   = 0;

    rows.forEach(row => {
        const itemId = row.cells[1].querySelector('input').value;
        const qty    = parseFloat(row.cells[4].querySelector('input').value) || 0;
        if (itemId && qty > 0) totalItems++;
        totalQty += qty;
    });

    document.getElementById('totalItems').value = totalItems;
    document.getElementById('totalQty').value   = totalQty.toFixed(2);
}

// ══════════════════════════════════════════════════════════════════════════════
// VOUCHER NUMBER
// ══════════════════════════════════════════════════════════════════════════════

function loadNextVoucherNumber() {
    fetch('/Consume/GetNextVoucherNumber')
        .then(res => res.json())
        .then(data => { document.getElementById('voucherNo').value = data.voucherNo; })
        .catch(err => console.error('Error fetching voucher number:', err));
}

// ══════════════════════════════════════════════════════════════════════════════
// FORM RESET / NEW
// ══════════════════════════════════════════════════════════════════════════════

function resetForm() {
    editId = null;
    const header = document.querySelector('.page-header h2');
    if (header) header.textContent = '🏭 Raw Material Consumption';
    document.getElementById('refNo').value       = '';
    document.getElementById('voucherDate').value = new Date().toISOString().split('T')[0];
    document.getElementById('purpose').value     = '';
    document.getElementById('remarks').value     = '';

    document.getElementById('itemsBody').innerHTML = '';
    rowCounter = 0;
    addNewRow();
    calculateTotals();
    loadNextVoucherNumber();
}

document.getElementById('newBtn').addEventListener('click', async function () {
    if (await confirmDialog('Create new voucher? Unsaved changes will be lost.')) resetForm();
});

// ══════════════════════════════════════════════════════════════════════════════
// FORM SUBMIT
// ══════════════════════════════════════════════════════════════════════════════

document.getElementById('consumeForm').addEventListener('submit', async function (e) {
    e.preventDefault();

    const rows = document.querySelectorAll('#itemsBody tr');
    let hasValidItem  = false;
    const stockErrors = [];

    rows.forEach(row => {
        const itemName = row.cells[2].querySelector('input').value;
        const avail    = parseFloat(row.cells[3].querySelector('input').value) || 0;
        const qty      = parseFloat(row.cells[4].querySelector('input').value) || 0;

        if (itemName && qty > 0) {
            hasValidItem = true;
            if (qty > avail) {
                stockErrors.push(`"${itemName}": Requested ${qty}, Available ${avail}`);
            }
        }
    });

    if (!hasValidItem) {
        await alertDialog('Please add at least one raw material with quantity!');
        return;
    }
    if (stockErrors.length > 0) {
        await alertDialog('⚠️ Insufficient stock:\n\n' + stockErrors.join('\n'));
        return;
    }

    // ── Build items payload ───────────────────────────────────────────────
    const items = [];
    rows.forEach(row => {
        const itemId = row.cells[1].querySelector('input').value;
        const qty    = parseFloat(row.cells[4].querySelector('input').value) || 0;
        if (!itemId || qty <= 0) return;

        items.push({
            ItemId:   parseInt(itemId),
            Desc:     row.cells[2].querySelector('input').value,
            Quantity: qty
        });
    });

    const payload = {
        ConsumeId:   editId,
        ConsumeDate: document.getElementById('voucherDate').value,
        BranchID:    1,
        RefNo:       document.getElementById('refNo').value   || null,
        Purpose:     document.getElementById('purpose').value || null,
        Remarks:     document.getElementById('remarks').value || null,
        Items:       items
    };

    // ── Disable save button to prevent double-submit ──────────────────────
    const saveBtn = document.querySelector('#consumeForm button[type="submit"]');
    saveBtn.disabled    = true;
    saveBtn.textContent = '⏳ Saving...';

    const url = editId ? '/Consume/Update' : '/Consume/Save';
    fetch(url, {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify(payload)
    })
        .then(res => res.json())
        .then(async data => {
            if (data.success) {
                await alertDialog('✅ ' + data.message);
                if (editId) {
                    window.location.href = '/Consume/List';
                } else {
                    resetForm();
                }
            } else {
                await alertDialog('❌ ' + (data.message || 'Unknown error'));
            }
        })
        .catch(async err => {
            console.error(err);
            await alertDialog('❌ Error saving consumption voucher. Please try again.');
        })
        .finally(() => {
            saveBtn.disabled    = false;
            saveBtn.textContent = '💾 Save';
        });
});

// ══════════════════════════════════════════════════════════════════════════════
// ITEM MODAL
// ══════════════════════════════════════════════════════════════════════════════

function openItemLookup(btn) {
    currentRow   = btn.closest('tr');
    selectedItem = null;
    document.getElementById('selectItemBtn').disabled = true;
    document.getElementById('itemModal').classList.add('show');
    loadItems();
}

function closeItemModal() {
    document.getElementById('itemModal').classList.remove('show');
    selectedItem = null;
    currentRow   = null;
    document.getElementById('selectItemBtn').disabled = true;
    document.querySelectorAll('#itemTableBody tr').forEach(r => r.classList.remove('selected'));
    // Clear search/filter inputs
    document.getElementById('itemSearchInput').value = '';
    document.getElementById('categoryFilter').value  = '';
    document.getElementById('stockFilter').value     = '';
}

function loadItems() {
    const tbody = document.getElementById('itemTableBody');
    tbody.innerHTML = '<tr><td colspan="6" style="text-align:center;padding:2rem;">Loading items...</td></tr>';

    // Reuse same endpoint as purchase & sale — returns itemID, itemName,
    // categoryName, currentStock, salePrice, imageUrl
    fetch('/Item/GetItems')
        .then(res => res.json())
        .then(data => {
            if (data.success && data.items && data.items.length > 0) {
                allItems = data.items;
                loadCategories(allItems);
                displayItems(allItems);
            } else {
                showItemEmptyState();
            }
        })
        .catch(err => {
            console.error('Error loading items:', err);
            tbody.innerHTML = '<tr><td colspan="6" style="text-align:center;padding:2rem;color:#dc3545;">Error loading items. Please try again.</td></tr>';
        });
}

function loadCategories(items) {
    const sel = document.getElementById('categoryFilter');
    const categories = [...new Set(items.map(i => i.categoryName).filter(Boolean))].sort();

    sel.innerHTML = '<option value="">All Categories</option>';
    categories.forEach(cat => {
        const opt = document.createElement('option');
        opt.value = opt.textContent = cat;
        sel.appendChild(opt);
    });
}

function displayItems(items) {
    const tbody      = document.getElementById('itemTableBody');
    const emptyState = document.getElementById('itemEmptyState');

    if (!items.length) { showItemEmptyState(); return; }

    emptyState.style.display = 'none';
    tbody.innerHTML = '';

    items.forEach(item => {
        const stock = item.currentStock || 0;
        let stockBadge;
        if      (stock > 50) stockBadge = `<span class="stock-badge stock-badge-high">${stock}</span>`;
        else if (stock > 0)  stockBadge = `<span class="stock-badge stock-badge-low">${stock}</span>`;
        else                 stockBadge = `<span class="stock-badge stock-badge-out">${stock}</span>`;

        const row = document.createElement('tr');
        row.innerHTML = `
            <td>${item.itemID}</td>
            <td><img src="${item.imageUrl || NO_IMAGE_PLACEHOLDER}"
                     class="item-image-thumb" alt="Item"
                     onerror="this.onerror=null;this.src=NO_IMAGE_PLACEHOLDER"></td>
            <td>${item.itemName}</td>
            <td>${item.categoryName}</td>
            <td>${item.barcode || '-'}</td>
            <td>${stockBadge}</td>
        `;
        row.addEventListener('click', function () { selectItemRow(this, item); });
        tbody.appendChild(row);
    });
}

function showItemEmptyState() {
    document.getElementById('itemTableBody').innerHTML = '';
    document.getElementById('itemEmptyState').style.display = 'block';
}

function selectItemRow(row, item) {
    document.querySelectorAll('#itemTableBody tr').forEach(r => r.classList.remove('selected'));
    row.classList.add('selected');
    selectedItem = item;
    document.getElementById('selectItemBtn').disabled = false;
}

async function selectItem() {
    if (!selectedItem || !currentRow) return;

    // ── Prevent duplicate items across rows ───────────────────────────────
    const allRows = document.querySelectorAll('#itemsBody tr');
    for (const row of allRows) {
        if (row === currentRow) continue;
        const existingId = row.cells[1].querySelector('input').value;
        if (existingId && existingId == selectedItem.itemID) {
            await alertDialog(`⚠️ "${selectedItem.itemName}" is already added in row ${row.cells[0].textContent}.\nPlease update the quantity in that row instead.`);
            closeItemModal();
            return;
        }
    }

    // ── Populate row ──────────────────────────────────────────────────────
    const avail    = selectedItem.currentStock ?? 0;
    const qtyInput = currentRow.cells[4].querySelector('input');

    currentRow.cells[1].querySelector('input').value = selectedItem.itemID;
    currentRow.cells[2].querySelector('input').value = selectedItem.itemName;
    currentRow.cells[3].querySelector('input').value = avail;

    // Set hard max limit so browser also blocks over-entry
    qtyInput.max   = avail;
    qtyInput.value = 0;
    qtyInput.title = `Max available: ${avail}`;

    // Highlight if zero stock
    if (avail <= 0) {
        currentRow.cells[3].querySelector('input').style.background = '#ffeaea';
        currentRow.cells[3].querySelector('input').style.color      = '#dc3545';
        await alertDialog(`⚠️ "${selectedItem.itemName}" has zero stock. Cannot consume.`);
    } else {
        currentRow.cells[3].querySelector('input').style.background = '#e8f5e9';
        currentRow.cells[3].querySelector('input').style.color      = '#2e7d32';
    }

    calculateTotals();
    closeItemModal();

    // Auto-focus the qty input for fast entry
    setTimeout(() => qtyInput.focus(), 100);
}

function filterItems() {
    const search   = document.getElementById('itemSearchInput').value.toLowerCase();
    const category = document.getElementById('categoryFilter').value;
    const stockF   = document.getElementById('stockFilter').value;

    const filtered = allItems.filter(item => {
        const matchText = !search ||
            item.itemName?.toLowerCase().includes(search) ||
            (item.barcode      && item.barcode.toLowerCase().includes(search)) ||
            (item.categoryName && item.categoryName.toLowerCase().includes(search));
        const matchCat  = !category || item.categoryName === category;
        const matchStk  = !stockF   ||
            (stockF === 'instock'    && item.currentStock > 50) ||
            (stockF === 'lowstock'   && item.currentStock > 0 && item.currentStock <= 50) ||
            (stockF === 'outofstock' && (item.currentStock ?? 0) <= 0);
        return matchText && matchCat && matchStk;
    });

    displayItems(filtered);
}

// ── Close modal on backdrop click ────────────────────────────────────────────
document.getElementById('itemModal').addEventListener('click', function (e) {
    if (e.target === this) closeItemModal();
});

// ── Close modal on Escape key ────────────────────────────────────────────────
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && document.getElementById('itemModal').classList.contains('show')) {
        closeItemModal();
    }
});