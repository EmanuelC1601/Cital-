# Belleza

Asistente digital de citas para barberías, estéticas y profesionales de belleza. El producto se está construyendo por entregas; esta primera entrega cubre la cuenta del propietario y el registro inicial de un negocio.

## Requisitos

- .NET SDK 10.0.13 o posterior de la línea 10.0.
- Docker Desktop con Docker Compose.
- SQL Server 2022 Developer para desarrollo (el contenedor incluido lo proporciona).

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

La migración no se ejecuta al iniciar la aplicación. Para producción, aplícala como paso explícito del despliegue después de respaldar y verificar el entorno.

## Estructura

- `src/Belleza.Domain`: entidades y reglas centrales, sin dependencias de infraestructura.
- `src/Belleza.Application`: casos de uso y contratos.
- `src/Belleza.Infrastructure`: EF Core, Identity y SQL Server.
- `src/Belleza.Web`: MVC, vistas, cookies de autenticación y composición de servicios.
- `tests/Belleza.Application.Tests`: pruebas unitarias de reglas de aplicación.

## Arquitectura y aislamiento

El producto es un monolito modular con dependencias hacia adentro: Web e Infrastructure conocen Application; Application conoce Domain. Cada negocio tiene identificador, propietario único, zona horaria explícita y slug único. En esta entrega el propietario se deriva del usuario autenticado y las lecturas del panel filtran por ese usuario. Los índices únicos de propietario y slug hacen cumplir las restricciones también en SQL Server.

Los filtros globales de EF no son la frontera de seguridad. Cada funcionalidad nueva debe obtener el negocio desde la identidad autorizada, filtrar explícitamente las lecturas y verificar pertenencia antes de modificar relaciones. Las pruebas actuales ejercitan reglas de alta; aún falta la batería de integración con SQL Server para aislamiento y solicitudes concurrentes.

## Decisiones técnicas

- .NET 10 LTS, ASP.NET Core MVC, Identity y EF Core 10.0.13.
- SQL Server 2022; una base compartida y claves de negocio en las entidades.
- Un negocio por propietario en MVP, con entidad separada del usuario de Identity.
- Prueba inicial de 30 días y zona horaria predeterminada `America/Mexico_City`.
- Los valores comerciales, pagos, disponibilidad y reservas todavía no se exponen en esta entrega.
- Los enlaces públicos, imágenes, emails, agenda, suscripciones de pago y administración de plataforma quedan en el backlog.

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

**MVP:** catálogo y horarios; página pública; reservas y prevención de solapamientos; reprogramación y cancelación; outbox de correo; prueba y pagos manuales; exportación; panel operativo; pruebas de integración SQL Server y accesibilidad.

**Mejoras:** volver a reservar, lista de espera, más profesionales, automatización de pagos y canales de notificación.
