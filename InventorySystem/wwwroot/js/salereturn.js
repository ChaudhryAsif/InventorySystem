// ── Sale Return column map ──────────────────────────────────────────────────
//  cells[0]  #
//  cells[1]  Item ID + 🔍 lookup
//  cells[2]  Item Name (readonly)
//  cells[3]  Quantity          data-field="qty"
//  cells[4]  Sale Price        data-field="price"
//  cells[5]  Disc %            data-field="disc"
//  cells[6]  Disc Amt (readonly)
//  cells[7]  Total    (readonly)
//  cells[8]  Remove button
// ───────────────────────────────────────────────────────────────────────────

let rowCounter      = 0;
let selectedCustomer = null;
let allCustomers    = [];
let selectedItem    = null;
let allItems        = [];
let currentRow      = null;
let editId          = null;   // set when editing an existing return (?id=)

window.addEventListener('DOMContentLoaded', function () {
    const id = new URLSearchParams(window.location.search).get('id');
    if (id) {
        editId = parseInt(id);
        loadReturnForEdit(editId);
    } else {
        addNewRow();
        loadNextReturnNumber();
    }
});

// ── Load an existing return into the form (edit mode) ────────────────────────
async function loadReturnForEdit(id) {
    try {
        const res = await fetch('/SaleReturn/GetSaleReturnById?id=' + id);
        if (!res.ok) { await alertDialog('Return not found.'); window.location.href = '/SaleReturn/List'; return; }
        const r = await res.json();

        const header = document.querySelector('.page-header h2');
        if (header) header.textContent = '✏️ Edit Sale Return #' + r.saleReturnId;

        document.getElementById('returnNo').value = r.saleReturnId;
        if (r.returnDate) document.getElementById('returnDate').value = r.returnDate;
        document.getElementById('originalSaleId').value = r.originalSaleId || '';
        document.getElementById('paymentMode').value = (r.paymentMode ?? 0).toString();

        document.getElementById('hftxtCustomerId').value = r.customerID || '';
        document.getElementById('customerAccount').value = r.customerName || '';
        document.getElementById('customerDetails').value = '';
        document.getElementById('remarks').value = r.remarks || '';

        const total = parseFloat(r.totalAmount) || 0;
        const discPct = total > 0 ? (((total - (parseFloat(r.netAmount) || 0)) / total) * 100) : 0;
        document.getElementById('discPercent').value = discPct.toFixed(2);

        document.getElementById('itemsBody').innerHTML = '';
        rowCounter = 0;
        (r.items || []).forEach(it => {
            addNewRow();
            const row = document.querySelector('#itemsBody tr:last-child');
            row.cells[1].querySelector('input').value = it.itemId;
            row.cells[2].querySelector('input').value = it.descr || '';
            row.cells[3].querySelector('input').value = it.quantity;
            row.cells[4].querySelector('input').value = parseFloat(it.salePrice || 0).toFixed(2);
            row.cells[5].querySelector('input').value = it.discPer || 0;
            calculateRowTotal(row);
        });
        if ((r.items || []).length === 0) addNewRow();

        calculateTotals();
    } catch (e) {
        console.error(e);
        await alertDialog('Error loading return for edit.');
        window.location.href = '/SaleReturn/List';
    }
}

// ── Row management ──────────────────────────────────────────────────────────
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
        <td><input type="number" class="table-input" value="0" min="0" step="0.01" data-field="qty"></td>
        <td><input type="number" class="table-input" value="0" step="0.01" data-field="price"></td>
        <td><input type="number" class="table-input highlight" value="0" min="0" max="100" data-field="disc"></td>
        <td><input type="number" class="table-input" value="0" readonly></td>
        <td><input type="number" class="table-input" value="0" readonly></td>
        <td><button type="button" class="btn-remove">×</button></td>
    `;

    tbody.appendChild(newRow);

    newRow.querySelectorAll('input[data-field]').forEach(input => {
        input.addEventListener('input', () => calculateRowTotal(newRow));
    });

    newRow.querySelector('.btn-remove').addEventListener('click', () => removeRow(newRow));
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
    document.querySelectorAll('#itemsBody tr').forEach((row, i) => {
        row.cells[0].textContent = i + 1;
    });
    rowCounter = document.querySelectorAll('#itemsBody tr').length;
}

// ── Calculations ────────────────────────────────────────────────────────────
function calculateRowTotal(row) {
    const qty      = parseFloat(row.cells[3].querySelector('input').value) || 0;
    const price    = parseFloat(row.cells[4].querySelector('input').value) || 0;
    const discPct  = parseFloat(row.cells[5].querySelector('input').value) || 0;
    const subtotal = qty * price;
    const discAmt  = (subtotal * discPct) / 100;

    row.cells[6].querySelector('input').value = discAmt.toFixed(2);
    row.cells[7].querySelector('input').value = (subtotal - discAmt).toFixed(2);

    calculateTotals();
}

function calculateTotals() {
    let totalQty = 0, invoiceTotal = 0;

    document.querySelectorAll('#itemsBody tr').forEach(row => {
        totalQty     += parseFloat(row.cells[3].querySelector('input').value) || 0;
        invoiceTotal += parseFloat(row.cells[7].querySelector('input').value) || 0;
    });

    const discPct = parseFloat(document.getElementById('discPercent').value) || 0;
    const discAmt = (invoiceTotal * discPct) / 100;
    const netAmt  = invoiceTotal - discAmt;

    document.getElementById('totalQty').value        = totalQty.toFixed(0);
    document.getElementById('invoiceTotal').value    = invoiceTotal.toFixed(2);
    document.getElementById('discAmount').value      = discAmt.toFixed(2);
    document.getElementById('netAmount').textContent = netAmt.toFixed(2);
}

document.getElementById('discPercent').addEventListener('input', calculateTotals);

// ── Return number ───────────────────────────────────────────────────────────
function loadNextReturnNumber() {
    fetch('/SaleReturn/GetNextReturnNumber')
        .then(res => res.json())
        .then(data => { document.getElementById('returnNo').value = data.returnNo; })
        .catch(err => console.error('Error fetching return number:', err));
}

// ── Reset form ──────────────────────────────────────────────────────────────
function resetForm() {
    editId = null;
    const header = document.querySelector('.page-header h2');
    if (header) header.textContent = '↩️ Sale Return';
    document.getElementById('returnDate').value      = new Date().toISOString().split('T')[0];
    document.getElementById('originalSaleId').value  = '';
    document.getElementById('customerAccount').value = '';
    document.getElementById('hftxtCustomerId').value = '';
    document.getElementById('customerDetails').value = '';
    document.getElementById('remarks').value         = '';
    document.getElementById('paymentMode').selectedIndex = 0;
    document.getElementById('discPercent').value     = '0';
    selectedCustomer = null;
    document.getElementById('itemsBody').innerHTML   = '';
    rowCounter = 0;
    addNewRow();
    calculateTotals();
    loadNextReturnNumber();
}

// ── New return ──────────────────────────────────────────────────────────────
document.getElementById('newBtn').addEventListener('click', async function () {
    if (await confirmDialog('Create new return? Unsaved changes will be lost.')) resetForm();
});

// ── Form submit ─────────────────────────────────────────────────────────────
document.getElementById('saleReturnForm').addEventListener('submit', async function (e) {
    e.preventDefault();

    if (!document.getElementById('hftxtCustomerId').value) {
        await alertDialog('Please select a customer!');
        return;
    }

    const rows = document.querySelectorAll('#itemsBody tr');
    const items = [];

    rows.forEach(row => {
        const itemId = row.cells[1].querySelector('input').value;
        const qty    = parseFloat(row.cells[3].querySelector('input').value) || 0;
        if (!itemId || qty <= 0) return;

        items.push({
            ItemId:    parseInt(itemId),
            Desc:      row.cells[2].querySelector('input').value,
            Quantity:  qty,
            SalePrice: parseFloat(row.cells[4].querySelector('input').value) || 0,
            DiscPer:   parseFloat(row.cells[5].querySelector('input').value) || 0,
            DiscAmt:   parseFloat(row.cells[6].querySelector('input').value) || 0,
            Total:     parseFloat(row.cells[7].querySelector('input').value) || 0
        });
    });

    if (items.length === 0) {
        await alertDialog('Please add at least one item with quantity!');
        return;
    }

    const originalId = parseInt(document.getElementById('originalSaleId').value) || null;

    const payload = {
        SaleReturnId:   editId,
        ReturnDate:     document.getElementById('returnDate').value,
        CustomerID:     document.getElementById('hftxtCustomerId').value,
        OriginalSaleId: originalId,
        BranchID:       1,
        PaymentMode:    parseInt(document.getElementById('paymentMode').value),
        Remarks:        document.getElementById('remarks').value || null,
        TotalAmount:    parseFloat(document.getElementById('invoiceTotal').value) || 0,
        NetAmount:      parseFloat(document.getElementById('netAmount').textContent) || 0,
        Items:          items
    };

    const url = editId ? '/SaleReturn/Update' : '/SaleReturn/Save';
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
                    window.location.href = '/SaleReturn/List';
                } else {
                    resetForm();
                }
            } else {
                await alertDialog('❌ ' + (data.message || 'Unknown error'));
            }
        })
        .catch(async err => {
            console.error(err);
            await alertDialog('Error saving sale return.');
        });
});

// ── Customer modal ──────────────────────────────────────────────────────────
document.getElementById('btnCustomerAccount').addEventListener('click', e => {
    e.preventDefault();
    openCustomerModal();
});

function openCustomerModal() {
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
    tbody.innerHTML = '<tr><td colspan="7" style="text-align:center;padding:2rem;">Loading...</td></tr>';

    fetch('/Parties/GetCustomers')
        .then(res => res.json())
        .then(data => {
            if (data.success && data.customers?.length > 0) {
                allCustomers = data.customers;
                displayCustomers(allCustomers);
            } else {
                showCustomerEmptyState();
            }
        })
        .catch(() => {
            document.getElementById('customerTableBody').innerHTML =
                '<tr><td colspan="7" style="text-align:center;padding:2rem;color:#dc3545;">Error loading customers.</td></tr>';
        });
}

function displayCustomers(customers) {
    const tbody = document.getElementById('customerTableBody');
    document.getElementById('emptyState').style.display = 'none';
    tbody.innerHTML = '';

    customers.forEach(c => {
        const row = document.createElement('tr');
        row.innerHTML = `
            <td>${c.partyId}</td>
            <td>${c.partyName}</td>
            <td>${c.partyType}</td>
            <td>${c.contactPerson || '-'}</td>
            <td>${c.phone}</td>
            <td>${c.email || '-'}</td>
            <td>${parseFloat(c.openingBalance || 0).toFixed(2)}</td>
        `;
        row.addEventListener('click', () => selectCustomerRow(row, c));
        tbody.appendChild(row);
    });
}

function showCustomerEmptyState() {
    document.getElementById('customerTableBody').innerHTML = '';
    document.getElementById('emptyState').style.display = 'block';
}

function selectCustomerRow(row, customer) {
    document.querySelectorAll('#customerTableBody tr').forEach(r => r.classList.remove('selected'));
    row.classList.add('selected');
    selectedCustomer = customer;
    document.getElementById('selectCustomerBtn').disabled = false;
}

function selectCustomer() {
    if (!selectedCustomer) return;
    document.getElementById('hftxtCustomerId').value  = selectedCustomer.partyId;
    document.getElementById('customerAccount').value  = selectedCustomer.partyName;
    document.getElementById('customerDetails').value  =
        [selectedCustomer.contactPerson ? `Contact: ${selectedCustomer.contactPerson}` : '',
         selectedCustomer.phone         ? `Phone: ${selectedCustomer.phone}`           : '',
         selectedCustomer.email         ? `Email: ${selectedCustomer.email}`           : '']
        .filter(Boolean).join('\n');
    closeCustomerModal();
}

function searchCustomers() {
    const q = document.getElementById('customerSearchInput').value.toLowerCase();
    displayCustomers(q
        ? allCustomers.filter(c =>
            c.partyName.toLowerCase().includes(q) ||
            (c.phone && c.phone.includes(q)) ||
            (c.email && c.email.toLowerCase().includes(q)))
        : allCustomers);
}

document.getElementById('customerModal').addEventListener('click', function (e) {
    if (e.target === this) closeCustomerModal();
});

// ── Item modal ──────────────────────────────────────────────────────────────
function openItemLookup(button) {
    currentRow = button.closest('tr');
    document.getElementById('itemModal').classList.add('show');
    loadItems();
}

function closeItemModal() {
    document.getElementById('itemModal').classList.remove('show');
    selectedItem = null;
    currentRow   = null;
    document.getElementById('selectItemBtn').disabled = true;
    document.querySelectorAll('#itemTableBody tr').forEach(r => r.classList.remove('selected'));
}

function loadItems() {
    const tbody = document.getElementById('itemTableBody');
    tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:2rem;">Loading...</td></tr>';

    fetch('/Item/GetItems')
        .then(res => res.json())
        .then(data => {
            if (data.success && data.items?.length > 0) {
                allItems = data.items;
                displayItems(allItems);
                loadCategories(data.items);
            } else {
                document.getElementById('itemEmptyState').style.display = 'block';
                tbody.innerHTML = '';
            }
        })
        .catch(() => {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:2rem;color:#dc3545;">Error loading items.</td></tr>';
        });
}

function loadCategories(items) {
    const sel  = document.getElementById('categoryFilter');
    const cats = [...new Set(items.map(i => i.categoryName))];
    sel.innerHTML = '<option value="">All Categories</option>';
    cats.forEach(c => {
        const opt = document.createElement('option');
        opt.value = c; opt.textContent = c;
        sel.appendChild(opt);
    });
}

function displayItems(items) {
    const tbody = document.getElementById('itemTableBody');
    document.getElementById('itemEmptyState').style.display = 'none';
    tbody.innerHTML = '';

    items.forEach(item => {
        const row = document.createElement('tr');
        row.innerHTML = `
            <td>${item.itemId}</td>
            <td>${item.itemName}</td>
            <td>${item.categoryName || '-'}</td>
            <td>${parseFloat(item.salePrice || 0).toFixed(2)}</td>
            <td>${parseFloat(item.stock || 0).toFixed(0)}</td>
        `;
        row.addEventListener('click', () => selectItemRow(row, item));
        tbody.appendChild(row);
    });
}

function selectItemRow(row, item) {
    document.querySelectorAll('#itemTableBody tr').forEach(r => r.classList.remove('selected'));
    row.classList.add('selected');
    selectedItem = item;
    document.getElementById('selectItemBtn').disabled = false;
}

function selectItem() {
    if (!selectedItem || !currentRow) return;
    currentRow.cells[1].querySelector('input').value = selectedItem.itemId;
    currentRow.cells[2].querySelector('input').value = selectedItem.itemName;
    currentRow.cells[4].querySelector('input').value = parseFloat(selectedItem.salePrice || 0).toFixed(2);
    calculateRowTotal(currentRow);
    closeItemModal();
}

function searchItems() {
    const q   = document.getElementById('itemSearchInput').value.toLowerCase();
    const cat = document.getElementById('categoryFilter').value;
    displayItems(allItems.filter(i =>
        (!q || i.itemName.toLowerCase().includes(q)) &&
        (!cat || i.categoryName === cat)));
}

function filterByCategory() { searchItems(); }

document.getElementById('itemModal').addEventListener('click', function (e) {
    if (e.target === this) closeItemModal();
});

document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    if (document.getElementById('customerModal').classList.contains('show')) closeCustomerModal();
    if (document.getElementById('itemModal').classList.contains('show'))     closeItemModal();
});