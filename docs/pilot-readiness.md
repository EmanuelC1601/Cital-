# Preparación operativa para el piloto

## Estado y decisión del alojamiento

La opción indicada para evaluar es **MonsterASP.NET Free**, con una aplicación y una base gratuita. No se ha creado ni publicado un sitio allí.

Revisión de capacidades según documentación oficial consultada el 20-oct-2026:

| Capacidad | Evidencia del plan Free | Estado para Citalú |
| --- | --- | --- |
| .NET | .NET 10 está disponible. Citalu apunta a `net10.0` (SDK de desarrollo 10.0.402). | Compatible. |
| SQL Server | SQL Server 2025 real, una base de 1 GB. LocalDB no es una opción del servidor. | Compatible con EF Core. El 21-oct-2026 se verificó acceso SQL cifrado a `db74897.public.databaseasp.net:1433`; la base `db74897` estaba vacía de tablas de usuario y se aplicaron sus 9 migraciones. No se publicó la aplicación. |
| Worker persistente | El proceso IIS puede dormir tras 3 horas sin solicitudes. `AlwaysRunning` y las tareas programadas no están disponibles en Free. | **Bloqueo:** el `OutboxProcessor` y `SubscriptionExpiryNotifier` son `BackgroundService`; no se puede garantizar procesamiento oportuno/reanudación sin tráfico. No usar un ping público como sustituto de una garantía de trabajo. |
| Imágenes y claves | La web tiene 5 GB de almacenamiento de archivos. La guía de despliegue permite archivos en `wwwroot`; no encontramos garantía de que un directorio persistente escribible sobreviva la publicación ni permisos documentados para una ruta externa a `wwwroot`. | Pendiente de verificar desde el panel y sitio de prueba. Nunca subir claves al repositorio. Excluir datos de usuario del artefacto y conservarlos al publicar. |
| HTTPS | Certificado Let's Encrypt está disponible para el subdominio gratuito. | Compatible después de habilitarlo y comprobar redirección HTTPS en el sitio. |
| Secretos | Variables de entorno admitidas. | Compatible en principio; falta configurarlas en el panel y confirmar que no se exporten al artefacto/logs. |
| SMTP | El SMTP local del proveedor es Premium. La documentación permite SMTP externo en Free, sujeto al proveedor y al acceso de salida. | Condicionado a elegir/configurar un servicio SMTP externo. No hay envío real verificado. |
| Copias | La base gratuita no tiene copias diarias automáticas; capacidad máxima 1 GB. | **Bloqueo operativo:** antes de piloto se necesita procedimiento de exportación y restauración independiente probado, con copia de imágenes/claves cifrada. |
| Recursos | 256 MB RAM, 5 GB de archivos, base de 1 GB; el plan está descrito como pruebas/desarrollo/hobby, no producción. | No se ha hecho carga contra el alojamiento ni se conocen tamaños reales. |

Fuentes oficiales: [tecnologías soportadas](https://help.monsterasp.net/getting-started/first-steps/supported-technologies), [webs Free y Premium](https://help.monsterasp.net/getting-started/first-steps/create-first-website), [bases MSSQL Free/Premium](https://help.monsterasp.net/databases/mssql/create-database), [AlwaysRunning e inactividad](https://help.monsterasp.net/websites/configuration/always-running), [tareas programadas](https://help.monsterasp.net/websites/configuration/scheduled-tasks), [SMTP externo y local](https://help.monsterasp.net/emails/sending-from-applications/smtp-server), [HTTPS](https://help.monsterasp.net/domains-https/enable-https-letsencrypt) y [variables de entorno](https://help.monsterasp.net/websites/configuration/environment-variables).

La conclusión para el plan Free es que **no se debe iniciar un piloto con reservas reales** mientras no haya una ruta operativa fiable para el worker y copias restaurables. No se han cambiado planes, proveedores ni servicios externos. La aplicación sigue siendo de una sola instancia durante el piloto local; rate limits y coordinación viven en el proceso. No se habilitarán varias instancias hasta diseñar almacenamiento compartido, coordinación distribuida, claves compartidas y límites compartidos.

## Configuración de producción

El arranque en `Production` ahora falla con una lista de nombres de configuración faltantes (nunca sus valores) si no se define:

- `ConnectionStrings__DefaultConnection`: SQL Server remoto; LocalDB se rechaza.
- `Application__PublicBaseUrl`: URL canónica absoluta `https://...`, sin query/fragment ni dominio `.invalid`. Los correos y QR se construyen desde esta URL, no desde `Host`/`X-Forwarded-Host` de una petición.
- `AllowedHosts`: host explícito que incluya el de la URL canónica; no `*`.
- `DataProtection__KeysPath` y `Storage__UploadsPath`: rutas absolutas persistentes y escribibles por el proceso.
- `DataProtection__CertificatePath` y `DataProtection__CertificatePassword`: certificado PFX con clave privada para cifrar el anillo de Data Protection en reposo; conserva su clave privada y el anillo al respaldar.
- `Email__Smtp__Host`, `Email__Smtp__Port`, `Email__Smtp__From`, `Email__Smtp__EnableSsl=true`; si se usa usuario SMTP también debe haber contraseña.
- Si el proxy termina TLS, `ForwardedHeaders__Enabled=true` y `ForwardedHeaders__KnownProxies__0=<IP>` (y entradas adicionales). Solo se aceptan `X-Forwarded-For` y `X-Forwarded-Proto`; el host reenviado no se confía.

Configura variables desde el panel/gestor de secretos del entorno, nunca en `appsettings*.json`, `.env.example`, GitHub o este documento. No copies secretos al chat. En producción las cookies de Identity son `Secure`, `HttpOnly`, `SameSite=Lax`; HSTS y manejo de errores sin detalles internos están activos. Logs de consola usan JSON estructurado. `/health/live` y `/health/ready` devuelven solo estado; readiness comprueba conexión SQL y no informa versión, conexión, filas ni excepciones.

Para desarrollo local, `src/Citalu.Web` tiene un `UserSecretsId`. La conexión privada de esta máquina se guarda en .NET Secret Manager, fuera del repositorio y del paquete publicado. Configúrala en cada máquina con `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<cadena privada>" --project src/Citalu.Web/Citalu.Web.csproj`. La aplicación publicada **no recibe User Secrets**: introduce `ConnectionStrings__DefaultConnection` como variable privada en el panel antes de iniciar el sitio.

Para desarrollo local, `src/Citalu.Web` tiene un `UserSecretsId`. La conexión privada de esta máquina se guarda en .NET Secret Manager, fuera del repositorio y del paquete publicado. Configúrala en cada máquina con `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<cadena privada>" --project src/Citalu.Web/Citalu.Web.csproj`. La aplicación publicada **no recibe User Secrets**: introduce `ConnectionStrings__DefaultConnection` como variable privada en el panel antes de iniciar el sitio.

La aplicación persiste imágenes en `Storage:UploadsPath` y sirve esa carpeta bajo `/uploads`; si la ruta está vacía en desarrollo usa `wwwroot/uploads`. En producción exige una ruta persistente y cifra claves de Data Protection con el PFX configurado. En desarrollo las claves permanecen en `data/keys` (fuera de Git) y el proveedor local puede advertir que no se cifran en reposo. **No se cambió el identificador de aplicación de Data Protection ni los propósitos criptográficos**; por ello, al migrar el key ring al servidor se debe copiar el anillo completo, mantener permisos restringidos, conservar el PFX compatible y comprobar cookies/enlaces privados existentes antes de retirar el anillo anterior. No sincronices claves entre instancias (solo se permite una instancia en este piloto).

Compose es para desarrollo local por defecto (`ASPNETCORE_ENVIRONMENT=Development`) y conserva volúmenes SQL, claves e imágenes. No representa una topología de despliegue en MonsterASP.NET.

## SMTP y mensajes

La app mantiene el outbox SQL y el capturador de desarrollo. Reserva, cambios de cita y estados de suscripción no dependen de que el transporte envíe en ese instante. Para correo real configura `Email:Smtp:*` con secretos de entorno y un proveedor autorizado. No se ha usado SMTP real ni se han autorizado destinatarios de prueba.

Al aprobar una prueba de envío, revisar en el mensaje recibido (no solo la respuesta SMTP): remitente y dominio, HTTPS de cada enlace, destino correcto y contenido. La aceptación SMTP indica que el servidor aceptó el mensaje, **no** que el buzón lo recibió o lo mostró. Sigue las instrucciones del proveedor para SPF, DKIM y DMARC del dominio remitente. No inventar registros DNS. En Free, usar un SMTP externo permitido por el proveedor; el SMTP local MonsterASP.NET es Premium.

## Artefacto, migraciones y reversión

En Windows con .NET SDK 10 y `dotnet-ef` del manifiesto local (10.0.13):

```powershell
dotnet restore Citalu.sln
dotnet build Citalu.sln --configuration Release --no-restore
dotnet test Citalu.sln --configuration Release --no-build
dotnet tool restore
dotnet publish src/Citalu.Web/Citalu.Web.csproj --configuration Release --output artifacts/release/app
dotnet ef migrations script --idempotent --project src/Citalu.Infrastructure --startup-project src/Citalu.Web --output artifacts/release/migrations.sql
git rev-parse HEAD | Set-Content artifacts/release/commit.txt
```

`artifacts/` está ignorado. El SQL se genera, revisa y aplica manualmente; la aplicación no migra al arrancar. Antes de una publicación real: detener cambios, exportar la base y copiar imágenes/key ring a almacenamiento cifrado, conservar el artefacto anterior, probar el script en una restauración aislada y aplicar el SQL con identidad administrativa. Comprobar después `/health/live`, `/health/ready`, inicio de sesión, lectura de agenda y archivos. Nunca ejecutar migraciones contra producción desde CI.

Usar cambios aditivos compatibles hacia atrás. Si una versión nueva falla, volver al artefacto anterior solo si puede leer el esquema ya migrado; si no, restaurar primero base/archivos/claves al entorno aislado y acordar recuperación. No ejecutar un `Down` destructivo automáticamente. La integración actual no es un sistema de despliegue ni sustituye autorización del propietario.

## Respaldo y restauración

Frecuencia inicial sugerida si se obtiene una modalidad operable: diario antes de cambios del día y antes de cada publicación; retener 7 diarios y 4 copias semanales, cifradas y fuera del proveedor. Revisar al menos una restauración aislada al mes. Para SQL gratuito de MonsterASP.NET, hacer una exportación BACPAC interactiva desde SSMS en una estación administrada (conectar, clic derecho a la base, **Tasks > Export Data-tier Application**, guardar el `.bacpac` fuera de `wwwroot`). Para restaurar, conectar SSMS al SQL Server aislado, **Databases > Import Data-tier Application**, asignar un nombre nuevo y confirmar que el destino no sea producción. Para comparar el esquema y los datos, ejecutar `SELECT COUNT_BIG(*)` sobre `Businesses`, `Appointments`, `Services`, `BusinessSubscriptions`, `SubscriptionPeriods`, `SubscriptionPayments` y `EmailOutbox` en la base restaurada y comparar con los recuentos registrados antes de exportar. No poner la cadena de conexión en scripts del repositorio ni en argumentos guardados.

Para imágenes y Data Protection, usar `scripts/ops/backup-files.ps1 -UploadsPath 'D:\Citalu\uploads' -DataProtectionKeysPath 'D:\Citalu\keys' -EncryptedDestination 'E:\CitaluBackups'` (reemplazar por rutas del entorno y un volumen cifrado). La copia contiene nombres/rutas y claves; conservarla en un volumen cifrado con acceso restringido. `scripts/ops/verify-files-backup.ps1 -BackupFolder 'E:\CitaluBackups\<carpeta-UTC>'` comprueba hashes locales. Respaldar también el PFX de cifrado de claves en una bóveda/copia cifrada independiente; perderlo vuelve ilegibles las claves nuevas cifradas. En restauración, extraer/copiar a dos directorios vacíos nuevos fuera del directorio publicado y configurar esas rutas y el PFX en el entorno aislado. No sobrescribir la instalación activa. El manifiesto de archivos no cifra por sí mismo.

Restauración aislada: crear una base vacía con nombre nuevo; importar el BACPAC sin apuntar al nombre productivo; verificar las siete tablas/recuentos anteriores; extraer imágenes y claves a carpetas nuevas con acceso restringido; iniciar una instancia local con conexión de prueba y esa carpeta de claves; comprobar negocios y citas, abrir imágenes, iniciar sesión y probar un enlace privado previamente generado. Los tokens de gestión requieren tanto sus datos cifrados/identificadores persistidos como el key ring compatible. Una exportación o `RESTORE VERIFYONLY` sin importar la copia y abrir estos datos **no** demuestra recuperación.

**Restauración sintética local verificada el 20-oct-2026:** se respaldó `CitaluDelivery6Browser` con `BACKUP DATABASE ... WITH COPY_ONLY, CHECKSUM`, pasó `RESTORE VERIFYONLY` y se restauró con nombre nuevo `CitaluDelivery6RestoreVerify20261020A` (sin tocar ni borrar la base fuente). En la restauración se encontraron 1 negocio, 2 citas, 1 servicio, 1 suscripción y 3 filas del outbox; los periodos/pagos eran 0 porque el negocio de prueba no tenía pagos manuales. Se copiaron las imágenes y claves a otro directorio, pasaron los hashes SHA-256 y el proceso local leyó la página pública y un PNG (HTTP 200) y aceptó un enlace privado antiguo con la misma clave (servicio y estado Confirmada). Esto prueba el flujo local SQL Server/archivos/claves con datos sintéticos; **no** verifica BACPAC, permisos/persistencia/cifrado de MonsterASP.NET ni un respaldo del sitio remoto.

## Carga y experiencia del piloto

CI integra con SQL Server 2022 y la suite contiene 31 pruebas entre unitarias e integración; no sustituye una prueba de carga. El 20-oct-2026 se ejecutó un burst HTTP aislado en Kestrel contra la base restaurada SQL Server 2019 Express LocalDB: cinco clientes concurrentes hicieron 15 consultas de disponibilidad y 5 reservas no solapadas. Las 20 operaciones devolvieron HTTP 200; p50 fue 475 ms y p95 989 ms. Una comprobación adicional de carrera hizo cinco reservas simultáneas para el mismo espacio: una confirmó y cuatro recibieron conflicto, en 459 ms. Se usó el capturador local; no hubo SMTP real. Son bursts pequeños en una máquina de desarrollo, no una prueba sostenida de cinco minutos ni una predicción de capacidad. No se midieron CPU/memoria del SQL ni capacidad del alojamiento. Docker Desktop está instalado, pero su daemon no inicia, por lo que Compose y la imagen SQL Server 2022 local no se probaron. El plan informado tiene 256 MB RAM; ningún resultado de LocalDB se extrapola a MonsterASP.NET.

El recorrido de navegador se comprobó en 390×844 y 1440×1000 con la base sintética: registro/inicio de sesión de propietario, creación del negocio, alta de servicio, horario semanal, página pública, selección de disponibilidad, confirmación, resumen y aparición en la agenda. Después del ajuste, la agenda indicó “Notificación enviada” aunque había un recordatorio futuro en cola. La reserva y capturas se ejecutaron con el transporte local, sin envío SMTP real. No se probó el panel de MonsterASP.NET ni HTTPS externo.

## Lista previa al piloto

- [ ] Resolver worker continuo o mecanismo programado admitido para procesar outbox/recordatorios y vencimientos; verificar tras reinicio e inactividad.
- [ ] Verificar por prueba que las rutas de archivos/keys son escribibles, persisten y sobreviven una publicación; configurar copia externa cifrada.
- [ ] Exportar e importar un respaldo en un entorno aislado y probar enlaces privados/cookies.
- [ ] Configurar URL canónica HTTPS, `AllowedHosts`, proxies explícitos, SQL, almacenamiento, SMTP y secreto administrativo.
- [ ] Confirmar dominio/remitente autorizado, SPF/DKIM/DMARC según proveedor y recepción real con destinatarios autorizados.
- [ ] Probar capacidad y límites del plan con carga aislada, espacio SQL y almacenamiento de archivos.
- [ ] Completar precios/datos comerciales, horarios, cancelaciones, zona horaria, ubicación/contacto, instrucciones de pago y mensajes de soporte.
- [ ] Definir correo/canal de soporte, responsables de incidencias, respaldo y recuperación.
- [ ] Revisar términos, aviso de privacidad, retención/borrado y tratamiento de datos con asesoría apropiada; este checklist no afirma cumplimiento legal.
- [ ] Autorizar explícitamente cualquier migración o publicación externa. Nada se despliega desde esta rama.
