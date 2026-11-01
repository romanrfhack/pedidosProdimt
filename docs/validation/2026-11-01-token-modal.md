# I10 — Modal responsivo de tokens

## Alcance y revisión

Sólo presentación Angular y distribución nativa del frontend sobre I09 instalado. Modal nativo con bloqueo de fondo, foco restaurado y cierre coherente durante operaciones; dos columnas PC, una en tableta/móvil, cuerpo desplazable y cierre visible. Estado del borrador y enlace conservado en padre; apertura/cierre no llama API de creación ni reinicia la configuración. Revocación informa en el modal. Revisión independiente estática de UI y empaquetado sin bloqueantes.

## Validación local

- npm run build correcto. Presupuestos conservados; advertencia previa de admin-customer-configuration.css (~7.55 kB) bajo el máximo de 8 kB.
- python3 -m unittest discover -s tests/pilot -q: 250 correctas, 12 nuevas. Integridad, target exacto, staging, idempotencia, rollback/fallo/interrupción y backend invariable verificados con fixtures aislados.
- graphify update . no disponible (127); no existe grafo previo en esta copia.
- Primer CI 41322649417: 101/103 E2E correctas; la comprobación de 1366×768 detectó altura extra de la tarjeta de acceso. Se sustituyó por un botón de 44 px. El otro fallo correspondía al fixture GET estático que omitía el token recién creado; se corrigió el fixture sin relajar la protección de acceso revocado. Las capturas finales se toman sobre el viewport, con formulario inicial, historial y enlace generado.
- CI final del código `89b51ddc3308b42e5ab9b4e0532826b568beb684`: [41323199461](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41323199461), cuatro trabajos correctos. 103 E2E, 305 .NET, 37 SQL Server y smoke Production. Las pruebas confirman borrador y enlace conservados sin creación extra, bloqueo de cierre durante operaciones, teclado/foco y tamaños 320/390/768/1366/1920 con 35 tokens largos. Capturas finales de viewport inicial/historial/enlace inspeccionadas en PC, tableta y móvil; cierre visible, contenido ajustado y desplazamiento interior. No equivale a aceptación con datos reales.

## Despliegue confirmado por el usuario

El 1 de noviembre el usuario verificó el ZIP y ejecutó el instalador con `--apply`, revisión `20261101-token-modal-install1`: `updated/completed`, `health_verified` y `frontend_verified` true. `backend_changed`, `database_migrations_run`, `service_restarted` y `password_changed` false. Frontend activo reportado: `/var/www/prodimt-pedidos/frontend/releases/token-modal-89b51ddc3308`; código `89b51ddc3308b42e5ab9b4e0532826b568beb684`. Snapshot privado: `/var/backups/prodimt-pedidos/token-modal-20261101T185243Z-2124f1ed5fa3`. Evidencia proporcionada por el usuario; sin acceso SSH desde este entorno.

Pendiente aceptación en navegador: Ctrl+F5 tras guardar cambios abiertos; abrir Configuración del cliente → Tokens de acceso y comprobar tamaño, desplazamiento interno, cierre/reapertura y conservación del borrador/enlace. Probar copia y apertura individual de WhatsApp sin confundirla con envío. El reporte no acredita todavía guardado de pedidos o envío de mensajes. No repetir el instalador.

## Paquete verificado

PRODIMT_Tokens_Modal_I10.zip, 180,442 bytes, SHA256 `1ff5df6abbf1ce0066662c1dccb88e1ff503f4291f8fc9754d52c5434f0657fc`. Payload SHA256 `94fac288150d38189e11b3785ec8306ac6a6b48ec4e49f34fc78cc3741ebb287`, feature customer-token-modal-v1, source `89b51ddc3308b42e5ab9b4e0532826b568beb684`. Build limpio con diez archivos frontend, incluidos avisos de terceros; sin mapas, backend, SQL, datos o secretos. SHA256SUMS y `load_payload` del instalador exportado con los bytes exportados correctos; sin ejecutar preflight o aplicación remota.

Extraer en PRODIMT_Tokens_Modal_I10/prodimt-token-modal-update; verificar SHA256SUMS, copiar PRODIMT_Instalar_I10.py y prodimt-token-modal-payload.zip al VPS y ejecutar el instalador con --apply. Resultado esperado updated/completed, frontend_verified y health_verified true; backend_changed y service_restarted false. Después recargar el navegador y abrir el botón Tokens de acceso. No repetir I09 ni las reparaciones de canales.
