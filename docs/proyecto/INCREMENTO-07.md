# I07 — Captura rápida de pedidos

Autorizado el 28 de octubre de 2026. Desarrollo y validación en curso; aún no instalado.

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

## Entrega prevista

Paquete nativo compilado fuera del VPS, instalador con respaldo COPY_ONLY/CHECKSUM verificado antes de DDL, preservación de configuración y cuentas, comprobación de frontend/API y reversión de aplicación ante fallo. No se restaura SQL automáticamente. No se reimportan catálogos. Herramientas: `scripts/pilot/install-quick-capture-update.py` y `build-quick-capture-package.py`. El instalador comprueba I06 activo, historial/esquema 10 o 11 y permisos de la aplicación; un esquema parcial se rechaza antes de detenerla. La configuración y los datos se conservan. Evidencia de pruebas y paquete se agregan al cierre.
