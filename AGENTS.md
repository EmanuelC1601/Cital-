# Instrucciones del repositorio

## Marca y nombres

- El nombre comercial es **Citalú**, siempre con acento en interfaz, títulos, mensajes y correos dirigidos a usuarios.
- Los nombres técnicos usan **Citalu**, sin acento: solución `Citalu.sln`, carpetas/proyectos/ensamblados y namespaces.
- Las nuevas ramas usan el prefijo `Ema/` (por ejemplo, `Ema/delivery-4-persistent-notifications`). Conserva las ramas ya publicadas y sus historiales; no las renombres ni las borres.

## Persistencia y migraciones

- Mantén SQL Server y aplica migraciones explícitamente en desarrollo; no las ejecutes automáticamente al arrancar.
- No renombres tablas ni alteres operaciones `Up`/`Down`, identificadores o el contenido histórico de migraciones ya aplicadas por cambios de marca. El snapshot vigente debe reflejar los namespaces `Citalu` para que las migraciones futuras se generen desde el modelo actual.
- Los namespaces y tipos antiguos que aparecen dentro de diseñadores de migraciones históricas son metadatos congelados. No los “corrijas” retroactivamente; crea migraciones nuevas bajo `Citalu.Infrastructure.Persistence.Migrations`.
- El propósito `Belleza.AppointmentManagement.v1` es un identificador criptográfico legado. Conserva la lectura de valores cifrados con ese propósito; cifra nuevos valores con el propósito `Citalu.AppointmentManagement.v1`.
- No agregues credenciales, archivos de base de datos, claves de Data Protection, resultados de compilación ni datos locales al control de versiones.

## Comprobaciones

- Usa `dotnet build Citalu.sln` y `dotnet test Citalu.sln` después de cambios de código.
- Para cambios de persistencia, verifica contra SQL Server real; EF InMemory no demuestra concurrencia ni persistencia relacional.
