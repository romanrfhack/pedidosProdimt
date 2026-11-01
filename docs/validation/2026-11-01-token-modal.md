# I10 — Modal responsivo de tokens

## Alcance y revisión

Sólo presentación Angular y distribución nativa del frontend sobre I09 instalado. Modal nativo con bloqueo de fondo, foco restaurado y cierre coherente durante operaciones; dos columnas PC, una en tableta/móvil, cuerpo desplazable y cierre visible. Estado del borrador y enlace conservado en padre; apertura/cierre no llama API de creación ni reinicia la configuración. Revocación informa en el modal. Revisión independiente estática de UI y empaquetado sin bloqueantes.

## Validación local

- npm run build correcto. Presupuestos conservados; advertencia previa de admin-customer-configuration.css (~7.55 kB) bajo el máximo de 8 kB.
- python3 -m unittest discover -s tests/pilot -q: 250 correctas, 12 nuevas. Integridad, target exacto, staging, idempotencia, rollback/fallo/interrupción y backend invariable verificados con fixtures aislados.
- graphify update . no disponible (127); no existe grafo previo en esta copia.
- Primer CI 41322649417: 101/103 E2E correctas; la comprobación de 1366×768 detectó altura extra de la tarjeta de acceso. Se sustituyó por un botón de 44 px. El otro fallo correspondía al fixture GET estático que omitía el token recién creado; se corrigió el fixture sin relajar la protección de acceso revocado. Las capturas finales se toman sobre el viewport, con formulario inicial, historial y enlace generado.
- CI final del código `89b51ddc3308b42e5ab9b4e0532826b568beb684`: [41323199461](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41323199461), cuatro trabajos correctos. 103 E2E, 305 .NET, 37 SQL Server y smoke Production. Las pruebas confirman borrador y enlace conservados sin creación extra, bloqueo de cierre durante operaciones, teclado/foco y tamaños 320/390/768/1366/1920 con 35 tokens largos. Capturas finales de viewport inicial/historial/enlace inspeccionadas en PC, tableta y móvil; cierre visible, contenido ajustado y desplazamiento interior. No equivale a aceptación con datos reales.

## Despliegue

I09 sigue siendo el último despliegue confirmado por el usuario. No se ha accedido por SSH desde este entorno ni instalado I10. El actualizador sólo cambia el enlace del frontend, conserva backend/SQL/configuración y no reinicia el servicio. Aceptación real pendiente después de aplicar el paquete.

## Paquete verificado

PRODIMT_Tokens_Modal_I10.zip, 180,442 bytes, SHA256 `1ff5df6abbf1ce0066662c1dccb88e1ff503f4291f8fc9754d52c5434f0657fc`. Payload SHA256 `94fac288150d38189e11b3785ec8306ac6a6b48ec4e49f34fc78cc3741ebb287`, feature customer-token-modal-v1, source `89b51ddc3308b42e5ab9b4e0532826b568beb684`. Build limpio con diez archivos frontend, incluidos avisos de terceros; sin mapas, backend, SQL, datos o secretos. SHA256SUMS y `load_payload` del instalador exportado con los bytes exportados correctos; sin ejecutar preflight o aplicación remota.

Extraer en PRODIMT_Tokens_Modal_I10/prodimt-token-modal-update; verificar SHA256SUMS, copiar PRODIMT_Instalar_I10.py y prodimt-token-modal-payload.zip al VPS y ejecutar el instalador con --apply. Resultado esperado updated/completed, frontend_verified y health_verified true; backend_changed y service_restarted false. Después recargar el navegador y abrir el botón Tokens de acceso. No repetir I09 ni las reparaciones de canales.
