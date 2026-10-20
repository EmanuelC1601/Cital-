# Persistencia local

## Motor y versiones

La aplicación usa `Microsoft.EntityFrameworkCore.SqlServer` 10.0.13 con EF Core 10 y ASP.NET Core sobre .NET 10. El proveedor oficial documenta soporte para SQL Server 2019 o posterior. La configuración Docker de este proyecto usa SQL Server 2022; la verificación de la entrega 1 se realizó contra SQL Server 2019 Express LocalDB.

## Windows LocalDB

LocalDB usa autenticación integrada de Windows, por lo que no requiere guardar una contraseña en el proyecto. En PowerShell, inicia y revisa la instancia:

```powershell
sqllocaldb info MSSQLLocalDB
sqllocaldb start MSSQLLocalDB
sqlcmd -S '(localdb)\MSSQLLocalDB' -E -Q "SELECT @@VERSION; SELECT name FROM sys.databases ORDER BY name;"
```

La base `Citalu` se crea solo cuando aplicas la primera migración si aún no existe. Configura la conexión en la sesión actual de PowerShell:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=(localdb)\MSSQLLocalDB;Database=Citalu;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True'
dotnet ef database update --project src/Citalu.Infrastructure --startup-project src/Citalu.Web
dotnet run --project src/Citalu.Web
```

La variable de entorno no se guarda en Git. Para terminar la sesión, ciérrala o ejecuta `Remove-Item Env:ConnectionStrings__DefaultConnection`.

## SQL Server con Docker

1. Copia `.env.example` a `.env` y reemplaza su valor de ejemplo por una contraseña local fuerte.
2. Inicia solo la base de datos: `docker compose up -d sqlserver`.
3. En PowerShell, carga la contraseña local y crea la cadena en memoria:

   ```powershell
   $env:MSSQL_SA_PASSWORD = (Get-Content .env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' } | Select-Object -First 1) -replace '^MSSQL_SA_PASSWORD=', ''
   $env:ConnectionStrings__DefaultConnection = "Server=localhost,1433;Database=Citalu;User Id=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
   dotnet ef database update --project src/Citalu.Infrastructure --startup-project src/Citalu.Web
   dotnet run --project src/Citalu.Web
   ```

`.env` está ignorado por Git. No escribas la contraseña en `appsettings*.json`, scripts versionados, commits ni logs.

## Esquema de la entrega 1

La migración `InitialIdentityAndBusiness` crea las tablas de ASP.NET Identity, `Businesses` y `__EFMigrationsHistory`. `Businesses` incluye el propietario, slug, zona horaria e inicio y fin de la prueba. Los índices únicos de slug y de propietario se aplican en SQL Server.

En esta entrega todavía no existe una tabla `Subscriptions`: la prueba se representa con fechas en `Businesses`. La entidad de suscripción, su estado de acceso, los pagos y la auditoría se incorporarán en la entrega correspondiente.

No se aplica ninguna migración al arrancar la aplicación. Revisa y aplica cada migración explícitamente en desarrollo y como un paso controlado de despliegue.

## Verificación de la entrega 1

El 20 de octubre de 2026 se aplicó la migración a una base nueva `BellezaDelivery1Validation` en LocalDB, SQL Server 2019 Express (15.0.4382.1). Se comprobaron tablas de Identity, `Businesses` y el historial de migraciones; índices únicos para `Slug` y `OwnerUserId`; registro, creación, cierre de sesión, nuevo inicio y lectura persistente del negocio; y dos propietarios con el mismo nombre de negocio, que obtuvieron slugs diferentes. La prueba persistida duró exactamente 30 días. Al autenticar al segundo propietario y enviar el identificador del negocio del primero como parámetro, el panel siguió mostrando solo el negocio propio.

Los usuarios y negocios de prueba eran sintéticos y se eliminaron al terminar. Se conservó la base aislada con su esquema y registro de migración. No existe una tabla de suscripciones en esta entrega.
