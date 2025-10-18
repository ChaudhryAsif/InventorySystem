// Toggle sidebar
function toggleSidebar() {
    const sidebar = document.getElementById('sidebarId');
    const mainWrapper = document.getElementById('mainWrapper');

    if (window.innerWidth <= 768) {
        sidebar.classList.toggle('show');
    } else {
        sidebar.classList.toggle('hidden');
        mainWrapper.classList.toggle('expanded');
    }
}

// Handle responsive sidebar
window.addEventListener('resize', function () {
    if (window.innerWidth > 768) {
        document.getElementById('sidebarId').classList.remove('show');
    }
});

// Common modal functions
//function openModal(modalId) {
//    document.getElementById(modalId).classList.add('show');
//}

function closeModal(modalId) {
    document.getElementById(modalId).classList.remove('show');
}

// Close modal on outside click
document.addEventListener('click', function (e) {
    if (e.target.classList.contains('modal')) {
        e.target.classList.remove('show');
    }
});

// Close modal with Escape key
document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') {
        document.querySelectorAll('.modal.show').forEach(modal => {
            modal.classList.remove('show');
        });
    }
});

// AJAX Helper function
function ajaxPost(url, data, successCallback, errorCallback) {
    fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(data)
    })
        .then(response => response.json())
        .then(data => {
            if (successCallback) successCallback(data);
        })
        .catch(error => {
            if (errorCallback) errorCallback(error);
            else console.error('Error:', error);
        });
}