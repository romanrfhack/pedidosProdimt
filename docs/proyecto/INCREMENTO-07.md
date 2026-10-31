# I07 — Captura rápida de pedidos

Autorizado el 28 de octubre de 2026. Implementado, validado y desplegado por el usuario en el VPS el 30 de octubre de 2026. Aceptación en navegador pendiente.

## Comportamiento

En Pendientes, Capturar pedido abre por defecto un único campo «Pedido del cliente». Al perder foco o pulsar Enter muestra el desglose; interpretar no registra demanda. Guardar es una acción separada. La hora/ventana habitual se muestra compacta e informativa y se modifica en la configuración del cliente. Las notas quedan contraídas.

- `16`: 16 unidades del producto en posición de interpretación 1.
- `13+7`: 13 del producto en posición 1 y 7 del de posición 2.
- `1+5+2HN`: 1 de posición 1, 5 de posición 2 y 2 del producto con abreviatura HN para ese cliente.
- La unidad procede del producto: kg, piezas, docenas o paquetes. No hay conversiones implícitas.
- Una abreviatura no consume posición sin etiqueta; `2HN+5` usa HN y posición 1. Repetir un mismo producto falla, no suma silenciosamente.
- Se permiten espacios, cero para saltar una posición y dos decimales con punto o coma. No se admiten cantidades negativas, frases libres ni productos/alias desconocidos.

«Posición de interpretación» y orden visual son independientes. HN no se precarga ni recibe significado inferido: se asigna explícitamente en Configurar cliente > Productos frecuentes > Abreviatura para captura. Letras A–Z, 1–16, normalizadas a mayúsculas y únicas dentro de ese cliente.

## Revisión y guardado

La vista previa puede mostrar un perfil importado aún no aprobado. «Confirmar orden y productos» permite revisar y aprobarlo una sola vez, con versión/unidad observadas; después se interpreta otra vez. Faltantes de posición/unidad/producto activo bloquean captura rápida. Captura manual permite escribir cantidades de productos configurados y con unidad confirmada sin aprobar la interpretación.

El servidor vuelve a interpretar y comprueba fecha, texto, versión, productos/unidades y preferencias de entrega contra la vista previa. Un cambio exige revisar de nuevo. El pedido hereda la entrega del cliente; no la configura desde captura. El texto original y las líneas resueltas quedan en auditoría.

Un pedido vigente para cliente/fecha obliga a abrirlo para corregir o confirmar otra entrega independiente, que mantiene revisión administrativa. No se suman kilos por recibir otro mensaje. Se conservan fecha operativa y corte de las 10:00.

Referencia por intento, pedido, auditoría y recibo se guardan en una transacción. Si se pierde la respuesta, Reintentar guardado conserva exactamente la referencia y el contenido; no permite cambiar el borrador incierto. InMemory no simula esta persistencia.

## Contratos aditivos

- POST `/api/admin/customers/{id}/orders/preview`: texto y fecha; devuelve líneas, perfil y huella de vista previa.
- POST `/api/admin/customers/{id}/orders/capture`: referencia, versión y texto/huella, o partidas manuales con unidad observada. Requieren AdminAccess.
- Perfil y CSV frecuente añaden `captureAlias` opcional. PUT con null/ausente y CSV sin columna conservan el valor; cadena vacía/columna presente vacía lo elimina. Importar revoca aprobación como antes.
- Migración `20261028202031_AddCustomerCaptureAliases`: columna nullable nvarchar(16) e índice único filtrado por cliente/abreviatura. Historial nativo I06 10 → I07 11. Sin modificación de pedidos históricos.

## Entrega nativa

Paquete nativo compilado fuera del VPS, instalador con respaldo COPY_ONLY/CHECKSUM verificado antes de DDL, preservación de configuración y cuentas, comprobación de frontend/API y reversión de aplicación ante fallo. No se restaura SQL automáticamente. No se reimportan catálogos. Herramientas: `scripts/pilot/install-quick-capture-update.py` y `build-quick-capture-package.py`. El instalador comprueba I06 activo, historial/esquema 10 o 11 y permisos de la aplicación; un esquema parcial se rechaza antes de detenerla. La configuración y los datos se conservan. Código `46fed604732eddca0c7ac199b58bb0b429545b89`, [cuatro jobs CI correctos](https://github.com/romanrfhack/pedidosProdimt/actions/runs/40765239145): 267 .NET, 29 SQL Server, 68 E2E y 162 Python/Production. Paquete PRODIMT_Captura_Rapida_I07.zip de 5.7 MB. Evidencia y hashes: [2026-10-28-quick-capture.md](../validation/2026-10-28-quick-capture.md). Instalación confirmada el 30 de octubre; aceptación funcional en navegador pendiente.

## I07 desplegado — 30 de octubre de 2026

Confirmado por salida del instalador ejecutado por el usuario: revision 20261028-capture-install1, status updated, phase completed, historial 10→11, migrated_schema_verified, existing_data_counts_preserved y health_verified true. Código 46fed604732eddca0c7ac199b58bb0b429545b89. Backend/frontend activos en capture-46fed604732e. Contraseñas sin cambio; no se restauró SQL automáticamente.

- Snapshot privado: `/var/backups/prodimt-pedidos/quick-capture-update-20261031T024549Z-90dbaa60d04e`.
- Respaldo SQL verificado: `/var/opt/mssql/backup/prodimt-i07-20261031T024549Z-90dbaa60d04e/ProdimtPedidos-copy-only.bak`.

El instalador verificó esquema, permisos DML, proceso, /health/db, autorización de los endpoints y frontend/JS/CSS por HTTPS. Se trata de evidencia reportada por el usuario; no se accedió al VPS desde este entorno. El campo database_checks.migrations_count=10 pertenece al preflight; migrations_after=11 y migrated_schema_verified=true confirman el resultado final.

Siguiente paso: Ctrl+F5 y revisar Pendientes → Capturar pedido. Comprobar fecha, entrega habitual informativa, texto abreviado/desglose por posiciones y alternativa manual. Perfiles importados aún requieren confirmación explícita para guardar captura rápida; HN debe asociarse al producto correcto por cliente. Revisar primero la vista previa sin crear pedidos ficticios. Instalación no equivale a aceptación de negocio ni al ensayo de ruta. Conservar catálogos y respaldos; no repetir instalador, bootstrap o importaciones.


## Incidencia de aceptación: canal administrativo ausente

El diagnóstico de sólo lectura del VPS confirma que falta SalesChannels.AdminManualCapture. El esquema actualizado conserva datos y no siembra canales automáticamente; la vista previa no consulta este canal, pero el guardado lo requiere. Se prepara una reparación explícita de una fila más auditoría atómica, sin reinstalar o ejecutar bootstrap. Conserva los perfiles aprobados y la referencia de captura. Ver [evidencia y validación](../validation/2026-10-30-i07-capture-incident.md). Reparación y guardado real pendientes de confirmación del usuario.
