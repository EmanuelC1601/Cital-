// Keep the compact mobile menu in the document flow so it never covers page controls.
const mobileNavToggle = document.getElementById('mobileOwnerNavToggle');
const mobileOwnerNav = document.getElementById('mobileOwnerNav');

mobileNavToggle?.addEventListener('click', () => {
    const willOpen = mobileNavToggle.getAttribute('aria-expanded') !== 'true';
    mobileNavToggle.setAttribute('aria-expanded', String(willOpen));
    mobileOwnerNav.hidden = !willOpen;
});

mobileNavToggle?.addEventListener('keydown', event => {
    if (event.key === 'Escape' && mobileNavToggle.getAttribute('aria-expanded') === 'true') {
        mobileNavToggle.setAttribute('aria-expanded', 'false');
        mobileOwnerNav.hidden = true;
    }
});

// The server remains the authority for slot validation; this only submits the chosen agenda date.
const agendaDate = document.getElementById('agendaDate');
agendaDate?.addEventListener('change', () => document.getElementById('agendaDateForm')?.requestSubmit());

document.querySelectorAll('[data-open-appointment]').forEach(link => {
    link.addEventListener('click', () => {
        const panel = document.getElementById('new-appointment');
        if (panel) panel.open = true;
    });
});

const appointmentPanel = document.getElementById('new-appointment');
const mobileActionBar = document.querySelector('.mobile-action-bar');
if (appointmentPanel && mobileActionBar) {
    // Hide the fixed quick actions while editing so they cannot cover customer fields on a phone.
    const syncMobileActionBar = () => { mobileActionBar.hidden = appointmentPanel.open; };
    appointmentPanel.addEventListener('toggle', syncMobileActionBar);
    syncMobileActionBar();
}

// Restore the visible new-appointment panel when the mobile quick action navigates to this page.
if (window.location.hash === '#new-appointment') {
    const panel = document.getElementById('new-appointment');
    if (panel) {
        panel.open = true;
        requestAnimationFrame(() => panel.scrollIntoView({ block: 'start' }));
    }
}
