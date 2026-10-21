(() => {
  const root = document.getElementById('booking');
  if (!root) return;
  const form = document.getElementById('bookingForm');
  const service = document.getElementById('service');
  const date = document.getElementById('date');
  const time = document.getElementById('time');
  const alertBox = document.getElementById('bookingAlert');
  const submit = document.getElementById('submitBooking');
  const key = root.dataset.key;
  const showMessage = (message, kind = 'danger') => { alertBox.textContent = message; alertBox.className = `alert alert-${kind}`; };

  async function loadSlots() {
    time.replaceChildren(new Option('Consultando horarios…', ''));
    time.disabled = true;
    if (!service.value || !date.value) return;
    try {
      const response = await fetch(`/citas/disponibilidad/${encodeURIComponent(root.dataset.slug)}/${service.value}?date=${date.value}`);
      if (!response.ok) throw new Error('No pudimos consultar la disponibilidad. Intenta de nuevo.');
      const result = await response.json();
      time.replaceChildren(new Option(result.slots.length ? 'Selecciona una hora' : 'No hay horarios disponibles', ''));
      result.slots.forEach(slot => time.add(new Option(slot.slice(0, 5), slot.slice(0, 5))));
      time.disabled = result.slots.length === 0;
    } catch (error) { time.replaceChildren(new Option('No se pudo consultar', '')); showMessage(error.message); }
  }
  service.addEventListener('change', loadSlots);
  date.addEventListener('change', loadSlots);

  form.addEventListener('submit', async event => {
    event.preventDefault();
    alertBox.className = 'alert d-none';
    submit.disabled = true; submit.textContent = 'Confirmando…';
    const data = {
      serviceId: service.value, date: date.value, time: `${time.value}:00`,
      customerName: document.getElementById('customerName').value,
      customerPhone: document.getElementById('customerPhone').value,
      customerEmail: document.getElementById('customerEmail').value || null,
      idempotencyKey: key
    };
    try {
      const response = await fetch(`/citas/reservar/${encodeURIComponent(root.dataset.slug)}`, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': form.querySelector('[name="__RequestVerificationToken"]').value },
        body: JSON.stringify(data)
      });
      const result = await response.json();
      if (response.status === 409) { showMessage(`${result.message} Tus datos siguen aquí.`); await loadSlots(); return; }
      if (!response.ok) throw new Error(result.fields?.length ? `${result.message} Campos: ${result.fields.join(', ')}.` : result.message || 'No se pudo confirmar la cita. Revisa tus datos e intenta de nuevo.');
      const card = document.getElementById('confirmation');
      const when = new Date(`${result.date}T${result.time}`);
      card.innerHTML = `<p class="eyebrow">Confirmada</p><h2 class="h3">Tu cita está lista</h2><p><strong>${escapeHtml(result.businessName)}</strong></p><dl class="row"><dt class="col-5">Folio</dt><dd class="col-7">${escapeHtml(result.id)}</dd><dt class="col-5">Servicio</dt><dd class="col-7">${escapeHtml(result.service)}</dd><dt class="col-5">Precio</dt><dd class="col-7">$${Number(result.price).toFixed(2)} MXN</dd><dt class="col-5">Duración</dt><dd class="col-7">${result.duration} minutos</dd><dt class="col-5">Fecha y hora</dt><dd class="col-7">${escapeHtml(result.date)} · ${escapeHtml(result.time.slice(0,5))}</dd><dt class="col-5">Zona horaria</dt><dd class="col-7">${escapeHtml(result.timeZone)}</dd></dl><p class="mb-2">Guarda este enlace privado para consultar, cancelar o reprogramar tu cita:</p><a class="btn btn-outline-primary" href="${escapeHtml(result.manageUrl)}">Ver o gestionar mi cita</a><p class="small text-secondary mt-2 mb-0">El enlace también se procesa para enviarse por correo si proporcionaste una dirección.</p>`;
      card.classList.remove('d-none'); form.classList.add('d-none'); card.scrollIntoView({ behavior: 'smooth' });
    } catch (error) { showMessage(error.message); }
    finally { submit.disabled = false; submit.textContent = 'Confirmar cita'; }
  });
  function escapeHtml(value) { const span = document.createElement('span'); span.textContent = value ?? ''; return span.innerHTML; }
})();
