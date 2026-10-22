# Demostración privada en MonsterASP.NET Free

Esta guía prepara una demostración aislada con datos ficticios. No convierte Free en un servicio operativo ni invita clientes. **La aplicación se publicó en `citalumx.runasp.net`, pero devolvió HTTP 500.30. El registro de ASP.NET Core identificó configuración de inicio incompleta: no recibió conexión SQL, rutas persistentes de claves e imágenes ni la configuración del transporte de correo de demo. La captura stdout se desactivó después del diagnóstico.** El esquema de `db74897` se había aplicado (9 migraciones, 21 tablas incluido el historial) y no contiene negocios ni clientes conocidos. Los permisos de escritura y persistencia tras reiniciar/publicar aún requieren validación.

## Aislamiento y privacidad

1. En el panel crea un sitio nuevo para la demo. La base `db74897` ya tiene el esquema vacío de Citalú; confirma que es exclusiva de esta demo. Si no es exclusiva, detente y crea una base dedicada antes de cargar datos. No reutilices bases con clientes. Usa nombres, teléfonos y domicilios ficticios; para correos usa dominios reservados como `example.invalid`.
2. Activa en el panel la restricción por contraseña o IP antes de abrir el sitio. Comprueba desde una sesión/red autorizada que funciona y desde otra no autorizada que responde con denegación. No uses una URL difícil de adivinar ni `robots.txt` como control de acceso. Si el panel no puede proteger también las rutas de aplicación, no cargues información sensible ni compartas la demo.
3. La configuración Demo usa `Email:CaptureOnly=true` por defecto. Configura también `ASPNETCORE_ENVIRONMENT=Demo` y `Email__CapturePath` como ruta absoluta privada fuera del directorio publicado. No configures SMTP. La aplicación captura mensajes en archivos locales a ese directorio y no expone una página para descargarlos. Esos archivos contienen datos de prueba y enlaces privados: accede a ellos solo desde el administrador de archivos privado, no copies enlaces a tickets, PR, logs o mensajes, y elimina las capturas al terminar la prueba.
4. El capturador Demo solo se activa con ambas condiciones anteriores. La validación de inicio requiere ruta de capturas absoluta fuera de `wwwroot`; la selección del transporte en Demo usa el capturador únicamente cuando `Email:CaptureOnly=true`. No es un modo válido para piloto: SMTP real debe configurarse y verificarse por separado.

## Configuración en el panel

Abre **Scripting → Environment variables** del sitio `citalumx.runasp.net`. El panel mostraba cero variables personalizadas durante la revisión. La raíz física del sitio aparece en el evento IIS como `D:\Sites\site100831\wwwroot`; el administrador de archivos muestra `private` como carpeta hermana de `wwwroot`. Se crearon `private\citalu\keys`, `private\citalu\uploads` y `private\citalu\mail-capture`. Sus rutas esperadas son `D:\Sites\site100831\private\citalu\...`, pero **no se ha probado que el proceso de la aplicación pueda escribirlas ni que el plan las conserve**. Configura esas rutas y valida permisos al iniciar; si el host rechaza escritura, detente y solicita al soporte la ruta persistente escribible. No pongas datos privados bajo `wwwroot`.

En **Scripting → Environment variables**, crea estos pares nombre/valor; MonsterASP usa `__` para las claves anidadas. Pon los secretos solo en el campo Value del panel:

| Variable | Valor |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Demo` |
| `ConnectionStrings__DefaultConnection` | `Server=tcp:db74897.public.databaseasp.net,1433;Initial Catalog=db74897;User ID=db74897;Password=<contraseña SQL>;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True` |
| `Application__PublicBaseUrl` | `https://<host-asignado>` |
| `AllowedHosts` | `<host-asignado>` exacto |
| `DataProtection__KeysPath` | `D:\Sites\site100831\private\citalu\keys` |
| `DataProtection__CertificatePath` | `D:\Sites\site100831\private\citalu\keys\citalu-dataprotection.pfx` |
| `DataProtection__CertificatePassword` | Contraseña del PFX, solo como secreto del panel |
| `Storage__UploadsPath` | `D:\Sites\site100831\private\citalu\uploads` |
| `Email__CaptureOnly` | `true` (también viene activado por `appsettings.Demo.json`) |
| `Email__CapturePath` | `D:\Sites\site100831\private\citalu\mail-capture` |

Añade `DataProtection__KeysPath=D:\Sites\site100831\private\citalu\keys` y `Storage__UploadsPath=D:\Sites\site100831\private\citalu\uploads`. Sube un PFX con clave privada a `private\citalu\keys` desde el administrador de archivos y configura `DataProtection__CertificatePath` con su ruta física completa, más `DataProtection__CertificatePassword` con la contraseña del PFX. El PFX debe generarse localmente, cifrarse con una contraseña fuerte y no versionarse. Conserva una copia segura del PFX y la contraseña: perderlos puede impedir descifrar el key ring y romper cookies/enlaces existentes.

**Rotación de credencial SQL:** la contraseña de SQL fue escrita anteriormente en el chat. Rótala en el panel de base de datos antes de usarla aquí; no la envíes por chat ni la pongas en Git, `appsettings*.json`, `web.config` o el perfil `.pubxml.user`.

No agregues credenciales SMTP en la demo. Con `ASPNETCORE_ENVIRONMENT=Demo` y `Email__CaptureOnly=true`, los correos quedan como archivos privados y no se envían. El perfil local de Visual Studio ahora selecciona `Demo` y abre la URL HTTPS, y `appsettings.Demo.json` activa el capturador.

`src/Citalu.Web/appsettings.Demo.json` establece `Application:PublicBaseUrl=https://citalumx.runasp.net` y `AllowedHosts=citalumx.runasp.net`. Se carga solo si el panel inicia el sitio con `ASPNETCORE_ENVIRONMENT=Demo`; una variable de entorno del panel puede sobrescribirlo.

Los perfiles locales `site100831-WebDeploy*.pubxml` seleccionan el entorno `Demo` y abren la URL HTTPS. Los perfiles y sus credenciales siguen ignorados por Git. Conserva cada archivo `.pubxml.user` solo localmente; no lo subas.

La configuración no acepta LocalDB, requiere HTTPS canónico, host permitido, directorios absolutos y certificado PFX de Data Protection en cualquier entorno distinto de Development. `Demo` conserva cookies seguras. Si el proxy termina TLS, solo activa `ForwardedHeaders__Enabled` y agrega `ForwardedHeaders__KnownProxies__0` cuando el panel documente la IP exacta del proxy; no confíes en `X-Forwarded-Host`.

### Diagnóstico de arranque

El 22-oct-2026, el sitio seguía respondiendo HTTP 500.30 en la raíz y en `/health/live`, lo que indica que el proceso no alcanzaba a iniciar. Se encontró y corrigió un error en la validación de `Email__CapturePath`: una ruta privada hermana de `wwwroot` se clasificaba incorrectamente como inválida. La validación ahora acepta rutas fuera de `wwwroot` y rechaza rutas dentro de la carpeta publicada. El paquete Release comprobado usa `net9.0`, genera `web.config` con ambiente `Demo`, no incluye `appsettings.Development.json` y mantiene vacía la conexión SQL del archivo publicado. Build y publicación Release: correctos. La suite completa pasó con SQL Server LocalDB 15 y una base temporal exclusiva: 11 pruebas unitarias y 25 de integración. El run #29 de GitHub Actions falló en tres pruebas HTTP porque el host de pruebas usaba `localhost` y la lista permitida no lo incluía; se corrigió la configuración del host de pruebas. El run #30 pasó build, verificación del modelo EF, las pruebas con SQL Server y la generación/carga del artefacto Release.

Ese cambio todavía requiere una nueva publicación y reinicio para llegar al sitio. No se pudo leer el registro ASP.NET del panel desde la sesión del navegador; si el 500.30 persiste después de publicar, revisa **ASP.NET debug log** y **Event log**. Verifica primero que el PFX exista en la ruta configurada, tenga clave privada y abra con la contraseña almacenada en el panel; después confirma que el proceso puede escribir en las carpetas privadas. No pegues secretos en incidencias ni en el chat.

Genera el PFX localmente con una contraseña propia y transfiérelo por el mecanismo privado del panel. El script `scripts/New-CitaluDataProtectionCertificate.ps1` pide la contraseña en una entrada segura y crea el archivo en Descargas. No escribas la contraseña en el chat, archivos versionados o historial de comandos. Conserva el PFX y su contraseña en una bóveda separada: el key ring está cifrado con ese certificado. No cambies el identificador Data Protection ni elimines claves existentes.

Publica el ZIP Release en el sitio protegido usando el método que indique el panel. El proyecto excluye `data/`, `wwwroot/uploads/` y `appsettings.Development.json` al publicar, por lo que el artefacto probado no incluye bases, imágenes, capturas, PFX, key ring ni settings locales de desarrollo. No se ha verificado qué administrador de archivos/editor ofrece la cuenta Free ni las rutas admitidas; confirma esto primero con un directorio de prueba sin datos. No coloques archivos de capturas bajo `wwwroot`.

### Estado del esquema

El paquete contiene `migrations.sql`, un script EF idempotente. El 21-oct-2026 se aplicó a `db74897` por SQL cifrado desde un cliente remoto, con `QUOTED_IDENTIFIER` activado. La consulta posterior encontró las 9 migraciones esperadas, 21 tablas contando `__EFMigrationsHistory` y cero negocios. No vuelvas a aplicarlo manualmente a menos que una comprobación de migraciones pendientes lo indique. La app no migra automáticamente al arrancar. No ejecutes el script en una base con información real o compartida.

Después comprueba `/health/live` y `/health/ready`, la URL HTTPS canónica, una cuenta de demo y los mensajes de prueba capturados. No incluyas la ruta ni contenido de las capturas en el PR.

## Persistencia, reinicios y respaldos

Con datos exclusivamente sintéticos, sube una imagen de prueba y crea una cita con enlace privado. Guarda la clave PFX y el anillo completos. Reinicia el sitio; verifica que la imagen sigue disponible y que el enlace continúa válido. Vuelve a publicar una versión y repite. Si una ruta se limpia, no es escribible o el enlace deja de validar, detén la demo: el panel no ofrece persistencia suficiente para esa configuración.

Para respaldar, exporta la base con la herramienta disponible en el plan y descarga por separado imágenes, key ring y PFX mediante un canal privado. No se verificó en la cuenta cuál exportador/importador ofrece el plan Free. La documentación previa describe BACPAC por SSMS, pero eso depende de acceso SQL/SSMS; comprueba que la opción existe y que el archivo se puede descargar. Para validar recuperación, restaura en otra base nueva aislada, extrae imágenes y claves a directorios nuevos y comprueba negocio, cita, imagen y enlace privado. No llames “respaldo verificado” a una exportación que no hayas importado y probado. El ejercicio local sintético documentado en `pilot-readiness.md` no verifica herramientas del host.

## Suspensión y notificaciones

MonsterASP.NET Free puede dormir la aplicación después de inactividad; no se cuenta con `AlwaysRunning` ni tarea programada en Free. Al volver a recibir tráfico, el host vuelve a iniciar la aplicación y el procesador consulta SQL: los trabajos pendientes vencidos vuelven a ser elegibles y los trabajos en `Processing` pueden reclamarse al expirar su lease de dos minutos. La retención está en SQL, así que el reinicio no borra la cola.

- Al despertar, un recordatorio atrasado se procesa si la cita todavía está confirmada, su versión sigue vigente y su inicio aún es futuro. No se garantiza la anticipación configurada ni un plazo de envío.
- Si la cita está cancelada/reprogramada, el trabajo viejo queda obsoleto. Si su inicio ya pasó, el recordatorio se marca sustituido y no se envía.
- Existe una carrera inevitable entre la última validación de estado y la aceptación del mensaje por el transporte: una cancelación posterior a esa comprobación no puede retirar un mensaje ya entregado al transporte.
- En Demo, el “envío” se limita al archivo privado de captura; no llega correo real. No uses información real.
- Las reservas, recordatorios oportunos y avisos de vencimiento no tienen garantía de ejecución mientras el sitio duerme. Las reglas de acceso por vencimiento se evalúan al atender cada solicitud; la notificación no es la autoridad del vencimiento.

Por ello, Free sirve únicamente para una demo privada breve. No invites clientes ni la presentes como piloto operativo.

## Requisitos antes de pasar a piloto

- [ ] Worker con ejecución fiable, reanudación demostrada y procesamiento oportuno; el plan Free no lo garantiza.
- [ ] SMTP real probado con remitente y destinatarios autorizados, incluyendo confirmación, recuperación, reserva y recordatorio.
- [ ] Exportación y restauración remotas de SQL, imágenes, key ring y PFX verificadas en un entorno aislado.
- [ ] Persistencia de imágenes y claves verificada después de reinicio y publicación.
- [ ] Prueba de carga sostenida con condiciones y resultados documentados en un entorno aislado.

No se desplegó ni se modificó ningún servicio externo como parte de esta guía.
