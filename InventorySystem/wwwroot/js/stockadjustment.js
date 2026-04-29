let adjustmentItems = [];
let allCategories = [];

// Initialize on page load
document.addEventListener('DOMContentLoaded', function () {
    loadNextAdjustmentNo();
    loadCategories();
});

// Load next adjustment number
async function loadNextAdjustmentNo() {
    try {
        const response = await fetch('/StockAdjustment/GetNextAdjustmentNo');
        const data = await response.json();
        document.getElementById('adjustmentNo').value = data.adjustmentNo;
    } catch (error) {
        console.error('Error loading adjustment number:', error);
    }
}

// Load categories for filter
async function loadCategories() {
    try {
        const response = await fetch('/StockAdjustment/GetCategories');
        allCategories = await response.json();

        console.log('Categories loaded:', allCategories); // Debug log

        const select = document.getElementById('categoryFilter');

        // Clear existing options except "All Categories"
        select.innerHTML = '<option value="">All Categories</option>';

        allCategories.forEach(cat => {
            const option = document.createElement('option');
            option.value = cat.categoryId; // Use lowercase to match JSON
            option.textContent = cat.categoryName; // Use lowercase to match JSON
            select.appendChild(option);
        });

        console.log('Categories added to dropdown:', select.options.length); // Debug
    } catch (error) {
        console.error('Error loading categories:', error);
    }
}

// Open item selection modal
function openItemModal() {
    const modal = document.getElementById('itemModal');
    modal.style.display = 'block';

    // Clear search and category filter
    document.getElementById('itemSearch').value = '';
    document.getElementById('categoryFilter').value = '';

    // Show loading state
    const tbody = document.getElementById('itemSearchResults');
    tbody.innerHTML = '<tr><td colspan="6" style="text-align: center; padding: 2rem; color: #3b82f6;"><div style="display: inline-block; width: 20px; height: 20px; border: 3px solid #e5e7eb; border-top-color: #3b82f6; border-radius: 50%; animation: spin 0.8s linear infinite;"></div> Loading items...</td></tr>';

    // Load items
    searchItems();
}

// Close item modal
function closeItemModal() {
    document.getElementById('itemModal').style.display = 'none';
}

// Search items
async function searchItems() {
    const search = document.getElementById('itemSearch').value;
    const categoryId = document.getElementById('categoryFilter').value;

    console.log('Searching items:', { search, categoryId }); // Debug log

    try {
        const response = await fetch(`/StockAdjustment/GetItems?search=${encodeURIComponent(search)}&categoryId=${categoryId}`);

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        const items = await response.json();

        console.log('Items received:', items); // Debug log

        const tbody = document.getElementById('itemSearchResults');

        if (!items || items.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" style="text-align: center; padding: 2rem; color: #6b7280;">No items found. Try adjusting your search.</td></tr>';
            return;
        }

        tbody.innerHTML = items.map(item => `
            <tr style="cursor: pointer; transition: background 0.2s;" 
                onclick="selectItem(${item.itemID})"
                onmouseover="this.style.background='#f0f7ff'" 
                onmouseout="this.style.background=''">
                <td style="padding: 0.75rem;">${item.itemID}</td>
                <td style="padding: 0.75rem;">
                    <strong>${item.itemName || 'N/A'}</strong><br>
                    <small style="color: #6b7280;">${item.companyName || ''}</small>
                </td>
                <td style="padding: 0.75rem;">${item.categoryName || 'N/A'}</td>
                <td class="num" style="padding: 0.75rem; text-align: right; font-weight: 600;">${item.currentStock || 0}</td>
                <td class="num" style="padding: 0.75rem; text-align: right; font-weight: 600; color: #059669;">${item.salePrice ? item.salePrice.toFixed(2) : '0.00'}</td>
                <td style="padding: 0.75rem;">
                    <button class="btn btn-sm btn-primary" 
                            style="padding: 0.4rem 1rem; background: #3b82f6; border: none; border-radius: 6px; color: white; cursor: pointer;"
                            onclick="selectItem(${item.itemID}); event.stopPropagation();">
                        Select
                    </button>
                </td>
            </tr>
        `).join('');

        console.log('Items rendered successfully'); // Debug log

    } catch (error) {
        console.error('Error searching items:', error);
        const tbody = document.getElementById('itemSearchResults');
        tbody.innerHTML = `<tr><td colspan="6" style="text-align: center; padding: 2rem; color: #dc2626;">
            <strong>⚠️ Error loading items</strong><br>
            <small style="color: #6b7280;">${error.message}</small>
        </td></tr>`;
    }
}

// Select item from modal
async function selectItem(itemId) {
    try {
        const response = await fetch(`/StockAdjustment/GetItems?search=&categoryId=`);
        const items = await response.json();
        const item = items.find(i => i.itemID === itemId);

        if (!item) {
            alert('Item not found. Please try again.');
            return;
        }

        // Check if item already added
        if (adjustmentItems.some(i => i.ItemID === itemId)) {
            alert('This item is already added to the adjustment.');
            return;
        }

        adjustmentItems.push({
            ItemID: item.itemID,
            ItemName: item.itemName,
            CompanyName: item.companyName,
            CurrentStock: item.currentStock,
            Quantity: 0,
            UnitCost: 0,
            TotalCost: 0,
            Description: '',
            Reason: ''
        });

        closeItemModal();
        renderItemsTable();
    } catch (error) {
        console.error('Error selecting item:', error);
        alert('Error selecting item. Please try again.');
    }
}

// Render items table
function renderItemsTable() {
    const tbody = document.getElementById('itemsTableBody');

    if (adjustmentItems.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="10" style="text-align: center; padding: 2rem; color: #6b7280;">
                    No items added yet. Click "Add Item" to start.
                </td>
            </tr>
        `;
        updateSummary();
        return;
    }

    tbody.innerHTML = adjustmentItems.map((item, index) => `
        <tr>
            <td>${index + 1}</td>
            <td>${item.ItemID}</td>
            <td>
                <strong>${item.ItemName || ''}</strong><br>
                <small style="color: #6b7280;">${item.CompanyName || ''}</small>
            </td>
            <td>
                <input type="text" class="item-input" value="${item.Description || ''}" 
                       onchange="updateItemField(${index}, 'Description', this.value)">
            </td>
            <td class="num"><strong>${item.CurrentStock}</strong></td>
            <td>
                <input type="number" class="item-input" value="${item.Quantity}" min="0" step="0.01"
                       onchange="updateItemQuantity(${index}, parseFloat(this.value) || 0)">
            </td>
            <td>
                <input type="number" class="item-input" value="${item.UnitCost}" min="0" step="0.01"
                       onchange="updateItemCost(${index}, parseFloat(this.value) || 0)">
            </td>
            <td class="num">
                <input type="number" class="item-input calc" value="${item.TotalCost.toFixed(2)}" readonly>
            </td>
            <td>
                <input type="text" class="item-input" value="${item.Reason || ''}" 
                       placeholder="e.g., Opening stock"
                       onchange="updateItemField(${index}, 'Reason', this.value)">
            </td>
            <td>
                <button class="btn-remove" onclick="removeItem(${index})">✖</button>
            </td>
        </tr>
    `).join('');

    updateSummary();
}

// Update item field
function updateItemField(index, field, value) {
    adjustmentItems[index][field] = value;
}

// Update item quantity
function updateItemQuantity(index, quantity) {
    adjustmentItems[index].Quantity = quantity;
    adjustmentItems[index].TotalCost = quantity * adjustmentItems[index].UnitCost;
    renderItemsTable();
}

// Update item cost
function updateItemCost(index, cost) {
    adjustmentItems[index].UnitCost = cost;
    adjustmentItems[index].TotalCost = adjustmentItems[index].Quantity * cost;
    renderItemsTable();
}

// Remove item
function removeItem(index) {
    if (confirm('Remove this item from adjustment?')) {
        adjustmentItems.splice(index, 1);
        renderItemsTable();
    }
}

// Update summary
function updateSummary() {
    const totalItems = adjustmentItems.length;
    const totalQuantity = adjustmentItems.reduce((sum, item) => sum + item.Quantity, 0);
    const totalCost = adjustmentItems.reduce((sum, item) => sum + item.TotalCost, 0);

    document.getElementById('totalItems').textContent = totalItems;
    document.getElementById('totalQuantity').textContent = totalQuantity.toFixed(2);
    document.getElementById('totalCost').textContent = totalCost.toFixed(2);
}

// Save as draft
async function saveDraft() {
    await saveAdjustment('Draft');
}

// Save and post
async function saveAndPost() {
    if (!confirm('This will add the quantities to stock. Continue?')) {
        return;
    }
    await saveAdjustment('Posted');
}

// Save adjustment
async function saveAdjustment(status) {
    if (adjustmentItems.length === 0) {
        alert('Please add at least one item.');
        return;
    }

    // Validate quantities
    const invalidItems = adjustmentItems.filter(item => item.Quantity <= 0);
    if (invalidItems.length > 0) {
        alert('All items must have quantity greater than 0.');
        return;
    }

    const data = {
        AdjustmentNo: document.getElementById('adjustmentNo').value,
        AdjustmentDate: document.getElementById('adjustmentDate').value,
        //BranchId: parseInt(document.getElementById('branchId').value),
        BranchId: 1,
        AdjustmentType: document.getElementById('adjustmentType').value,
        Remarks: document.getElementById('remarks').value,
        Status: status,
        Details: adjustmentItems.map(item => ({
            ItemId: item.ItemID,
            Description: item.Description,
            Quantity: item.Quantity,
            UnitCost: item.UnitCost,
            Reason: item.Reason
        }))
    };

    try {
        const response = await fetch('/StockAdjustment/Save', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(data)
        });

        const result = await response.json();

        if (result.success) {
            alert(result.message);
            window.location.href = '/StockAdjustment/Index';
        } else {
            alert('Error: ' + result.message);
        }
    } catch (error) {
        console.error('Error saving adjustment:', error);
        alert('Error saving adjustment. Please try again.');
    }
}

// Reset form
function resetForm() {
    if (confirm('This will clear all data. Continue?')) {
        adjustmentItems = [];
        renderItemsTable();
        document.getElementById('adjustmentDate').value = new Date().toISOString().split('T')[0];
        document.getElementById('adjustmentType').value = 'Opening Stock';
        document.getElementById('remarks').value = '';
        loadNextAdjustmentNo();
    }
}