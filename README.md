# Belleza

Agenda y reservas para barberías, estéticas y profesionales de belleza. Las entregas 1–3 incluyen cuenta del propietario, negocio, servicios, horarios, página pública y reservas con protección de solapamientos en SQL Server.

## Requisitos

- .NET SDK/runtime de la línea 10.0.
- SQL Server 2019 o posterior; la instancia Docker incluida usa SQL Server 2022.
- En Windows también puedes usar SQL Server LocalDB 2019 o posterior.

.NET 10 es LTS y recibe soporte hasta el 14 de noviembre de 2028. La solución usa EF Core y su proveedor de SQL Server en 10.0.13 para mantenerse en la misma línea de soporte.

## Ejecutar en desarrollo

1. Copia `.env.example` a `.env` y sustituye el valor de `MSSQL_SA_PASSWORD` por una contraseña local fuerte.
2. Inicia SQL Server: `docker compose up -d sqlserver`.
3. En PowerShell, define la conexión para la aplicación:

   ```powershell
   $env:MSSQL_SA_PASSWORD = (Get-Content .env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' } | Select-Object -First 1) -replace '^MSSQL_SA_PASSWORD=', ''
   $env:ConnectionStrings__DefaultConnection = "Server=localhost,1433;Database=Belleza;User Id=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
   ```

4. Aplica la migración explícitamente: `dotnet tool restore` y `dotnet ef database update --project src/Belleza.Infrastructure --startup-project src/Belleza.Web`.
5. Ejecuta `dotnet run --project src/Belleza.Web` y abre la URL HTTPS indicada por ASP.NET Core.

La migración no se ejecuta al iniciar la aplicación. Para producción, aplícala como paso explícito del despliegue después de respaldar y verificar el entorno. Las instrucciones para LocalDB y SQL Server Docker están en [persistencia](docs/persistence.md).

La entrega 2 agrega catálogo, horarios y la página pública `/b/{slug}`. La [entrega 3](docs/delivery-3.md) agrega disponibilidad, reservas, agenda, enlaces privados y concurrencia SQL Server. Aún no están implementados notificaciones, pagos ni confirmación manual.

## Estructura

- `src/Belleza.Domain`: entidades y reglas centrales, sin dependencias de infraestructura.
- `src/Belleza.Application`: casos de uso y contratos.
- `src/Belleza.Infrastructure`: EF Core, Identity y SQL Server.
- `src/Belleza.Web`: MVC, vistas, cookies de autenticación y composición de servicios.
- `tests/Belleza.Application.Tests`: pruebas unitarias de reglas de aplicación y estados de cita.

## Arquitectura y aislamiento

El producto es un monolito modular con dependencias hacia adentro: Web e Infrastructure conocen Application; Application conoce Domain. Cada negocio tiene propietario, zona horaria, slug y agenda aislados. Las páginas del propietario derivan el negocio del usuario autenticado y todas las lecturas y escrituras se filtran por negocio.

Los filtros globales de EF no son la frontera de seguridad. La disponibilidad se calcula en hora local y las citas se guardan en UTC con instantáneas. Las escrituras que afectan la agenda serializan por fila del negocio con `UPDLOCK, HOLDLOCK` dentro de una transacción SQL Server. Se verificó concurrencia y aislamiento contra LocalDB; consulta [entrega 3](docs/delivery-3.md) para el alcance y resultados.

## Decisiones técnicas

- .NET 10 LTS, ASP.NET Core MVC, Identity y EF Core 10.0.13.
- SQL Server 2019 o posterior; la verificación se ejecutó en SQL Server 2019 Express LocalDB.
- Un negocio por propietario en MVP, con entidad separada del usuario de Identity.
- Prueba inicial de 30 días y zona horaria predeterminada `America/Mexico_City`.
- Disponibilidad y reservas se implementan en la entrega 3; pagos quedan pendientes.
- La prueba de 30 días está representada por `TrialStartedAtUtc` y `TrialEndsAtUtc` en `Businesses`. Todavía no hay una entidad ni tabla de suscripciones/pagos.
- Los correos, pagos, suscripciones y administración de plataforma quedan en el backlog.

## Seguridad conocida

El alta de cuenta crea una sesión inmediatamente. La confirmación de correo y la recuperación de cuenta requieren configurar el proveedor de correo y deben completarse antes de producción. La política de contraseña exige 12 caracteres, mayúscula, minúscula, número y símbolo; cinco intentos fallidos bloquean el acceso durante 15 minutos. La cookie es HttpOnly, SameSite Lax y Secure en producción. Los formularios MVC usan antiforgery.

La cadena de conexión y la contraseña local se leen desde variables de entorno; no guardes secretos en archivos versionados. El aviso de privacidad y los términos deben redactarse y revisarse antes del lanzamiento. El contenido de privacidad de la plantilla ASP.NET se considera provisional.

## Datos y operación

- Migraciones versionadas en `src/Belleza.Infrastructure/Persistence/Migrations`.
- SQL Server se conserva en el volumen Docker `sqlserver-data`.
- Haz respaldos periódicos y ensaya la restauración en una base distinta antes de despliegues relevantes.
- Retención y eliminación de datos se documentarán cuando exista el flujo de citas y exportación.
- CI restaura, compila y ejecuta pruebas en GitHub Actions.

## Backlog

**MVP:** catálogo y horarios; página pública; reservas y prevención de solapamientos SQL Server; reprogramación y cancelación. Pendiente: outbox de correo, pagos manuales, exportación, panel operativo, accesibilidad y pruebas de carga.

**Mejoras:** volver a reservar, lista de espera, más profesionales, automatización de pagos y canales de notificación.
