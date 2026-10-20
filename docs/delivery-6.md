# Entrega 6: preparación operativa y piloto

## Preparado

- Inicio en `Production` valida antes de servir una conexión SQL Server remota, URL canónica HTTPS, `AllowedHosts` exacto, rutas persistentes, certificado PFX de Data Protection, SMTP y proxies confiables configurados. No acepta LocalDB en Production.
- Los correos, enlaces de reserva/recuperación y QR usan `Application:PublicBaseUrl`; no derivan su dominio del encabezado `Host`.
- Cookies de Identity se mantienen Secure/HttpOnly en Production, se aceptan encabezados `X-Forwarded-For`/`Proto` solo al habilitar proxy y enumerar direcciones confiables, el logging de Production es JSON estructurado, las excepciones no se exponen por HTTP, y `/health/live` y `/health/ready` no muestran detalles privados.
- Imágenes usan `Storage:UploadsPath` fuera de `wwwroot` configurable y se sirven en `/uploads`; el key ring persiste en `DataProtection:KeysPath`. En Production el key ring debe cifrarse con el certificado PFX configurado. No se cambió el identificador de aplicación ni los propósitos Data Protection.
- Se añadieron comandos/scripts para empaquetar Release y SQL idempotente, sondear health, copiar/verificar hashes de archivos y documentar BACPAC/recuperación. CI produce un artefacto revisable con app, SQL y commit.
- La guía del dueño, configuración de producción, backups, rollback, límites del proveedor y checklist previo están en [pilot-readiness.md](pilot-readiness.md) y [pilot-owner-guide.md](pilot-owner-guide.md).

## Pruebas ejecutadas

- **Aprobado:** `dotnet build Citalu.sln --configuration Release`, 0 advertencias/errores.
- **Aprobado:** `dotnet tool restore` y `dotnet ef migrations has-pending-model-changes --project src/Citalu.Infrastructure --startup-project src/Citalu.Web`; sin cambios de modelo pendientes. No se generó ni aplicó migración.
- **Aprobado:** `dotnet test Citalu.sln --configuration Release --no-build`, 11 pruebas unitarias y 20 pruebas de integración. Integración contra SQL Server 2019 Express LocalDB, base aislada existente `CitaluDelivery5Verify3`; se conservaron sus datos.
- **Aprobado:** arranque local y `/health/live`=`ok`, `/health/ready`=`ready` con SQL Server.
- **Aprobado:** guard de producción rechazó configuración incompleta antes de servir HTTP; la salida revisada no incluyó valores secretos.
- **Aprobado:** recorrido Playwright local en móvil 390×844 y escritorio 1440×1000: registrar/iniciar propietario, configurar negocio, servicio y horario, cargar página pública, consultar horario, reservar, leer confirmación, consultar agenda y revisar estado del envío. Transporte local de correo únicamente.
- **Aprobado, datos sintéticos:** SQL Server hizo backup con checksum y restauró a una base nueva aislada. Imágenes y key ring se copiaron y validaron por SHA-256; después del restore la página pública, una imagen y un enlace privado antiguo respondieron correctamente. Detalle en `pilot-readiness.md`. No se modificó ni borró la base fuente.
- **Aprobado como burst local, no como capacity test:** cinco clientes concurrentes, 15 consultas de disponibilidad y 5 reservas en Kestrel+LocalDB, HTTP 200 en las 20, latencia p50 475 ms/p95 989 ms. La prueba de colisión simultánea devolvió 1 confirmación y 4 conflictos en 459 ms. Se usó capturador de desarrollo; no SMTP.
- **Pendiente observar:** GitHub Actions para el commit de esta rama y artefacto CI, después de publicar.
- **No ejecutado:** Compose con SQL Server 2022 local; Docker Desktop responde que su daemon no está iniciado. CI anterior usa SQL Server 2022, pero no sustituye el job nuevo.

## Dependencias y bloqueos

- El plan informado es MonsterASP.NET Free. Su documentación permite .NET 10, SQL Server 2025, variables de entorno y HTTPS para subdominio; la base Free tiene 1 GB. No se conectó la cuenta ni se aplicaron migraciones remotas.
- **Bloqueo del worker en Free:** los procesos se detienen tras tres horas sin solicitudes y tanto `AlwaysRunning` como planned tasks son Premium. Los correos persistentes, recordatorios y avisos de vencimiento no tienen ejecución oportuna garantizada en ese plan. No se usó un ping como sustituto.
- La documentación oficial permite SMTP externo en Free, pero no hay proveedor/cuenta configurados ni destinatarios autorizados; no se enviaron correos reales. El SMTP local del host es Premium.
- No hay confirmación documentada de permisos/persistencia para `Storage:UploadsPath` ni de supervivencia a publicaciones. Tampoco se realizó exportación/importación BACPAC del SQL alojado ni restore de archivos del host. La prueba de recuperación solo cubre LocalDB sintético.
- No se validaron en el servidor el PFX de cifrado, variables secretas, IP/proxy de confianza, URLs HTTPS, remitente ni entrega SMTP. `AlwaysRunning`/tarea programada, persistencia de archivos y backup externo deben resolverse antes del piloto.
- La prueba de carga es breve y local: faltan una corrida sostenida, métricas de CPU/memoria y mediciones en el alojamiento. Rate limiting sigue en memoria por proceso; el piloto se documenta de una sola instancia.
- SMTP real, reputación/recepción, SPF/DKIM/DMARC del remitente, datos comerciales, soporte, términos/privacidad y revisión legal siguen pendientes. No se afirma cumplimiento legal.
- No se desplegó. El PR contra `main` y la publicación de la rama quedan sujetos a CI y permisos de GitHub, que se reportarán por separado.
