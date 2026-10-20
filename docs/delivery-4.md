# Entrega 4: notificaciones persistentes y acceso

## Alcance implementado

- `IEmailSender` vive en Application. Infraestructura aporta capturador local (solo Development) y transporte SMTP configurable. Ninguna credencial está en el repositorio; en desarrollo nunca se llama a SMTP.
- Confirmación de reserva, avisos de cancelación/reprogramación, avisos al propietario con vínculo autenticado, recordatorios programables de 0–168 horas (0 desactiva), confirmación de cuenta, recuperación/restablecimiento de contraseña y recuperación del enlace privado de una reserva.
- Registro de cuentas y su trabajo de confirmación comparten transacción SQL. Los mensajes de citas se agregan al DbContext/transacción en las escrituras de agenda ya protegidas con `UPDLOCK, HOLDLOCK`. Si el SMTP falla, la transacción de cita no se revierte; el worker procesa el outbox aparte.
- Carga del correo (destinatario, asunto, cuerpo y vínculos/tokens) se cifra con Data Protection, propósito `Citalu.EmailOutbox.Payload.v1`; no se registran contenidos ni tokens. Los mensajes enviados purgan destinatario, asunto y carga. Un barrido del worker purga cargas fallidas después de 30 días y conserva un fallo resumido solo por tipo de excepción.
- El worker usa transacción SQL y `UPDLOCK, READPAST, ROWLOCK`, lease de 2 minutos, reintentos exponenciales (15 s hasta una hora), límite de 8 intentos y recuperación al reiniciar/expirar lease. El propietario puede reencolar desde su agenda un fallo de notificación de cita mientras la carga cifrada no haya vencido; los fallos de correo de cuenta requieren una solicitud nueva. Los cambios de cita invalidan trabajos pendientes y, justo antes del envío, se comprueba estado y versión. El mismo trabajo puede reintentarse tras un fallo después de entregar SMTP y antes de guardar `Sent`; SMTP no ofrece idempotencia portable, por lo que ese caso puede duplicar el correo. El identificador de idempotencia se entrega a `IEmailSender` para proveedores que sí la soporten.
- La agenda muestra el estado de notificación separado del estado de cita. Los avisos al propietario solo se encolan cuando su correo está confirmado, para no enviar detalles de la agenda a direcciones sin verificar. Las cuentas previas sin correo confirmado conservan acceso; tras iniciar sesión reciben aviso visible y pueden pedir reenvío. Alta, confirmación, solicitud de recuperación y login no afirman si existe una cuenta. La recuperación de contraseña requiere correo confirmado.
- La recuperación de gestión requiere folio y correo asociado y responde lo mismo haya coincidencia o no. Solo se envía al correo almacenado en la reserva. Si el token venció, genera otro de 90 días y reemplaza hash/cifrado (los anteriores quedan invalidados); si sigue vigente, reenvía el mismo. Reprogramar conserva la regla anterior: emite token nuevo e invalida el anterior.
- Rate limit de recuperación: 5 solicitudes/IP/hora para cuenta y límite público existente de 20/10 min para reservas/gestión. Esos límites son locales al proceso; detrás de proxy deben configurarse proxies confiables, y varias réplicas requieren un limitador distribuido.
- Data Protection persiste claves en `src/Citalu.Web/data/keys` durante desarrollo y en el volumen configurado para Compose. No cambió el nombre de aplicación ni el propósito legado de citas `Belleza.AppointmentManagement.v1`; se mantiene fallback para leer tokens heredados.

## Configuración del correo

La aplicación local captura mensajes como JSON bajo `src/Citalu.Web/data/mail-capture` (ignorado por Git), elimina capturas con más de 7 días y no contacta servicios externos. Configura `Application:PublicBaseUrl` para enlaces. En producción, `Email:Smtp:Host`, `Port`, `EnableSsl`, `From`, `Username` y `Password` se proporcionan mediante secretos/variables del entorno de ejecución; sin Host/From, el worker reintenta y eventualmente conserva estado `Failed`. SMTP no implementa clave idempotente; conectar un proveedor con idempotencia requiere una implementación que use `EmailMessage.IdempotencyKey`.

Antes de habilitar un remitente real, publica registros SPF que autoricen al proveedor, habilita DKIM con sus selectores publicados y configura DMARC inicialmente en modo de observación (`p=none`), revisa reportes y endurece gradualmente la política. Alinea el dominio de `From` con SPF o DKIM. No se modificó DNS ni se enviaron correos reales.

Data Protection de producción debe guardarse en almacenamiento duradero compartido por las réplicas y protegido mediante certificado/KMS o almacenamiento cifrado y controlado. El volumen de Compose es para desarrollo/validación local; sus claves no deben versionarse. Mantener las claves también es necesario para cookies de Identity y tokens vigentes. No se cambió el identificador de aplicación.

## Migración y validación

La migración `Delivery4PersistentNotifications` agrega `EmailOutbox`, índices de deduplicación/reclamación y `Businesses.ReminderLeadHours` con valor inicial de 24. Es aditiva y no cambia migraciones previas; no se aplica automáticamente al iniciar. Ejecutar explícitamente en desarrollo:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=(localdb)\MSSQLLocalDB;Database=BellezaDelivery1Validation;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True'
dotnet ef database update --project src/Citalu.Infrastructure --startup-project src/Citalu.Web
```

En esta rama se aplicó sobre `BellezaDelivery1Validation`, SQL Server 2019 Express LocalDB `15.0.4382.1`. Se comprobaron las seis migraciones en `__EFMigrationsHistory` y las columnas de `EmailOutbox`. En navegador, con datos sintéticos, se completó registro, inicio de sesión de cuenta aún sin confirmar (con aviso y reenvío explícito), creación de negocio/servicio/horario y reserva pública para el 25 de octubre; la confirmación mostró servicio, precio, duración, fecha, hora y zona. SQL Server registró una cita y la outbox; el capturador local procesó la confirmación de cuenta y del cliente y dejó el recordatorio futuro pendiente. El aviso al propietario queda condicionado a confirmar el correo. No se contactó SMTP. Se inspeccionó la página pública en escritorio y a 390×844; no se observó desbordamiento horizontal. Se eliminaron las filas sintéticas y capturas al concluir, conservando el esquema y las migraciones.

`dotnet build Citalu.sln` y `dotnet test Citalu.sln` se ejecutaron; la suite actual pasó 11 pruebas unitarias.

En Docker Compose, la ruta de claves es un volumen separado. SMTP se configura por variables `EMAIL_SMTP_*` y la URL pública por `APPLICATION_PUBLIC_BASE_URL`; no pongas valores reales en `.env.example` o `appsettings.json`.

## Pendiente explícito

- Aún deben ejecutarse pruebas de integración contra SQL Server real específicas de esta entrega: reserva confirmada cuando falla SMTP, rollback sin outbox, recuperación del worker/reintentos tras reinicio, múltiples workers, invalidación de recordatorios, confirmación/restablecimiento de cuenta, privacidad de recuperación de enlaces y aislamiento. La pasada manual sí verificó reserva, outbox y capturas, pero no reemplaza esos casos automatizados ni prueba por sí misma concurrencia multiinstancia.
- En navegador se verificaron registro/login, negocio, servicio, horarios, página pública y reserva; siguen pendientes los recorridos de confirmación de correo, recuperación de contraseña y recuperación de enlace, además de validar tratamiento del fallo SMTP y estado de correo en la agenda con citas reales. Esta entrega no está desplegada ni se considera lista para producción. Pagos, lista de espera y WhatsApp siguen fuera de alcance.
