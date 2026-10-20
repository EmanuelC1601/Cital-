# Arquitectura inicial

La primera entrega usa cuatro proyectos para conservar una dirección clara de dependencias sin añadir buses ni repositorios genéricos.

```text
Web ───────► Application ───────► Domain
 │                 ▲
 └────► Infrastructure ──────────┘
```

Web contiene MVC, Identity y el arranque de dependencias. Application contiene contratos y casos de uso. Infrastructure implementa el almacenamiento SQL Server e Identity. Domain modela el negocio, el catálogo de servicios, los horarios locales, los bloqueos, las citas y el historial.

## Modelo de negocio

`AspNetUsers` identifica la cuenta y `Businesses` el negocio. El negocio conserva propietario, slug, zona horaria, prueba de 30 días y límites de reserva. Servicios, horarios, descansos, excepciones, bloqueos, citas e historial guardan `BusinessId`. Las reglas se expresan en hora local; las citas guardan inicios UTC y una instantánea del servicio acordado.

## Aislamiento de datos

Las vistas del panel no aceptan un tenant enviado por navegador. Cada acción obtiene el negocio desde el principal autenticado y filtra las lecturas y escrituras por su `BusinessId`. La página anónima consulta por slug y devuelve campos expresamente públicos; no expone datos de citas, correo de Identity ni motivos de bloqueo. Los tokens de gestión se guardan como hash y se limitan a una cita.

## Concurrencia de citas

Los endpoints que escriben agenda usan una transacción SQL Server y toman `UPDLOCK, HOLDLOCK` sobre la fila del negocio antes de comprobar disponibilidad. Esa fila es el mutex de escritura por tenant: serializa reserva pública, alta manual, reprogramación, cancelación y cambios de estado. La disponibilidad se valida otra vez dentro del bloqueo. Las escrituras de calendario que pueden quitar espacios adquieren el mismo bloqueo y rechazan conflictos con citas existentes. El detalle de reglas y pruebas está en [delivery-3](delivery-3.md).

## Migraciones

Genera migraciones con `dotnet ef migrations add Nombre --project src/Citalu.Infrastructure --startup-project src/Citalu.Web`. Aplica cambios explícitamente con `dotnet ef database update`; no se llama a `Database.Migrate()` al arrancar. El detalle de la entrega 2 y su verificación SQL Server están en [delivery-2](delivery-2.md).
