(() => {
  const token = location.hash.slice(1);
  const status = document.getElementById('manageStatus');
  const details = document.getElementById('manageDetails');
  const message = document.getElementById('manageMessage');
  const request = async (path, body) => {
    const response = await fetch(`/citas/gestionar/${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const result = await response.json();
    if (!response.ok) throw new Error(result.message || 'No se pudo procesar la solicitud.');
    return result;
  };
  const say = (text, kind = 'info') => { message.textContent = text; message.className = `alert alert-${kind} mt-3`; };
  if (!token) { status.textContent = 'El enlace no contiene un token. Usa el enlace privado que recibiste al confirmar.'; return; }
  let appointment;
  request('datos', { token }).then(result => {
    appointment = result;
    const labels = { Confirmed: 'Confirmada', Cancelled: 'Cancelada', Completed: 'Atendida', NoShow: 'Inasistencia' };
    document.getElementById('summary').innerHTML = `<dt class="col-5">Negocio</dt><dd class="col-7">${esc(result.businessName)}</dd><dt class="col-5">Servicio</dt><dd class="col-7">${esc(result.serviceName)}</dd><dt class="col-5">Precio</dt><dd class="col-7">$${Number(result.price).toFixed(2)} MXN</dd><dt class="col-5">Duración</dt><dd class="col-7">${result.duration} minutos</dd><dt class="col-5">Fecha y hora</dt><dd class="col-7">${esc(result.date)} · ${esc(result.time.slice(0,5))}</dd><dt class="col-5">Zona horaria</dt><dd class="col-7">${esc(result.timeZoneId)}</dd><dt class="col-5">Estado</dt><dd class="col-7">${labels[result.status] || 'Desconocida'}</dd>`;
    status.textContent = 'Consulta o cambia tu cita. El token vence a los 90 días.'; details.classList.remove('d-none');
    if (result.status !== 'Confirmed') document.getElementById('manageActions').classList.add('d-none');
  }).catch(error => { status.textContent = error.message; });
  document.getElementById('cancelAppointment').addEventListener('click', async () => {
    if (!confirm('¿Cancelar esta cita?')) return;
    try { await request('accion', { token, action: 'cancelar' }); say('La cita quedó cancelada. El horario ya está disponible.', 'success'); document.getElementById('manageActions').classList.add('d-none'); }
    catch (error) { say(error.message, 'danger'); }
  });
  document.getElementById('showReschedule').addEventListener('click', () => document.getElementById('rescheduleBox').classList.toggle('d-none'));
  const date = document.getElementById('newDate');
  date.addEventListener('change', async () => {
    const select = document.getElementById('newTime'); select.replaceChildren(new Option('Consultando…', '')); select.disabled = true;
    try { const result = await request('horarios', { token, date: date.value }); select.replaceChildren(new Option(result.slots.length ? 'Selecciona una hora' : 'Sin horarios', '')); result.slots.forEach(x => select.add(new Option(x.slice(0,5), x.slice(0,5)))); select.disabled = !result.slots.length; }
    catch (error) { say(error.message, 'danger'); }
  });
  document.getElementById('saveReschedule').addEventListener('click', async () => {
    const time = document.getElementById('newTime').value;
    if (!date.value || !time) { say('Elige fecha y hora disponibles.', 'warning'); return; }
    try { const result = await request('accion', { token, action: 'reprogramar', date: date.value, time: `${time}:00` }); location.hash = result.manageUrl.split('#')[1]; location.reload(); }
    catch (error) { say(`${error.message} Tu cita original permanece sin cambios.`, 'danger'); }
  });
  function esc(value) { const el = document.createElement('span'); el.textContent = value ?? ''; return el.innerHTML; }
})();
