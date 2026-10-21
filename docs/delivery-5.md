# Entrega 5: prueba, suscripción y pagos manuales

## Modelo y reglas

- Una suscripción por negocio conserva inicio/fin de prueba, plan, estado `Trial`/`Paid`/`Expired` y el indicador `RenewalCancelled`. `Businesses.TrialStartedAtUtc` y `TrialEndsAtUtc` se mantienen como datos históricos; la migración copia esos valores exactos a `BusinessSubscriptions` y no reinicia ni amplía pruebas.
- `SubscriptionPeriods` conserva cada intervalo de cobertura; `SubscriptionPayments` conserva los pagos manuales y registros compensatorios; `SubscriptionAudits` conserva las operaciones y sus responsables. No hay endpoints de borrado. Una corrección agrega un reverso, revoca explícitamente el periodo asociado y conserva ambas filas originales.
- La referencia se normaliza con `Trim` y es única por suscripción; un reintento con la misma referencia e importe/moneda devuelve el pago ya existente, aunque el identificador de solicitud haya cambiado. La misma referencia con otro importe o moneda se rechaza. Las solicitudes simultáneas se serializan con bloqueo de fila de SQL Server; referencias distintas conceden periodos consecutivos.
- La regla de acceso se evalúa en cada petición mediante `TimeProvider`: prueba `[inicio, fin)`, cobertura pagada `[inicio, fin)` o periodo de gracia configurable. La configuración inicial `Subscriptions:GraceDays=0` desactiva la gracia. No se requiere una tarea programada para bloquear reservas.
- Un mes significa `DateTimeOffset.AddMonths(1)` en UTC, con las reglas de calendario .NET para fin de mes y años bisiestos. Un pago anticipado inicia al final de la prueba/cobertura vigente; una vez vencido, inicia cuando el administrador lo registra. No se acreditan días anteriores al registro. `PaidAtUtc` documenta la fecha declarada del pago; no permite retrotraer cobertura.
- Precio inicial configurable: `Subscriptions:MonthlyPrice=299.00`, `Currency=MXN`, `PlanName=Mensual`. El importe es decimal con dos posiciones; cada pago y periodo guardan su moneda. `TaxDisplay` comunica la configuración sin fijar una decisión fiscal. `PaymentInstructions` comienza vacío: configura instrucciones reales fuera de Git antes de mostrarlas.
- Cancelar renovación solo deshabilita renovaciones futuras; no revoca prueba ni periodo ya concedido. No hay cobro automático ni renovaciones automáticas en esta entrega. Solicitar/reportar pago no cambia el acceso.

## Autorización y acceso vencido

- `PlatformAdmin` es el único rol autorizado para registrar pagos y conceder meses. El alta pública nunca asigna roles.
- Crear el primer administrador requiere acceso local/operativo al proyecto y base ya migrada. Ejecuta `dotnet run --project src/Citalu.Web -- --create-platform-admin`, proporciona un correo y una contraseña en entrada oculta. El comando toma un `sp_getapplock` transaccional y se deshabilita cuando ya existe un administrador; no hay contraseña predefinida ni ruta web para elevar privilegios.
- En prueba, cobertura o gracia, las reservas públicas y manuales pasan la validación. Al vencer, la aplicación rechaza nuevas reservas y altas manuales en el servidor, aunque se envíe un POST directo. La página `/b/{slug}` conserva perfil, servicios y contacto e informa que las reservas están temporalmente deshabilitadas.
- Tras el vencimiento permanecen disponibles agenda, consulta, exportación CSV, cancelación y reprogramación de citas existentes. No se borran ni cancelan citas por falta de pago.
- `SubscriptionAccessService` es la regla central para `/citas/disponibilidad`, `/citas/reservar/{slug}`, el alta manual y el render público. Los endpoints de gestión de una cita existente no requieren acceso de reservas.

## Notificaciones de vencimiento

El hosted service revisa cada hora y agrega avisos de 7 y 1 días a la outbox SQL existente. La clave deduplicada incluye negocio, vencimiento exacto y anticipación. Al registrar o revertir un pago se invalidan, dentro de la misma transacción, los avisos todavía pendientes/reclamados. Antes de enviar, el worker compara el vencimiento del mensaje con el periodo vigente. La regla de bloqueo de nuevas reservas se calcula en la petición y funciona aunque el correo falle o el worker esté apagado.

Hay una ventana inevitable después de que un transporte ya aceptó un mensaje: un pago simultáneo no puede retirar un correo entregado. La outbox evita trabajos obsoletos antes del envío, pero SMTP genérico no ofrece revocación transaccional.

## Migración y pruebas locales

La migración `20261020182701_Delivery5SubscriptionsManualPayments` crea suscripción, cobertura, pagos, reversos y auditoría, y copia las fechas previas desde `Businesses`. La migración aditiva `20261020191730_Delivery5UniqueManualPaymentReference` impone la unicidad de la referencia por suscripción. No se modifica ninguna migración anterior. Ambas migraciones se aplicaron explícitamente a `BellezaDelivery1Validation` en SQL Server 2019 Express LocalDB `15.0.4382.1`; el historial tiene nueve migraciones y el índice único está presente.

Para validar la migración de Entrega 5 sobre datos de la versión previa, se creó la base temporal aislada `CitaluDelivery5LegacyVerify`, se migró hasta `20261020172726_Delivery4OutboxLeaseFencing`, se insertó un negocio sintético con prueba `[2025-02-28T09:17:23Z, 2025-03-30T09:17:23Z)` y después se aplicaron las dos migraciones de Entrega 5. SQL Server devolvió ambas fechas idénticas en `Businesses` y `BusinessSubscriptions` (`EXACT MATCH`) y el negocio quedó presente.

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=(localdb)\MSSQLLocalDB;Database=BellezaDelivery1Validation;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True'
dotnet ef database update --project src/Citalu.Infrastructure --startup-project src/Citalu.Web
sqlcmd -S '(localdb)\MSSQLLocalDB' -d master -E -Q "IF DB_ID('CitaluDelivery5Integration') IS NULL CREATE DATABASE CitaluDelivery5Integration;"
$env:CITALU_SQLSERVER_TEST_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=CitaluDelivery5Integration;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True'
dotnet build Citalu.sln
dotnet test Citalu.sln
```

Para reproducir el orden y configuración de CI en Release:

```powershell
dotnet restore Citalu.sln
dotnet build Citalu.sln --configuration Release --no-restore
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/Citalu.Infrastructure --startup-project src/Citalu.Web
dotnet test Citalu.sln --configuration Release --no-build
```

La integración usa SQL Server real, un reloj fijo y una outbox real. Comprueba límite exacto de prueba, pago anticipado, pago tardío, mes calendario, reintento idempotente, rollback y reversión del aviso en conjunto, y aislamiento. `Delivery4HttpSecurityTests` usa TestServer+SQL Server para el límite HTTP (seis solicitudes de recuperación en el proceso) y verifica que dirección, contraseña y token de restablecimiento no aparecen en los logs. Las reglas de CI inician SQL Server 2022 y ejecutan `dotnet test Citalu.sln`.

Los límites actuales de recuperación y reserva son particionados por IP en memoria del proceso. No coordinan varias réplicas y dependen de que la IP cliente se determine correctamente tras un proxy. Un despliegue multiinstancia requiere un almacén de rate limit distribuido y proxies de confianza configurados.

## Verificación de la rama

Espacio al iniciar esta validación: C: tenía 9.2 GB libres; el código y artefactos de `src` ocupaban ~0.24 GB y los de `tests` ~0.09 GB. No fue necesario limpiar datos ni mover la compilación a otra unidad.

- **Aprobado:** `dotnet build Citalu.sln --configuration Release`, SDK 10.0.402, 0 advertencias y 0 errores.
- **Aprobado:** `dotnet ef migrations has-pending-model-changes --project src/Citalu.Infrastructure --startup-project src/Citalu.Web`; informa que no hay cambios pendientes. Se usa `ApplicationDbContextFactory`, proveedor `UseSqlServer`, EF CLI 10.0.13 y fallback LocalDB de diseño. No necesita conexión a la base.
- **Aprobado:** `dotnet test Citalu.sln --configuration Release --no-build` con `CITALU_SQLSERVER_TEST_CONNECTION` apuntando a la base LocalDB temporal aislada `CitaluDelivery5Verify3`: 11 unitarias y 20 pruebas TestServer/SQL Server aprobadas. Incluye todos los casos SQL de Entregas 3/4, límite HTTP de recuperación y comprobación de logs sensibles; prueba de límite exacto de prueba/cobertura, meses calendario, pagos anticipados/tardíos, retry por referencia incluso con clave nueva, dos pagos simultáneos serializados, rollback de pago/cobertura/auditoría/outbox, avisos obsoletos incluso reclamados, acceso administrador, aislamiento entre negocios, bloqueo de reservas públicas y manuales tras vencer, consulta/exportación/cancelación de citas existentes y reactivación tras pago. SQL Server local fue 2019 Express `15.0.4382.1`; CI usa SQL Server 2022.
- **Aprobado:** las migraciones se aplicaron en desarrollo y en la base sintética previa a Entrega 5; fechas originales de prueba se conservaron exactamente. No se borró la base de desarrollo.
- **Aprobado:** recorrido en navegador con el capturador local; se registró pago administrativo, se reactivó la reserva y se revisaron paneles de propietario/administrador. El perfil público deshabilitado se revisó en móvil 390×844 y escritorio.
- **Fallo inicial reproducido y corregido:** las corridas anteriores `39478549241` y `39478773505` fallaron en `Check EF migration model`. La descarga de logs devuelve HTTP 403 por permisos del repositorio. Localmente, con un `DOTNET_CLI_HOME` limpio, se reprodujo que el comando exacto falla porque el manifiesto contiene `dotnet-ef` 10.0.13 pero la herramienta no está restaurada: `Ejecute "dotnet tool restore" para que esté disponible el comando "dotnet-ef"`. Tras `dotnet tool restore`, `dotnet ef --version` informa 10.0.13 y el comando exacto pasa. Se añadió el paso `Restore local .NET tools` antes del chequeo EF; el control sigue habilitado.
- **Aprobado en GitHub Actions:** la corrida `39486221175` del commit `19b434a` terminó `success`. Pasaron Start SQL Server, Restore, Build, Restore local .NET tools, Check EF migration model, Test y Stop SQL Server. Así que CI completa sí pasó para los cambios de código y workflow publicados.
- **Pendiente operativo:** SMTP real. Se conserva el capturador local y no se solicitaron ni guardaron secretos.

Esta entrega no está lista para producción. No incluye pasarela, cobros automáticos, lista de espera ni nuevas funciones comerciales.
