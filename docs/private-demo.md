# Demostración privada en MonsterASP.NET Free

Esta guía prepara una demostración aislada con datos ficticios. No convierte Free en un servicio operativo, no invita clientes y no autoriza por sí misma a publicar. **El 21-oct-2026 se conectó a la base `db74897`, que tenía cero tablas de usuario, y se aplicó el esquema actual (9 migraciones, 21 tablas incluido el historial). No hay negocios ni clientes cargados. La aplicación todavía no se publicó.** Hay que confirmar en el panel que esta base está dedicada exclusivamente a la demo antes de cargar datos; los permisos, persistencia y herramientas del plan aún deben probarse.

## Aislamiento y privacidad

1. En el panel crea un sitio nuevo para la demo. La base `db74897` ya tiene el esquema vacío de Citalú; confirma que es exclusiva de esta demo. Si no es exclusiva, detente y crea una base dedicada antes de cargar datos. No reutilices bases con clientes. Usa nombres, teléfonos y domicilios ficticios; para correos usa dominios reservados como `example.invalid`.
2. Activa en el panel la restricción por contraseña o IP antes de abrir el sitio. Comprueba desde una sesión/red autorizada que funciona y desde otra no autorizada que responde con denegación. No uses una URL difícil de adivinar ni `robots.txt` como control de acceso. Si el panel no puede proteger también las rutas de aplicación, no cargues información sensible ni compartas la demo.
3. Configura `ASPNETCORE_ENVIRONMENT=Demo`, `Email__CaptureOnly=true` y `Email__CapturePath` como ruta absoluta privada fuera del directorio publicado. No configures SMTP. La aplicación captura mensajes en archivos locales a ese directorio y no expone una página para descargarlos. Esos archivos contienen datos de prueba y enlaces privados: accede a ellos solo desde el administrador de archivos privado, no copies enlaces a tickets, PR, logs o mensajes, y elimina las capturas al terminar la prueba.
4. El capturador Demo solo se activa con ambas condiciones anteriores. La validación de inicio requiere ruta de capturas absoluta fuera de `wwwroot`; la selección del transporte en Demo usa el capturador únicamente cuando `Email:CaptureOnly=true`. No es un modo válido para piloto: SMTP real debe configurarse y verificarse por separado.

## Configuración en el panel

Primero confirma el host/subdominio asignado y las rutas absolutas que permite el plan. En la sección del panel para variables de entorno configura, con valores propios del panel y sin guardarlos en Git:

| Variable | Valor |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Demo` |
| `ConnectionStrings__DefaultConnection` | Cadena privada para la base exclusiva de demo; se introduce en el panel, no en el paquete |
| `Application__PublicBaseUrl` | `https://<host-asignado>` |
| `AllowedHosts` | `<host-asignado>` exacto |
| `DataProtection__KeysPath` | Ruta absoluta privada y persistente |
| `DataProtection__CertificatePath` | Ruta absoluta del PFX privado |
| `DataProtection__CertificatePassword` | Contraseña del PFX, solo como secreto del panel |
| `Storage__UploadsPath` | Ruta absoluta privada y persistente para imágenes |
| `Email__CaptureOnly` | `true` |
| `Email__CapturePath` | Ruta absoluta privada fuera de la raíz publicada |

`src/Citalu.Web/appsettings.Demo.json` ya establece `Application:PublicBaseUrl=https://citalu.runasp.net` y `AllowedHosts=citalu.runasp.net`. Se carga solo si el panel inicia el sitio con `ASPNETCORE_ENVIRONMENT=Demo`; una variable de entorno del panel puede sobrescribirlo.

La configuración no acepta LocalDB, requiere HTTPS canónico, host permitido, directorios absolutos y certificado PFX de Data Protection en cualquier entorno distinto de Development. `Demo` conserva cookies seguras. Si el proxy termina TLS, solo activa `ForwardedHeaders__Enabled` y agrega `ForwardedHeaders__KnownProxies__0` cuando el panel documente la IP exacta del proxy; no confíes en `X-Forwarded-Host`.

Genera el PFX localmente con una contraseña propia y transfiérelo por el mecanismo privado del panel. No escribas la contraseña en el chat, archivos versionados o historial de comandos. Conserva el PFX y su contraseña en una bóveda separada: el key ring está cifrado con ese certificado. No cambies el identificador Data Protection ni elimines claves existentes.

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
