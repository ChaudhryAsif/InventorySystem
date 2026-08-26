// ── Purchase Invoice column map ─────────────────────────────────────────────
//  cells[0]  #
//  cells[1]  Item ID input + 🔍 lookup button
//  cells[2]  Item Name (readonly)
//  cells[3]  Quantity                              data-field="qty"
//  cells[4]  Pur. Price                             data-field="price"
//  cells[5]  Sale Rate
//  cells[6]  Disc %                                data-field="disc"
//  cells[7]  Disc Amt (readonly, calculated)
//  cells[8]  Total    (readonly, calculated)
//  cells[9]  Remove button
// ───────────────────────────────────────────────────────────────────────────

// Self-contained placeholder — via.placeholder.com is no longer online, so items
// without a real photo must fall back to something that doesn't depend on the network.
const NO_IMAGE_PLACEHOLDER = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='100' height='100'%3E%3Crect width='100' height='100' fill='%23e2e8f0'/%3E%3Ccircle cx='38' cy='36' r='8' fill='%23ffffff'/%3E%3Cpath d='M20 78 L42 50 L58 68 L72 48 L90 78 Z' fill='%23ffffff'/%3E%3C/svg%3E";

let rowCounter = 0;
let selectedSupplier = null;
let allSuppliers = [];
let selectedItem = null;
let allItems = [];
let currentRow = null;
let editId = null;   // set when editing an existing invoice (?id=)

// ── Init ────────────────────────────────────────────────────────────────────
window.addEventListener('DOMContentLoaded', function () {
    const id = new URLSearchParams(window.location.search).get('id');
    if (id) {
        editId = parseInt(id);
        loadInvoiceForEdit(editId);
    } else {
        addNewRow();
        loadNextInvoiceNumber();
    }
});

// ── Load an existing invoice into the form (edit mode) ───────────────────────
async function loadInvoiceForEdit(id) {
    try {
        const res = await fetch('/Purchase/GetPurchaseById?id=' + id);
        if (!res.ok) { await alertDialog('Invoice not found.'); window.location.href = '/Purchase/List'; return; }
        const inv = await res.json();

        const header = document.querySelector('.page-header h2');
        if (header) header.textContent = '✏️ Edit Purchase Invoice #' + inv.purchaseId;

        document.getElementById('invoiceNo').value = inv.purchaseId;
        document.getElementById('billNo').value     = inv.billNo || '';
        if (inv.purchaseDate) document.getElementById('invoiceDate').value = inv.purchaseDate;

        document.getElementById('hftxtSupplierId').value = inv.vendorID || '';
        document.getElementById('supplierAccount').value = inv.vendorName || '';
        document.getElementById('supplierDetails').value = '';
        document.getElementById('remarks').value          = inv.remarks || '';
        document.getElementById('paymentMode').value      = (inv.paymentMode ?? 0).toString();

        document.getElementById('gstPercent').value  = inv.gstPer || 0;
        document.getElementById('freight').value     = inv.freightExp || 0;
        document.getElementById('otherExp').value    = inv.otherExp || 0;
        const total = parseFloat(inv.totalAmount) || 0;
        const discPct = total > 0 ? ((parseFloat(inv.discount) || 0) / total) * 100 : 0;
        document.getElementById('discPercent').value = discPct.toFixed(2);

        // Rebuild item rows
        document.getElementById('itemsBody').innerHTML = '';
        rowCounter = 0;
        (inv.items || []).forEach(it => {
            addNewRow();
            const row = document.querySelector('#itemsBody tr:last-child');
            row.cells[1].querySelector('input').value = it.itemid;
            row.cells[2].querySelector('input').value = it.descr || '';
            row.cells[3].querySelector('input').value = it.quantity;
            row.cells[4].querySelector('input').value = parseFloat(it.purPrice || 0).toFixed(2);
            row.cells[5].querySelector('input').value = parseFloat(it.salePrice || 0).toFixed(2);
            row.cells[6].querySelector('input').value = it.discPer || 0;
            calculateRowTotal(row);
        });
        if ((inv.items || []).length === 0) addNewRow();

        calculateTotals();
    } catch (e) {
        console.error(e);
        await alertDialog('Error loading invoice for edit.');
        window.location.href = '/Purchase/List';
    }
}

// Add new row
document.getElementById('addRowBtn').addEventListener('click', addNewRow);

function addNewRow() {
    rowCounter++;
    const tbody = document.getElementById('itemsBody');

    const newRow = document.createElement('tr');
    newRow.innerHTML = `
            <td>${rowCounter}</td>
            <td>
                <div style="display: flex; align-items: center; gap: 0.3rem;">
                    <input type="text" class="table-input" placeholder="ID" readonly style="flex: 1;">
                    <button type="button" class="item-lookup-icon" onclick="openItemLookup(this)">🔍</button>
                </div>
            </td>
            <td><input type="text" class="table-input" placeholder="Enter item name" readonly></td>
            <td><input type="number" class="table-input" value="0" step="0.01" data-field="qty"></td>
            <td><input type="number" class="table-input" value="0" step="0.01" data-field="price"></td>
            <td><input type="number" class="table-input" value="0" step="0.01"></td>
            <td><input type="number" class="table-input highlight" value="0" data-field="disc"></td>
            <td><input type="number" class="table-input" value="0" readonly></td>
            <td><input type="number" class="table-input" value="0" readonly></td>
            <td><button type="button" class="btn-remove">×</button></td>
        `;

    tbody.appendChild(newRow);

    // Add event listeners to inputs
    const inputs = newRow.querySelectorAll('input[data-field]');
    inputs.forEach(input => {
        input.addEventListener('input', function () {
            calculateRowTotal(newRow);
        });
    });

    // Add event listener to remove button
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
    const rows = document.querySelectorAll('#itemsBody tr');
    rows.forEach((row, index) => {
        row.cells[0].textContent = index + 1;
    });
    rowCounter = rows.length;
}

function calculateRowTotal(row) {
    const cells = row.cells;
    const qty = parseFloat(cells[3].querySelector('input').value) || 0;
    const price = parseFloat(cells[4].querySelector('input').value) || 0;
    const discPercent = parseFloat(cells[6].querySelector('input').value) || 0;

    const subtotal = qty * price;
    const discAmount = (subtotal * discPercent) / 100;
    const total = subtotal - discAmount;

    cells[7].querySelector('input').value = discAmount.toFixed(2);
    cells[8].querySelector('input').value = total.toFixed(2);

    calculateTotals();
}

function calculateTotals() {
    const rows = document.querySelectorAll('#itemsBody tr');
    let totalQty = 0;
    let invoiceTotal = 0;

    rows.forEach(row => {
        const qty = parseFloat(row.cells[3].querySelector('input').value) || 0;
        const total = parseFloat(row.cells[8].querySelector('input').value) || 0;
        totalQty += qty;
        invoiceTotal += total;
    });

    const gstPercent = parseFloat(document.getElementById('gstPercent').value) || 0;
    const freight = parseFloat(document.getElementById('freight').value) || 0;
    const otherExp = parseFloat(document.getElementById('otherExp').value) || 0;
    const discPercent = parseFloat(document.getElementById('discPercent').value) || 0;

    const gstAmount = (invoiceTotal * gstPercent) / 100;
    const discAmount = (invoiceTotal * discPercent) / 100;
    const netAmount = invoiceTotal + gstAmount + freight + otherExp - discAmount;

    document.getElementById('totalQty').value = totalQty.toFixed(0);
    document.getElementById('invoiceTotal').value = invoiceTotal.toFixed(2);
    document.getElementById('gstAmount').value = gstAmount.toFixed(2);
    document.getElementById('discAmount').value = discAmount.toFixed(2);
    document.getElementById('netAmount').textContent = netAmount.toFixed(2);
}

function loadNextInvoiceNumber() {
    fetch('/Purchase/GetNextInvoiceNumber')
        .then(res => res.json())
        .then(data => {
            document.getElementById('invoiceNo').value = data.invoiceNo;
        })
        .catch(err => console.error('Error fetching invoice number:', err));
}

function resetForm() {
    editId = null;
    const header = document.querySelector('.page-header h2');
    if (header) header.textContent = 'Purchase Invoice';

    // --- Header fields ---
    document.getElementById('billNo').value = '';
    document.getElementById('invoiceDate').value = new Date().toISOString().split('T')[0];
    document.getElementById('supplierAccount').value = '';
    document.getElementById('hftxtSupplierId').value = '';
    document.getElementById('supplierDetails').value = '';
    document.getElementById('remarks').value = '';
    document.getElementById('paymentMode').selectedIndex = 0;

    // --- Invoice number: fetch the accurate next ID from server ---
    loadNextInvoiceNumber();

    // --- Reset supplier state ---
    selectedSupplier = null;

    // --- Clear item rows and add a fresh empty one ---
    document.getElementById('itemsBody').innerHTML = '';
    rowCounter = 0;
    addNewRow();

    // --- Reset totals ---
    document.getElementById('gstPercent').value = '0';
    document.getElementById('freight').value = '0';
    document.getElementById('otherExp').value = '0';
    document.getElementById('discPercent').value = '0';
    calculateTotals();
}

// New Invoice
document.getElementById('newBtn').addEventListener('click', async function () {
    if (await confirmDialog('Create new invoice? Unsaved changes will be lost.')) {
        resetForm();
    }
});

// Form Submit
document.getElementById('purchaseForm').addEventListener('submit', async function (e) {
    e.preventDefault();

    const supplierAccount = document.getElementById('hftxtSupplierId').value;
    if (!supplierAccount) {
        await alertDialog('Please select a supplier!');
        return;
    }

    const rows = document.querySelectorAll('#itemsBody tr');
    let hasValidItem = false;
    rows.forEach(row => {
        const itemName = row.cells[2].querySelector('input').value;
        const qty = parseFloat(row.cells[3].querySelector('input').value) || 0;
        if (itemName && qty > 0) {
            hasValidItem = true;
        }
    });

    if (!hasValidItem) {
        await alertDialog('Please add at least one item with quantity!');
        return;
    }

    // Prepare items data
    const items = [];
    rows.forEach(row => {
        const itemId = row.cells[1].querySelector('input').value;
        const qty    = parseFloat(row.cells[3].querySelector('input').value) || 0;
        if (!itemId || qty <= 0) return;

        items.push({
            Itemid: parseInt(itemId),
            Desc: row.cells[2].querySelector('input').value,
            Quantity: qty,
            PurPrice: parseFloat(row.cells[4].querySelector('input').value) || 0,
            SalePrice: parseFloat(row.cells[5].querySelector('input').value) || 0,
            DiscPer: parseFloat(row.cells[6].querySelector('input').value) || 0,
            DiscAmt: parseFloat(row.cells[7].querySelector('input').value) || 0,
            Total: parseFloat(row.cells[8].querySelector('input').value) || 0
        });
    });

    // Full payload
    const payload = {
        PurchaseId: editId,
        PurchaseDate: document.getElementById('invoiceDate').value,
        VendorID: supplierAccount,
        BillNo: document.getElementById('billNo').value,
        BranchID: parseInt(document.getElementById('branch').value) || 1,
        PaymentMode: parseInt(document.getElementById('paymentMode').value) || 0,
        Remarks: document.getElementById('remarks').value,
        GSTPer: parseFloat(document.getElementById('gstPercent').value),
        GSTAmount: parseFloat(document.getElementById('gstAmount').value),
        FreightExp: parseFloat(document.getElementById('freight').value),
        OtherExp: parseFloat(document.getElementById('otherExp').value),
        Discount: parseFloat(document.getElementById('discAmount').value),
        TotalAmount: parseFloat(document.getElementById('invoiceTotal').value),
        NetAmount: parseFloat(document.getElementById('netAmount').textContent),
        Items: items
    };

    const url = editId ? '/Purchase/Update' : '/Purchase/Save';

    // Send to controller
    fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(res => res.json())
        .then(async data => {
            if (data.success) {
                await alertDialog('✅ ' + data.message);
                if (editId) {
                    window.location.href = '/Purchase/List';
                } else {
                    resetForm();
                }
            } else {
                await alertDialog('❌ Failed: ' + (data.message || 'Unknown error'));
            }
        })
        .catch(async err => {
            console.error(err);
            await alertDialog('Error saving invoice.');
        });
});

// supplier section
// Open Supplier Modal when clicking the icon button
document.querySelector('#btnSupplierAccount').addEventListener('click', function (e) {
    e.preventDefault();
    openSupplierModal();
});

function openSupplierModal() {
    document.getElementById('supplierModal').classList.add('show');
    loadSuppliers();
}

function closeSupplierModal() {
    document.getElementById('supplierModal').classList.remove('show');
    selectedSupplier = null;
    document.getElementById('selectSupplierBtn').disabled = true;
    document.querySelectorAll('#supplierTableBody tr').forEach(row => {
        row.classList.remove('selected');
    });
}

// Load Suppliers from API
function loadSuppliers() {
    const tbody = document.getElementById('supplierTableBody');
    tbody.innerHTML = '<tr><td colspan="7" style="text-align: center; padding: 2rem;">Loading suppliers...</td></tr>';

    // Fetch suppliers from your Parties controller
    fetch('/Parties/GetSuppliers')
        .then(response => response.json())
        .then(data => {
            if (data.success && data.suppliers && data.suppliers.length > 0) {
                allSuppliers = data.suppliers;
                displaySuppliers(allSuppliers);
            } else {
                showEmptyState();
            }
        })
        .catch(error => {
            console.error('Error loading suppliers:', error);
            tbody.innerHTML = '<tr><td colspan="7" style="text-align: center; padding: 2rem; color: #dc3545;">Error loading suppliers. Please try again.</td></tr>';
        });
}

function displaySuppliers(suppliers) {
    const tbody = document.getElementById('supplierTableBody');
    const emptyState = document.getElementById('emptyState');

    if (suppliers.length === 0) {
        showEmptyState();
        return;
    }

    emptyState.style.display = 'none';
    tbody.innerHTML = '';

    suppliers.forEach(supplier => {
        const row = document.createElement('tr');
        row.setAttribute('data-supplier-id', supplier.partyId);
        row.setAttribute('data-supplier-name', supplier.partyName);
        row.innerHTML = `
                <td>${supplier.partyId}</td>
                <td>${supplier.partyName}</td>
                <td><span class="party-badge party-badge-${supplier.partyType}">${formatPartyType(supplier.partyType)}</span></td>
                <td>${supplier.contactPerson || '-'}</td>
                <td>${supplier.phone}</td>
                <td>${supplier.email || '-'}</td>
                <td>Rs. ${parseFloat(supplier.openingBalance || 0).toFixed(2)}</td>
            `;

        row.addEventListener('click', function () {
            selectSupplierRow(this, supplier);
        });

        tbody.appendChild(row);
    });
}

function showEmptyState() {
    document.getElementById('supplierTableBody').innerHTML = '';
    document.getElementById('emptyState').style.display = 'block';
}

function formatPartyType(type) {
    if (type === 'supplier') return 'Supplier';
    if (type === 'customer') return 'Customer';
    if (type === 'both') return 'Both';
    return type;
}

function selectSupplierRow(row, supplier) {
    document.querySelectorAll('#supplierTableBody tr').forEach(r => {
        r.classList.remove('selected');
    });

    row.classList.add('selected');
    selectedSupplier = supplier;
    document.getElementById('selectSupplierBtn').disabled = false;
}

function selectSupplier() {
    if (selectedSupplier) {
        document.getElementById('hftxtSupplierId').value = selectedSupplier.partyId;
        document.getElementById('supplierAccount').value = selectedSupplier.partyName;

        let details = '';
        if (selectedSupplier.contactPerson) {
            details += '\nContact: ' + selectedSupplier.contactPerson;
        }
        if (selectedSupplier.phone) {
            details += '\nPhone: ' + selectedSupplier.phone;
        }
        if (selectedSupplier.email) {
            details += '\nEmail: ' + selectedSupplier.email;
        }
        if (selectedSupplier.address) {
            details += '\nAddress: ' + selectedSupplier.address;
        }

        document.getElementById('supplierDetails').value = details;
        closeSupplierModal();
    }
}

function searchSuppliers() {
    const searchText = document.getElementById('supplierSearchInput').value.toLowerCase();

    if (!searchText) {
        displaySuppliers(allSuppliers);
        return;
    }

    const filtered = allSuppliers.filter(supplier => {
        return supplier.partyName.toLowerCase().includes(searchText) ||
            (supplier.phone && supplier.phone.includes(searchText)) ||
            (supplier.email && supplier.email.toLowerCase().includes(searchText)) ||
            (supplier.contactPerson && supplier.contactPerson.toLowerCase().includes(searchText));
    });

    displaySuppliers(filtered);
}

document.getElementById('supplierModal').addEventListener('click', function (e) {
    if (e.target === this) {
        closeSupplierModal();
    }
});

document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        const modal = document.getElementById('supplierModal');
        if (modal.classList.contains('show')) {
            closeSupplierModal();
        }
    }
});

// item section

// Open Item Lookup Modal
function openItemLookup(button) {
    currentRow = button.closest('tr');
    document.getElementById('itemModal').classList.add('show');
    loadItems();
}

function closeItemModal() {
    document.getElementById('itemModal').classList.remove('show');
    selectedItem = null;
    currentRow = null;
    document.getElementById('selectItemBtn').disabled = true;
    document.querySelectorAll('#itemTableBody tr').forEach(row => {
        row.classList.remove('selected');
    });
}

// Load Items from API
function loadItems() {
    const tbody = document.getElementById('itemTableBody');
    tbody.innerHTML = '<tr><td colspan="7" style="text-align: center; padding: 2rem;">Loading items...</td></tr>';

    fetch('/Item/GetItems')
        .then(response => response.json())
        .then(data => {
            if (data.success && data.items && data.items.length > 0) {
                allItems = data.items;
                displayItems(allItems);
                loadCategories(data.items);
            } else {
                showItemEmptyState();
            }
        })
        .catch(error => {
            console.error('Error loading items:', error);
            tbody.innerHTML = '<tr><td colspan="7" style="text-align: center; padding: 2rem; color: #dc3545;">Error loading items. Please try again.</td></tr>';
        });
}

function loadCategories(items) {
    const categoryFilter = document.getElementById('categoryFilter');
    const categories = [...new Set(items.map(item => item.categoryName))];

    categoryFilter.innerHTML = '<option value="">All Categories</option>';
    categories.forEach(cat => {
        const option = document.createElement('option');
        option.value = cat;
        option.textContent = cat;
        categoryFilter.appendChild(option);
    });
}

function displayItems(items) {
    const tbody = document.getElementById('itemTableBody');
    const emptyState = document.getElementById('itemEmptyState');

    if (items.length === 0) {
        showItemEmptyState();
        return;
    }

    emptyState.style.display = 'none';
    tbody.innerHTML = '';

    items.forEach(item => {
        const row = document.createElement('tr');

        let stockBadge = '';
        const stock = item.currentStock || 0;
        if (stock > 50) {
            stockBadge = `<span class="stock-badge stock-badge-high">${stock}</span>`;
        } else if (stock > 0) {
            stockBadge = `<span class="stock-badge stock-badge-low">${stock}</span>`;
        } else {
            stockBadge = `<span class="stock-badge stock-badge-out">${stock}</span>`;
        }

        row.innerHTML = `
                <td>${item.itemID}</td>
                <td><img src="${item.imageUrl || NO_IMAGE_PLACEHOLDER}" class="item-image-thumb" alt="Item"></td>
                <td>${item.itemName}</td>
                <td>${item.categoryName}</td>
                <td>${item.barcode || '-'}</td>
                <td>Rs. ${parseFloat(item.salePrice).toFixed(2)}</td>
                <td>${stockBadge}</td>
            `;

        row.addEventListener('click', function () {
            selectItemRow(this, item);
        });

        tbody.appendChild(row);
    });
}

function showItemEmptyState() {
    document.getElementById('itemTableBody').innerHTML = '';
    document.getElementById('itemEmptyState').style.display = 'block';
}

function selectItemRow(row, item) {
    document.querySelectorAll('#itemTableBody tr').forEach(r => {
        r.classList.remove('selected');
    });

    row.classList.add('selected');
    selectedItem = item;
    document.getElementById('selectItemBtn').disabled = false;
}

function selectItem() {
    if (selectedItem && currentRow) {
        // Fill the row with selected item data
        currentRow.cells[1].querySelector('input').value = selectedItem.itemID;
        currentRow.cells[2].querySelector('input').value = selectedItem.itemName;
        currentRow.cells[4].querySelector('input').value = selectedItem.purchasePrice || selectedItem.salePrice;
        currentRow.cells[5].querySelector('input').value = selectedItem.salePrice;

        // Recalculate row total
        calculateRowTotal(currentRow);

        closeItemModal();
    }
}

function searchItems() {
    const searchText = document.getElementById('itemSearchInput').value.toLowerCase();
    const categoryFilter = document.getElementById('categoryFilter').value;
    const stockFilter = document.getElementById('stockFilter').value;

    let filtered = allItems;

    if (searchText) {
        filtered = filtered.filter(item => {
            return item.itemName.toLowerCase().includes(searchText) ||
                (item.barcode && item.barcode.toLowerCase().includes(searchText)) ||
                (item.categoryName && item.categoryName.toLowerCase().includes(searchText));
        });
    }

    if (categoryFilter) {
        filtered = filtered.filter(item => item.categoryName === categoryFilter);
    }

    if (stockFilter) {
        filtered = filtered.filter(item => {
            const stock = item.currentStock || 0;
            if (stockFilter === 'instock') return stock > 50;
            if (stockFilter === 'lowstock') return stock > 0 && stock <= 50;
            if (stockFilter === 'outofstock') return stock === 0;
            return true;
        });
    }

    displayItems(filtered);
}

function filterItems() {
    searchItems();
}

// Close modal on outside click
document.getElementById('itemModal').addEventListener('click', function (e) {
    if (e.target === this) {
        closeItemModal();
    }
});

// Close modal with Escape key
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        const modal = document.getElementById('itemModal');
        if (modal.classList.contains('show')) {
            closeItemModal();
        }
    }
});
