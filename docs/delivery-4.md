# Entrega 4: notificaciones persistentes y acceso

## Alcance implementado

- `IEmailSender` vive en Application. Infraestructura aporta capturador local (solo Development) y transporte SMTP configurable. Ninguna credencial está en el repositorio; en desarrollo nunca se llama a SMTP.
- Confirmación de reserva, avisos de cancelación/reprogramación, avisos al propietario con vínculo autenticado, recordatorios programables de 0–168 horas (0 desactiva), confirmación de cuenta, recuperación/restablecimiento de contraseña y recuperación del enlace privado de una reserva.
- Registro de cuentas y su trabajo de confirmación comparten transacción SQL. Los mensajes de citas se agregan al DbContext/transacción en las escrituras de agenda ya protegidas con `UPDLOCK, HOLDLOCK`. Si el SMTP falla, la transacción de cita no se revierte; el worker procesa el outbox aparte.
- Carga del correo (destinatario, asunto, cuerpo y vínculos/tokens) se cifra con Data Protection, propósito `Citalu.EmailOutbox.Payload.v1`; no se registran contenidos ni tokens. Los mensajes enviados purgan destinatario, asunto y carga. Un barrido del worker purga cargas fallidas después de 30 días y conserva un fallo resumido solo por tipo de excepción.
- El worker reclama con transacción SQL y `UPDLOCK, READPAST, ROWLOCK`. Cada reclamación lleva un `LeaseId` de fencing: solo ese propietario puede renovar o finalizar el trabajo. El lease inicial dura 2 minutos y se renueva cada 30 segundos mientras el transporte está enviando; si otra instancia lo reclama, la renovación falla y se cancela el envío cuando el transporte respeta cancelación. Los reintentos son exponenciales (15 s × 2^intento, hasta una hora), con límite de 8 intentos y recuperación al reiniciar o vencer el lease. El propietario puede reencolar desde su agenda un fallo de notificación de cita mientras la carga cifrada no haya vencido; los fallos de correo de cuenta requieren una solicitud nueva. Los cambios de cita invalidan trabajos pendientes o reclamados y borran su carga cifrada; justo antes del envío también se comprueba estado y versión. Si el transporte ya aceptó/entregó el mensaje antes de que se procese el cambio de cita, el correo no se puede retirar. Un fallo después de entregar SMTP y antes de registrar `Sent` también puede duplicar el correo; SMTP no ofrece idempotencia portable. El identificador de idempotencia se entrega a `IEmailSender` para proveedores que sí la soporten.
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

En esta rama se aplicó sobre `BellezaDelivery1Validation`, SQL Server 2019 Express LocalDB `15.0.4382.1`. La migración aditiva `Delivery4OutboxLeaseFencing` agrega `EmailOutbox.LeaseId`; no modifica migraciones anteriores. Quedaron aplicadas siete migraciones en `__EFMigrationsHistory`.

El proyecto `tests/Citalu.Integration.Tests` ejecuta pruebas sobre SQL Server real con un transporte SMTP controlable. Configura `CITALU_SQLSERVER_TEST_CONNECTION` con una base de desarrollo/pruebas aislada; el fixture aplica las migraciones explícitamente, usa claves Data Protection temporales y escribe filas con prefijos únicos que elimina al terminar. No borra la base configurada. Comando local:

```powershell
$env:CITALU_SQLSERVER_TEST_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=CitaluDelivery4Integration;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True'
dotnet test tests/Citalu.Integration.Tests/Citalu.Integration.Tests.csproj
```

La primera ejecución necesita permiso para crear/aplicar el esquema. No apuntes el comando a una base compartida o de producción. CI crea SQL Server 2022 en un contenedor con contraseña aleatoria efímera, enmascarada por Actions, y ejecuta la misma suite.

Las once pruebas de integración cubren fallo SMTP con cita confirmada, reintento y tope de ocho intentos, rollback de cita/outbox, reclamación de dos workers, recuperación de lease vencido, fencing del worker anterior, renovación durante un envío real de más de dos minutos, invalidación de recordatorios pendientes y reclamados, aislamiento de lectura y reintento de correo entre negocios, recuperación de enlace con correo asociado y rotación de tokens vencidos, además de confirmación y restablecimiento Identity con rechazo de tokens inválidos/reutilizados. Los envíos se simulan con un transporte controlable; no se contactó SMTP real.

Resultado final en SQL Server 2019 Express LocalDB: `dotnet build Citalu.sln` pasó sin advertencias ni errores; `dotnet ef migrations has-pending-model-changes` indicó que no hay cambios; `dotnet test Citalu.sln` pasó 22/22 (11 unitarias y 11 de integración). La prueba que mantiene el transporte bloqueado pasó a los 2 min 7 s y comprobó que el lease siguió renovándose. Una pasada inicial sobre esta base de desarrollo compartida hizo que dos pruebas reclamaran correos pendientes de los datos de navegador; el arnés se acotó a claves de prueba únicas y la suite completa volvió a pasar. Las filas de pruebas se eliminan al terminar.

En navegador local se recorrieron registro, confirmación de correo, solicitud de recuperación de contraseña, restablecimiento e inicio con la contraseña nueva; reserva pública con resumen y correo capturado; apertura del enlace privado, recuperación genérica del enlace, reprogramación, rechazo del enlace anterior, cancelación y agenda del propietario con estado de correo. La ficha pública se abrió en escritorio y a 390×844; en móvil `scrollWidth` coincidió con el ancho de viewport y no apareció el correo privado del propietario. Los datos sintéticos del recorrido y las capturas se retiraron; las siete migraciones se conservaron.

Build y pruebas deben ejecutarse después de cada cambio:

```powershell
dotnet build Citalu.sln
$env:CITALU_SQLSERVER_TEST_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=CitaluDelivery4Integration;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True'
dotnet test Citalu.sln
```

En Docker Compose, la ruta de claves es un volumen separado. SMTP se configura por variables `EMAIL_SMTP_*` y la URL pública por `APPLICATION_PUBLIC_BASE_URL`; no pongas valores reales en `.env.example` o `appsettings.json`.

## Pendiente explícito

- Pendiente ejecutar el envío contra un SMTP real: se usó el capturador local y fallos simulados, de modo que no se verificaron las credenciales ni el servidor remitente. El código de aplicación compartido por el usuario no se agregó a archivos ni a logs; para configurarlo localmente faltan proveedor/host SMTP, cuenta/remitente y usuario SMTP. No se envió correo a terceros.
- Los límites de tasa y la privacidad se revisaron en configuración/código y la respuesta genérica de recuperación se verificó en navegador; todavía no hay prueba automatizada HTTP de las 20 solicitudes por IP ni una prueba de logs con un proveedor de logging capturable. El límite es por proceso, no distribuido.
- El workflow de CI quedó configurado para iniciar SQL Server 2022 con una contraseña aleatoria efímera y ejecutar las pruebas; no se observó una ejecución de GitHub Actions dentro de esta validación.
- La ventana donde el transporte ya aceptó el correo no se puede cerrar con SMTP genérico; si el transporte no honra cancelación puede terminar el envío después de invalidar el trabajo, y una caída después de entregar antes de guardar `Sent` puede duplicarlo. Esta entrega no está desplegada ni se declara lista para producción. Pagos, lista de espera y WhatsApp siguen fuera de alcance.
