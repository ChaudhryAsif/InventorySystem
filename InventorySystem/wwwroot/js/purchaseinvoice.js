let rowCounter = 0;
let selectedSupplier = null;
let allSuppliers = [];
let selectedItem = null;
let allItems = [];
let currentRow = null;


// Add first row on page load
window.addEventListener('DOMContentLoaded', function () {
    addNewRow();
    loadNextInvoiceNumber();
});

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

function removeRow(row) {
    if (document.getElementById('itemsBody').rows.length > 1) {
        if (confirm('Remove this item?')) {
            row.remove();
            updateRowNumbers();
            calculateTotals();
        }
    } else {
        alert('At least one row is required!');
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
    // --- Header fields ---
    document.getElementById('billNo').value = '';
    document.getElementById('invoiceDate').value = new Date().toISOString().split('T')[0];
    document.getElementById('supplierAccount').value = '';
    document.getElementById('hftxtSupplierId').value = '';
    document.getElementById('supplierDetails').value = '';
    document.getElementById('remarks').value = '';
    // document.getElementById('paymentMode').selectedIndex = 0;
    // document.getElementById('branch').selectedIndex = 0;
    document.getElementById('printStyle').selectedIndex = 0;

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

// Event listeners for total calculations
document.getElementById('gstPercent').addEventListener('input', calculateTotals);
document.getElementById('freight').addEventListener('input', calculateTotals);
document.getElementById('otherExp').addEventListener('input', calculateTotals);
document.getElementById('discPercent').addEventListener('input', calculateTotals);

// New Invoice
document.getElementById('newBtn').addEventListener('click', function () {
    if (confirm('Create new invoice? Unsaved changes will be lost.')) {
        document.getElementById('purchaseForm').reset();
        document.getElementById('itemsBody').innerHTML = '';
        rowCounter = 0;
        addNewRow();
        calculateTotals();
    }
});

// Edit Invoice
// document.getElementById('editBtn').addEventListener('click', function() {
//     alert('Edit mode activated');
// });

// Delete Invoice
// document.getElementById('deleteBtn').addEventListener('click', function() {
//     if (confirm('Delete this invoice?')) {
//         document.getElementById('purchaseForm').reset();
//         document.getElementById('itemsBody').innerHTML = '';
//         rowCounter = 0;
//         addNewRow();
//         calculateTotals();
//         alert('Invoice deleted successfully!');
//     }
// });

// Form Submit
document.getElementById('purchaseForm').addEventListener('submit', function (e) {
    e.preventDefault();

    const supplierAccount = document.getElementById('hftxtSupplierId').value;
    if (!supplierAccount) {
        alert('Please select a supplier!');
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
        alert('Please add at least one item with quantity!');
        return;
    }

    const invoiceData = {
        invoiceNo: document.getElementById('invoiceNo').value,
        billNo: document.getElementById('billNo').value,
        invoiceDate: document.getElementById('invoiceDate').value,
        // branch: document.getElementById('branch').value,
        supplierAccount: supplierAccount,
        // paymentMode: document.getElementById('paymentMode').value,
        remarks: document.getElementById('remarks').value,
        totalQty: document.getElementById('totalQty').value,
        invoiceTotal: document.getElementById('invoiceTotal').value,
        netAmount: document.getElementById('netAmount').textContent
    };

    // Prepare items data
    const items = [];
    rows.forEach(row => {
        const item = {
            Itemid: row.cells[1].querySelector('input').value,
            Desc: row.cells[2].querySelector('input').value,
            Quantity: parseFloat(row.cells[3].querySelector('input').value) || 0,
            PurPrice: parseFloat(row.cells[4].querySelector('input').value) || 0,
            SalePrice: parseFloat(row.cells[5].querySelector('input').value) || 0,
            DiscPer: parseFloat(row.cells[6].querySelector('input').value) || 0,
            DiscAmt: parseFloat(row.cells[7].querySelector('input').value) || 0,
            Total: parseFloat(row.cells[8].querySelector('input').value) || 0
        };
        items.push(item);
    });

    // Full payload
    const payload = {
        PurchaseDate: document.getElementById('invoiceDate').value,
        VendorID: supplierAccount,
        BillNo: document.getElementById('billNo').value,
        BranchID: 1,
        // PaymentMode: document.getElementById('paymentMode').selectedIndex,
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

    // Send to controller
    fetch('/Purchase/Save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
    })
        .then(res => res.json())
        .then(data => {
            if (data.success) {
                alert('✅ ' + data.message);
                resetForm(data.id);

            } else {
                alert('❌ Failed: ' + (data.message || 'Unknown error'));
            }
        })
        .catch(err => {
            console.error(err);
            alert('Error saving invoice.');
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
                <td>$${parseFloat(supplier.openingBalance || 0).toFixed(2)}</td>
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
                <td><img src="${item.imageUrl || 'https://via.placeholder.com/40'}" class="item-image-thumb" alt="Item"></td>
                <td>${item.itemName}</td>
                <td>${item.categoryName}</td>
                <td>${item.barcode || '-'}</td>
                <td>$${parseFloat(item.salePrice).toFixed(2)}</td>
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