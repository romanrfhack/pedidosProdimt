# I14 — Plantilla simplificada del mensaje de acceso

Autorizado el 1 de noviembre de 2026 después de revisar con el usuario la limitación del enlace en un mensaje normal de WhatsApp.

## Comportamiento

El mensaje conserva saludo con nombre del cliente, descripción editable y vencimiento con hora de Ciudad de México. Agrega «Haz tu pedido aquí:» antes de la URL completa, en otra línea. Retira únicamente del texto compartido «Entrega prevista si lo confirmas ahora…» y la explicación del corte de las 18:00.

La URL sigue siendo del mismo origen, `/cliente#token=` y el token del acceso correspondiente. No usa enlaces Markdown/HTML, redirección, acortador ni botón simulado. No se integra WhatsApp Business API. El operador sigue confirmando el envío de la conversación preparada individualmente.

`AccessTokenManager.messageFor` es la única plantilla para vista previa, Copiar mensaje, copia manual y WhatsApp, tanto de enlaces nuevos como de los existentes recuperables. Guardar y abrir WhatsApp usa los datos recién guardados. No se reescriben descripciones, fechas o secretos de los tokens por instalar.

La fecha de entrega se sigue mostrando y validando en el portal del cliente y en la información administrativa. Se mantienen las reglas de las 18:00, las 10:00, cruce de fecha, vigencia/revocación y aislamiento del cliente.

## Entrega nativa

Frontend I13 `product-catalog-2345c7f4c27f` requerido; backend I12 `access-tokens-65560ca3cf98` conservado. El nuevo release es `reminder-message-{source12}`. El paquete incluye build Angular de toda la rama I13 para mantener agrupación/tarjetas, modales, perfiles y precios. No instala I12/I13 por adelantado ni acepta frontends previos.

Instalador fijado por hashes, preflight de lectura de proceso/enlaces y comprobaciones HTTP, snapshot privado, activación atómica, verificación del HTML/JS/CSS servido y reversión del enlace ante fallo. Repetición del mismo paquete verifica `already_current`. No consulta SQL directamente, migra, cambia claves/contraseñas, revoca tokens o reinicia servicios. El endpoint de salud existente puede comprobar conectividad de la base.

## Validación y aceptación

Build Angular, regresión E2E de enlaces nuevos y existentes, igualdad de mensaje entre vista previa/copia/WhatsApp/copia manual, y pruebas Python de paquete/instalador. Evidencia en `docs/validation/2026-11-01-reminder-message.md`.

Aplicación y paquete `768230826a3eff9d02cea0620acd814cb0047359`, [PR #6](https://github.com/romanrfhack/pedidosProdimt/pull/6). [CI 41383717018](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41383717018) completo con cuatro trabajos correctos: 335 Domain/Application, 66 SQL/criptografía, 134 E2E y 320 Python más smoke Production. ZIP final verificado/guardado: `PRODIMT_Plantilla_Mensaje_I14.zip`, 198059 bytes, SHA256 `c8b51437fa71e0fe61164cafabee7902e46a8aa69d4b630c5a55e67db957e39b`. Instalación remota pendiente.

Tras instalar, recargar con Ctrl+F5 y abrir Configuración de cliente → Tokens de acceso. Verificar texto simplificado, vencimiento y mismo enlace; comprobar fecha de entrega visible dentro del portal y conservación de Catálogos → Productos I13. No crear pedidos ni enviar mensajes desde las pruebas. La entrega no demuestra instalación remota o aceptación de negocio.

## Instalación confirmada

El 2 de noviembre de 2026 el usuario comparte preflight `planned/completed` y aplicación `updated/completed`, con base/salud/frontend verificados. Fuente desplegada `768230826a3eff9d02cea0620acd814cb0047359`, release `reminder-message-768230826a3e`; snapshot registrado en la evidencia. Sin migraciones/consultas SQL directas, cambios de backend/contraseña, reinicio o secretos impresos según el instalador. No se operó VPS/SQL desde este entorno. Instalación confirmada por ese reporte; aceptación funcional de la plantilla/portal pendiente del usuario. No repetir instalación, importaciones o generación de accesos por este resultado.
