# Arquitectura inicial

La primera entrega usa cuatro proyectos para conservar una dirección clara de dependencias sin añadir buses ni repositorios genéricos.

```text
Web ───────► Application ───────► Domain
 │                 ▲
 └────► Infrastructure ──────────┘
```

Web contiene MVC, Identity y el arranque de dependencias. Application contiene contratos y casos de uso. Infrastructure implementa el almacenamiento SQL Server e Identity. Domain modela el negocio, el catálogo de servicios, los horarios locales, los bloqueos y la prueba.

## Modelo de negocio

`AspNetUsers` identifica la cuenta y `Businesses` el negocio. El negocio conserva `OwnerUserId`, `Slug`, `TimeZoneId`, `TrialStartedAtUtc` y `TrialEndsAtUtc`; SQL Server impone unicidad tanto del propietario como del slug. Servicios, horarios, descansos, excepciones y bloqueos guardan `BusinessId`. Un horario se expresa como tiempo local del negocio y no equivale a un espacio reservable.

## Aislamiento de datos

Las vistas del panel no aceptan un tenant enviado por navegador. Cada acción obtiene el negocio desde el principal autenticado y filtra las lecturas y escrituras por su `BusinessId`. La página anónima consulta por slug y devuelve una proyección con campos expresamente públicos; no expone correo o ID de Identity ni el motivo de un bloqueo.

## Migraciones

Genera migraciones con `dotnet ef migrations add Nombre --project src/Belleza.Infrastructure --startup-project src/Belleza.Web`. Aplica cambios explícitamente con `dotnet ef database update`; no se llama a `Database.Migrate()` al arrancar. El detalle de la entrega 2 y su verificación SQL Server están en [delivery-2](delivery-2.md).
