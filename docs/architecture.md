# Arquitectura inicial

La primera entrega usa cuatro proyectos para conservar una dirección clara de dependencias sin añadir buses ni repositorios genéricos.

```text
Web ───────► Application ───────► Domain
 │                 ▲
 └────► Infrastructure ──────────┘
```

Web contiene MVC, Identity y el arranque de dependencias. Application contiene contratos y casos de uso. Infrastructure implementa el almacenamiento SQL Server e Identity. Domain modela el negocio, su propietario, el slug, la zona horaria y la prueba.

## Modelo inicial

`AspNetUsers` identifica la cuenta y `Businesses` el negocio. El negocio conserva `OwnerUserId`, `Slug`, `TimeZoneId`, `TrialStartedAtUtc` y `TrialEndsAtUtc`. SQL Server impone unicidad tanto del propietario como del slug. La prueba comienza al crear el negocio y dura 30 días.

## Aislamiento de datos

Las vistas del panel no aceptan un tenant enviado por navegador. El controlador obtiene el identificador de propietario a partir del principal autenticado; el contrato de persistencia lo exige como filtro de lectura. En módulos futuros cada fila tenant-aware guardará `BusinessId`, las relaciones incluirán ese identificador y las escrituras validarán pertenencia. Esta primera entrega todavía no contiene datos de clientes ni citas.

## Migraciones

Genera una migración con `dotnet ef migrations add Nombre --project src/Belleza.Infrastructure --startup-project src/Belleza.Web`. Aplica cambios explícitamente con `dotnet ef database update ...`; no se llama a `Database.Migrate()` al arrancar.
