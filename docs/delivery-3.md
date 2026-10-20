# Entrega 3: disponibilidad y reservas seguras

## Funciones

- La página `/b/{slug}/reservar` permite reservar sin una cuenta. El cliente proporciona nombre y teléfono; el correo es opcional. La confirmación aparece inmediatamente con servicio, precio, duración, fecha, hora y zona horaria. No se envía ni simula correo.
- El calendario ofrece inicios cada 15 minutos. Un espacio debe caber en horario semanal o excepción especial y debe dejar libre el descanso, los bloqueos, la duración del servicio y su margen. Se omiten horas locales inexistentes o ambiguas por cambios de horario. Los instantes se guardan como `datetimeoffset` UTC.
- Cada negocio configura anticipación mínima (0–10,080 minutos; valor inicial 60) y horizonte máximo (1–365 días; inicial 60). Los cambios afectan nuevas reservas y disponibilidad futura; no alteran citas confirmadas.
- El propietario consulta la agenda en la zona horaria del negocio, agrega citas manuales, cancela, reprograma, marca atendida o inasistencia. Cliente y propietario usan la misma exclusión transaccional.
- El cliente consulta, cancela y reprograma mediante enlace privado. Una reprogramación exitosa invalida el enlace anterior y presenta uno nuevo. La confirmación, el panel de agenda y el perfil público no exponen correo de Identity, motivos de bloqueo ni citas de otros negocios.
- El precio, nombre, duración y margen acordados se guardan en la cita. Editar o desactivar el servicio solo cambia reservas futuras; no cambia las instantáneas. El servicio no se puede borrar si aparece en el historial.

## Integridad y concurrencia SQL Server

Cada operación que crea, reprograma o cambia el estado de una cita abre una transacción `ReadCommitted` y ejecuta, antes de validar disponibilidad, un `SELECT ... FROM dbo.Businesses WITH (UPDLOCK, HOLDLOCK) WHERE Id = ...`. El bloqueo de actualización sobre la fila del negocio se conserva hasta el commit y serializa las escrituras de agenda de ese negocio, incluso si todavía no hay una fila de cita para el horario consultado. Los endpoints públicos, altas manuales, gestión con token y cambios de estado usan el mismo procedimiento. Los cambios de catálogo y calendario que pueden alterar disponibilidad adquieren el mismo bloqueo.

La consulta de disponibilidad se vuelve a ejecutar dentro de la transacción inmediatamente antes de guardar. Por eso dos solicitudes para el mismo horario no pueden pasar simultáneamente la validación. La reprogramación valida el nuevo inicio antes de cambiar la cita, dentro de la misma transacción; un conflicto devuelve 409 y el rollback conserva el inicio original. El margen se considera tiempo ocupado para detectar cruces.

El formulario crea una clave aleatoria de idempotencia por intento. SQL Server impone unicidad por negocio y hash de clave; un reintento recupera la cita existente y devuelve la misma confirmación y el mismo enlace, sin duplicar la fila. Para poder repetir la respuesta sin guardar el enlace en claro, la base conserva el hash SHA-256 para validarlo y una copia cifrada con ASP.NET Data Protection para recuperarlo solo en el servidor. Un intento reutilizado después de que la cita ya tenga estado final recibe 409 y requiere una nueva solicitud.

Los cambios de horario que quitarían disponibilidad se rechazan si alcanzan citas confirmadas: descansos, bloqueos, excepciones y retiro de horarios comprueban las citas existentes bajo el mismo bloqueo. No se quitan excepciones con citas en esa fecha. Los cambios de zona horaria se rechazan mientras haya citas futuras. Las reglas que solo amplían disponibilidad se guardan sin tocar las citas. Cancelar libera inmediatamente el horario.

## Enlaces, abuso y privacidad

- Se generan 32 bytes aleatorios y se codifican en Base64URL. La base conserva el hash SHA-256 y el token cifrado con ASP.NET Data Protection; nunca se almacena en claro. Conocer el ID de una cita no sirve para administrarla.
- El enlace dura 90 días desde su emisión. El token viaja en el fragmento de URL, que no se envía al servidor en la navegación inicial; el JavaScript lo entrega por POST en el cuerpo. Al reprogramar se emite otro token y el hash/cifrado anterior deja de ser válido. Después del vencimiento, el cliente debe pedir un enlace nuevo al negocio; todavía no hay correo de recuperación.
- ASP.NET Data Protection usa el anillo de claves local de la aplicación. En un despliegue, hay que persistirlo en almacenamiento protegido y compartirlo entre instancias; si se pierde, los enlaces vigentes seguirán validándose por hash, pero no se podrán reconstruir respuestas idempotentes con su mismo enlace.
- Las páginas públicas no proyectan datos personales de citas ni del propietario. No se registra el token en los logs de la aplicación; EF no tiene logging de valores sensibles habilitado. El límite fijo es de 20 solicitudes por IP cada 10 minutos para disponibilidad y gestión públicas. En una futura operación tras proxy se debe configurar una cadena de proxies de confianza para que el límite use la IP del cliente correctamente.
- Las citas creadas manualmente generan un enlace que el propietario puede copiar y compartir personalmente. No se envía correo, WhatsApp ni otra notificación automática.

## Migración y desarrollo

Las migraciones `20261020161620_Delivery3AppointmentsAndBookingLimits` y `20261020164023_Delivery3ManagementTokenProtection` agregan límites, tablas de citas e historial, índices de agenda/tokens/idempotencia y el token cifrado para recuperar una respuesta idempotente. Son aditivas: no borran tablas ni filas anteriores. No se aplican automáticamente al iniciar.

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=(localdb)\MSSQLLocalDB;Database=Belleza;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True'
dotnet ef database update --project src/Belleza.Infrastructure --startup-project src/Belleza.Web
dotnet run --project src/Belleza.Web
```

Mantén la cadena en una variable de entorno y no la incluyas en `appsettings*.json`, Git ni logs. Consulta [persistencia](persistence.md) para SQL Server LocalDB y Docker.

## Verificación ejecutada

El 20 de octubre de 2026 se aplicaron las dos migraciones de Entrega 3 sobre `BellezaDelivery1Validation`, conservando las tres migraciones previas. La instancia fue SQL Server 2019 Express LocalDB 15.0.4382.1. SQL Server reportó cinco migraciones aplicadas; `ManageTokenHash` e `IdempotencyKeyHash` tienen 64 caracteres y `ManageTokenCiphertext` tiene capacidad de 500. Las comprobaciones incluyeron:

- Flujo anónimo en navegador: página pública, servicio, fecha, horarios, reserva y resumen persistido. El cliente no inició sesión.
- Flujo del propietario: registro, negocio, catálogo, horario semanal, agenda, alta manual y cancelación.
- Dos POST simultáneos al mismo inicio con distintas claves: uno respondió 200, el otro 409, y SQL Server guardó una sola cita.
- Dos POST simultáneos con la misma clave: ambos respondieron 200 con el mismo ID y el mismo enlace; SQL Server guardó una sola fila. Se verificó después del cifrado del token; el hash almacenado mide 64 caracteres y el texto cifrado no se imprimió.
- Un servicio de 30 minutos mostró 10:15 cuando el de 60 minutos más margen no lo ofrecía. Un inicio distinto solapado y una reserva pública en conflicto con una cita manual fueron rechazados.
- Reprogramación a un espacio ocupado respondió 409 y la hora UTC original permaneció igual. Una reprogramación válida cambió la cita, agregó historial y revocó el token anterior.
- Una cancelación liberó el inicio en la consulta de disponibilidad. Se probaron token válido, token inválido/rotado y token vencido (401).
- Descanso semanal, apertura excepcional y bloqueo cambiaron los horarios devueltos. El sistema rechazó crear un bloqueo que invadía una cita. Anticipación de 7 días y horizonte de 1 día ocultaron fechas fuera de límite; los valores se restauraron después.
- Un segundo propietario vio la agenda vacía y un POST de cambio de estado sobre la cita ajena respondió 404, sin modificarla.
- El límite público respondió 429 desde la solicitud 21 de 25 en una ventana de 10 minutos.
- La respuesta pública no contenía el correo del propietario, nombre ni teléfono del cliente, ni el motivo privado de un bloqueo. Se inspeccionó la página pública en 1440×1000 y 390×844.
- `dotnet build Belleza.sln` y `dotnet test Belleza.sln` finalizaron correctamente; las pruebas unitarias son complementarias a las verificaciones SQL Server anteriores.

La base de validación comenzó sin usuarios ni negocios. Se crearon registros sintéticos en LocalDB y se retiraron al terminar usando sus identificadores/correo exactos; no se modificaron datos de otros usuarios. La base quedó sin usuarios, negocios ni citas de prueba; se conservaron las cinco migraciones y el esquema.

## Pendiente y alcance

No hay correos durables ni recuperación automática de enlaces, confirmación manual, pagos, lista de espera, WhatsApp, profesionales múltiples ni sincronización con calendarios externos. Se requiere todavía revisión de accesibilidad, políticas de retención/privacidad, despliegue y pruebas de carga bajo la infraestructura final. Esta entrega no está desplegada y no declara lista para producción la plataforma completa.
