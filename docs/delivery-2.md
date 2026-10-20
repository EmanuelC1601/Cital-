# Entrega 2: servicios, horarios y página pública

## Alcance

- Servicios por negocio con creación, edición, activación, desactivación y eliminación. El importe se guarda en MXN con dos decimales; la duración admite de 5 a 480 minutos y el tiempo adicional de 0 a 180 minutos.
- Horarios semanales con ventanas que no se traslapan, descansos contenidos dentro de una ventana, excepciones de fecha (cerrado o un horario especial) y periodos bloqueados. Los intervalos duran al menos cinco minutos y las fechas se interpretan en la zona horaria guardada por el negocio.
- Perfil público editable con zona horaria, ubicación y medios de contacto que el propietario decide publicar, colores, logo y portada. Las imágenes cargadas se validan por firma de archivo y se limitan a 3 MB; se almacenan con nombre aleatorio bajo src/Citalu.Web/wwwroot/uploads/{businessId}. Ese directorio está ignorado por Git.
- Página anónima /b/{slug} con datos públicos explícitos y únicamente servicios activos. El correo de identidad del propietario, su identificador y los motivos privados de bloqueos no forman parte del modelo público.
- Enlace para copiar y QR PNG descargable desde /b/{slug}/qr.png.

La interfaz incluye administración y visualización adaptable a teléfono. Los horarios todavía son reglas de calendario: no se publican espacios reservables y no se crean citas. El motor de disponibilidad, la prevención de colisiones y las reservas pertenecen a la entrega 3.

## Aislamiento

Cada acción autenticada obtiene el negocio a partir del usuario de Identity. Las lecturas, actualizaciones y eliminaciones de servicios, horarios, descansos, excepciones y bloqueos filtran también por BusinessId. La página anónima consulta por slug y proyecta solo los campos públicos requeridos. SQL Server conserva índices únicos para slug y propietario; una excepción de fecha también es única por negocio y fecha.

## Persistencia y migraciones

El proveedor continúa siendo Microsoft.EntityFrameworkCore.SqlServer 10.0.13 con .NET/EF Core 10. La instancia probada es SQL Server 2019 Express LocalDB. Las migraciones son aditivas respecto de la entrega 1 y no se aplican al iniciar el sitio:

~~~powershell
$env:ConnectionStrings__DefaultConnection = 'Server=(localdb)\MSSQLLocalDB;Database=Citalu;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True'
dotnet ef database update --project src/Citalu.Infrastructure --startup-project src/Citalu.Web
dotnet run --project src/Citalu.Web
~~~

La cadena se configura en una variable de entorno local; no la pongas en appsettings.json, scripts versionados ni commits. La configuración general de LocalDB y Docker está en persistencia.md.

## Verificación local

El 20 de octubre de 2026 se aplicaron Delivery2ServicesAvailabilityAndPublicProfile y Delivery2PublicProfileColorDefaults a BellezaDelivery1Validation, conservando el historial y las tablas anteriores. Contra LocalDB se comprobó el registro de dos propietarios, la creación de negocios con nombres iguales y slugs diferentes, y una prueba de 30 días para cada uno. Mediante la aplicación se creó un servicio a MXN $650.50, se editó, desactivó y reactivó; se guardaron un horario semanal, un descanso, una fecha especial y un bloqueo. La página anónima mostró logo, descripción, contacto y solo el servicio activo; al desactivarlo, dejó de mostrarlo. Un POST autenticado como el segundo propietario contra el ID de servicio del primero devolvió 404. El QR respondió con PNG válido. Se revisaron capturas en viewport de 1440×1000 y 390×844.

La base de validación empezó sin usuarios ni negocios. Las dos cuentas y los datos sintéticos creados para las pruebas se retiraron al terminar; se conservaron el esquema y las tres migraciones. No se modificó información preexistente.

La tabla Subscriptions no existe todavía en el modelo; la prueba sigue representada por sus fechas en Businesses. Pagos y estados de suscripción quedan fuera de esta entrega.

## Límites locales

Los archivos cargados se guardan en disco local y no se incluyen en Git. En despliegues futuros habrá que configurar almacenamiento persistente antes de publicar; no se despliega en esta entrega.
