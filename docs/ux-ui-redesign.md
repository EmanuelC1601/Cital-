# Rediseño UX/UI de Citalú

## Dirección visual

- Marca comercial **Citalú**; nombres técnicos y estructura MVC/Razor siguen como están.
- Fondo marfil `#f6f3f0`, superficies blancas, texto carbón `#28242a`, texto secundario `#625b63` y acento ciruela `#5b2148`.
- Estados semánticos separados: verde `#14633d`, ámbar `#784500`, rojo `#a12634` y azul `#214f70`; los estados incluyen texto e indicador, no dependen solo del color.
- Tipografía del sistema (Segoe UI/Roboto/Arial), familia de iconos dibujados con SVG inline de trazo y sin dependencia o solicitud externa en tiempo de ejecución.
- Tokens CSS centralizados para color, radios, espacios, foco y sombra. Contrastes calculados WCAG para pares de texto/superficie usados: texto carbón sobre marfil 13.81:1, secundario sobre marfil 5.94:1, texto blanco sobre ciruela 11.96:1, éxito 8.78:1, advertencia 9.46:1, error 9.04:1 e información 8.68:1. Foco visible, controles táctiles de al menos 44 px y respeto a `prefers-reduced-motion`.

## Revisión de la versión anterior

| Recorrido | Observación concreta |
| --- | --- |
| Registro, acceso y recuperación | Formularios ya usan etiquetas visibles, validación y enlaces de recuperación. La barra de cuenta general no distinguía una experiencia pública de un espacio autenticado. |
| Configuración inicial | La creación de negocio presenta un formulario sencillo, pero la pantalla posterior agrupa configuración y enlaces en una cuadrícula de botones, sin progreso guiado por datos. |
| Panel y agenda | Navegación global horizontal con siete enlaces; el nombre del negocio y la agenda compiten en la misma cabecera. La fecha requería pulsar “Ver agenda”. El alta manual estaba dentro de un `details` sin asociación visual con la acción principal. Las citas eran tarjetas uniformes con muchas acciones al mismo nivel y el estado del correo no se diferenciaba claramente en cada cita. El estado vacío no facilitaba compartir el enlace. |
| Servicios | El formulario de alta abierto ocupa el inicio de la página; cada servicio repite un editor largo y acciones que compiten visualmente. |
| Horarios | Ventanas, descansos, límites, excepciones y bloqueos aparecen en cuatro formularios extensos. No hay acción para copiar una ventana a otros días; los intervalos están expresados con campos técnicos sin resumen contextual. |
| Personalización y página pública | El formulario combina información, recordatorios, zona horaria y colores antes de las cargas de imágenes. La página pública mantiene los datos públicos separados de los datos de identidad, aunque el botón de reserva necesita una jerarquía más clara frente a la información del negocio. |
| Reserva y gestión | El flujo de reserva ya presenta servicio, fecha, disponibilidad y datos; los estados de carga/error/conflicto se manejan en el cliente. Todavía se puede hacer más evidente el paso actual y conservar un resumen al regresar. La vista de gestión debe mantener el enlace privado como acceso central. |
| Suscripción | La información existente está disponible, pero requiere revisión visual para jerarquizar estado, fechas de cobertura, precio e historial sin confundir correo con suscripción. |
| Administración de plataforma | Está protegida por rol. La nueva navegación conserva esta sección únicamente para `PlatformAdmin`; no la presenta a propietarios regulares. |

No se encontró la captura referida en los archivos adjuntos disponibles para este turno; se inspeccionó el código de la rama base `Ema/delivery-6-pilot-readiness`. No hay texto “Belleza” visible en las vistas o CSS inspeccionados. Se conserva el propósito criptográfico legado `Belleza.AppointmentManagement.v1` para compatibilidad.

## Primera implementación

Esta etapa cubre la estructura autenticada y la agenda del propietario:

- En escritorio, navegación lateral compacta con estado activo, cuenta y cierre de sesión; administración de plataforma solo para el rol correspondiente.
- En celular, menú de secciones que se despliega en el flujo del documento y barra fija con Agenda y Nueva cita; la zona de menú no tapa los controles de pantalla.
- Agenda con contexto y avatar inicial del negocio, fecha en español, pasos de día anterior/siguiente, selector nativo que cambia de fecha inmediatamente y acceso a Hoy.
- Citas en orden cronológico con hora de inicio/fin, cliente, servicio, precio, duración, estado textual y estado de correo separado. Las acciones existentes permanecen vinculadas a las rutas actuales.
- Estado vacío con “No hay citas registradas para este día”, acción para agregar cita y enlace público del negocio; no afirma que el negocio esté libre o cerrado.
- Alta manual inline, accesible por teclado, con etiquetas, campos compactos, ayuda de validación del horario y botón guardar. Tras un conflicto de horario, la redirección conserva temporalmente los valores de entrada con TempData protegido por ASP.NET Core. La validación final sigue siendo del servidor y el mismo control transaccional existente.
- No se usa modal, dependencia de iconos, fuentes remotas, datos de producción ni tokens de gestión para las capturas.

Las reglas de disponibilidad, reservas, autorizaciones, suscripciones, persistencia y migraciones no forman parte de este cambio visual.

## Capturas y revisión de la primera etapa

Capturas reales de navegador en `output/playwright/` (datos sintéticos de LocalDB `CitaluUxVisual20261020`):

- `agenda-appointments-desktop.png` — agenda con cita a 1440 × 1000.
- `agenda-appointments-mobile-390.png` — agenda con cita a 390 × 844; el ancho del documento medido fue 390 px, igual al viewport.
- `agenda-empty-desktop.png` — estado vacío a 1440 × 1000.
- `agenda-empty-mobile-390.png` — estado vacío a 390 × 844; ancho de documento igual al viewport.
- `new-appointment-desktop.png` — formulario manual abierto a 1440 × 1000.
- `new-appointment-mobile-390.png` — formulario manual abierto a 390 × 844; la barra fija se oculta mientras se edita para no tapar campos.

También se midió ancho de documento y viewport a 360 px; ambos dieron 360 px. En el navegador se intentó guardar una segunda cita ficticia para el mismo horario: el servidor informó el conflicto, conservó el servicio, hora y datos de contacto introducidos, y la agenda siguió mostrando una sola cita. La cita de muestra contiene únicamente datos ficticios; las capturas no muestran el enlace privado de gestión. Estos archivos se conservan en `output/playwright/`, ignorado por Git.

La primera etapa es una propuesta visual funcional para revisar. El recorrido de agenda y nueva cita se verificó en navegador local con SQL Server LocalDB. El build Release y las pruebas unitarias e integración contra SQL Server LocalDB finalizaron correctamente (11 unitarias y 23 integración). La validación en más tamaños, flujos completos y CI se registra al cerrar el rediseño integral; esta primera revisión no significa que las demás pantallas estén rediseñadas.

Después de revisar esta dirección, el siguiente tramo extiende los mismos componentes y patrones a autenticación/configuración, servicios/horarios, perfil y página pública, reserva/gestión, suscripción y administración. La validación total de todos esos recorridos pertenece al cierre integral, no se considera terminada en esta primera revisión.
