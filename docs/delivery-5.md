# Entrega 5: prueba, suscripción y pagos manuales

## Modelo y reglas

- Una suscripción por negocio conserva inicio/fin de prueba, plan, estado `Trial`/`Paid`/`Expired` y el indicador `RenewalCancelled`. `Businesses.TrialStartedAtUtc` y `TrialEndsAtUtc` se mantienen como datos históricos; la migración copia esos valores exactos a `BusinessSubscriptions` y no reinicia ni amplía pruebas.
- `SubscriptionPeriods` conserva cada intervalo de cobertura; `SubscriptionPayments` conserva los pagos manuales y registros compensatorios; `SubscriptionAudits` conserva las operaciones y sus responsables. No hay endpoints de borrado. Una corrección agrega un reverso, revoca explícitamente el periodo asociado y conserva ambas filas originales.
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

La migración `20261020182701_Delivery5SubscriptionsManualPayments` crea suscripción, cobertura, pagos, reversos y auditoría, y copia las fechas previas desde `Businesses`. Es aditiva y no modifica migraciones anteriores. Se aplicó explícitamente en desarrollo con SQL Server 2019 Express LocalDB `15.0.4382.1` sobre `BellezaDelivery1Validation`; el historial conserva la migración y el esquema existente. En esa base de validación había cero negocios antes de aplicar Entrega 5, por lo que la rama `INSERT ... SELECT` de preservación se revisó en la migración pero no pudo compararse con filas heredadas reales en esa instancia.

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=(localdb)\MSSQLLocalDB;Database=BellezaDelivery1Validation;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True'
dotnet ef database update --project src/Citalu.Infrastructure --startup-project src/Citalu.Web
$env:CITALU_SQLSERVER_TEST_CONNECTION = $env:ConnectionStrings__DefaultConnection
dotnet build Citalu.sln
dotnet test Citalu.sln
```

La integración usa SQL Server real, un reloj fijo y una outbox real. Comprueba límite exacto de prueba, pago anticipado, pago tardío, mes calendario, reintento idempotente, rollback y reversión del aviso en conjunto, y aislamiento. `Delivery4HttpSecurityTests` usa TestServer+SQL Server para el límite HTTP (seis solicitudes de recuperación en el proceso) y verifica que dirección, contraseña y token de restablecimiento no aparecen en los logs. Las reglas de CI inician SQL Server 2022 y ejecutan `dotnet test Citalu.sln`.

Los límites actuales de recuperación y reserva son particionados por IP en memoria del proceso. No coordinan varias réplicas y dependen de que la IP cliente se determine correctamente tras un proxy. Un despliegue multiinstancia requiere un almacén de rate limit distribuido y proxies de confianza configurados.

## Verificación de la rama

- Build ejecutado con `dotnet build Citalu.sln --no-restore -p:CopyLocalLockFileAssemblies=false -p:PreserveCompilationReferences=false`: compiló todos los proyectos, sin advertencias ni errores. El indicador se usó porque el disco C quedó sin espacio al copiar ensamblados locales; no cambia las opciones versionadas de build. Después, `dotnet build src/Citalu.Web/Citalu.Web.csproj --no-restore` pasó sin advertencias ni errores tras corregir la fecha local del registro manual.
- SQL Server aplicó la migración. En la base local había cero negocios y quedaron cero suscripciones iniciales; no se borraron filas. Por eso no se afirma que se haya comprobado una prueba heredada con negocio preexistente.
- El chequeo local `dotnet ef migrations has-pending-model-changes` pasó después de generar la migración.
- Se observó la corrida real de GitHub Actions `39473355247` del commit publicado de entrega 4: build pasó, el paso `Check EF migration model` falló y las pruebas fueron omitidas. La API pública permite ver el paso fallido, pero descarga de logs devuelve HTTP 403 por requerir permisos de administrador del repositorio; no se puede determinar la causa ni declarar CI aprobado en esta validación.
- SMTP real sigue pendiente. Se conserva el capturador local; no se solicitaron ni guardaron secretos de SMTP.
- Recorrido en navegador completado con datos sintéticos y el capturador local: el panel del propietario mostró prueba vigente y luego cobertura pagada; el administrador registró un pago con referencia y el acceso vencido se reactivó; la página pública volvió a habilitar reservas. Antes del pago, tanto la página pública como el alta manual del propietario mostraron el bloqueo; el perfil público conservó información y contacto. En viewport móvil de 390×844 no hubo desbordamiento horizontal. Se comprobó el historial de cobertura/pago y el estado de acceso en pantalla.
- Los datos sintéticos de navegador y el administrador de prueba se eliminaron de SQL Server al terminar el recorrido; la migración quedó aplicada. La captura de correo sintética se eliminó. Las claves locales de Data Protection se conservaron y siguen ignoradas por Git.
- `dotnet test Citalu.sln --no-restore` inició y pasó las 11 pruebas unitarias de Application. La construcción del host SQL Server/TestServer falló al copiar ensamblados por falta de espacio en C: (aprox. 60 MB libres al iniciar); el ensamblado de integración quedó incompleto y no se ejecutó ninguna prueba de esa categoría. No se afirma que las pruebas de integración hayan pasado.
- La corrida disponible de GitHub Actions sigue siendo la de Entrega 4: build pasó, `Check EF migration model` falló y las pruebas se omitieron. Los logs del paso devuelven 403 por permisos del repositorio. La rama Entrega 5 aún no ha producido una corrida verificable, por lo que CI de esta entrega queda pendiente.
- SMTP real sigue pendiente. Se conserva el capturador local; no se solicitaron ni guardaron secretos de SMTP.

Esta entrega no está lista para producción. No incluye pasarela, cobros automáticos, lista de espera ni nuevas funciones comerciales.
