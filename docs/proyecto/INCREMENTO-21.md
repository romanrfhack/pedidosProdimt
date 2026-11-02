# I21 — Pedidos con tarjetas responsivas, filtros y colores

Autorizado el 2 de noviembre de 2026, America/Mexico_City. El usuario solicita una pestaña Pedidos similar al catálogo de clientes actualizado, con filtros y colores de los pedidos del día.

## Comportamiento

- Ancho completo con márgenes y rejilla CSS automática mínima de 280px, limitada al 100% en móvil. Acciones al pie, textos completos y más columnas según el espacio disponible.
- Búsqueda por cliente/número sin distinguir acentos o mayúsculas. Filtros: Todos, Registrados/aceptados, Por revisar, No pide, Rechazados, Tardíos y Avisos pendientes. Conteos sobre todos los registros de la fecha; resultado visible tras combinar búsqueda/filtro y acción para limpiar.
- La fecha es la de servicio que ya usa el servidor. Predeterminada hoy CDMX; se puede consultar otra o volver con Hoy. El timestamp Enviado sigue siendo independiente. No se reinterpretan capturas de después del corte ni se filtra otra fecha silenciosamente.
- Verde para Submitted/Accepted; ámbar PendingAdminReview; gris NoOrder; rojo Rejected; desconocidos neutros y «Sin clasificar». Texto explícito acompaña al color. AcceptedWithChanges se reconoce por adminDecision y no se inventa otro estado.
- Conserva indicadores de tardío, revisión y cambios; avisos enlazan al plan de la misma fecha. Colores/filtros no aceptan pedidos ni crean NoOrder o producción.
- Refresco existente cada 15 s mientras la pantalla está visible, conservando filtros. Error de fondo conserva datos con aviso. Detalle/corrección ocupa toda la fila; redimensionar conserva instancia, cantidades y motivo. Durante corrección quedan pausados refresco y filtros.
- Historial, precios aplicados, unidades, permisos I18, correcciones y reintentos conservan contratos y lógica existentes.

## Base y límites

I19 integra I16/I18 sobre I17 y fue instalado por el usuario según continuidad del proyecto: fuente `d3a11420b2025fa0a19f84e1796d5a8b252259a3`, releases backend/frontend `production-d3a11420b202`, migraciones 13→16 y salud verificada. No hubo inspección SSH desde esta sesión.

Fuente recuperada de `PRODIMT_Produccion_I19_Cambios.zip` verificado, bundle exacto/checkpoint `77457633f5a510aca48855b5a9078adf0a66d271`. Rama local `codex/i21-responsive-orders`. La base I19 no está publicada por rechazo automático previo; no se elude con API u otro transporte. El código se conserva mediante bundle/parche incremental revisable y paquete descargable. No se afirma PR/CI remoto.

Sólo presentación/frontend; sin cambios de API, backend, dependencias, migraciones o datos. No se añaden consultas por buscar/filtrar ni se modifica producción, seguridad o captura de productos adicionales.

## Entrega nativa

Actualizador `PRODIMT_Pedidos_I21.zip`: `PRODIMT_Instalar_I21.py` y `prodimt-orders-catalog-payload.zip` dentro de `prodimt-orders-catalog-layout-update`. Base estricta I19, conserva backend/servicio y activa `orders-catalog-{source12}`. Fuente/payload fijados por hash, preflight de sólo lectura, snapshot privado, activación atómica, assets HTTPS y reversión del enlace ante fallo. Repetir verifica `already_current`; releases distintos se rechazan antes de escribir.

No SQL directo, DDL, claves, contraseñas, revocaciones o reinicios. No encadenar instaladores antiguos ni aplicar sobre otra versión nueva. Los archivos de fuentes se entregan por separado y no se copian al VPS.

## Validación y aceptación

Build Angular correcto, 15 pruebas específicas del paquete correctas y 14 E2E nuevas de Pedidos correctas en una ejecución final de 15.8s. Diseño, colores, conteos, búsqueda, filtros, refresco/errores, sólo lectura, CDMX, enlace a detalle y conservación de corrección se prueban en navegador. Siete anchos 320/390/768/1024/1366/1920/2560px; capturas con datos ficticios. La suite completa dio 204/205 correctas; la prueba restante de altura en Configuración falla igual sobre I19 sin cambios. El ZIP final se registra al cerrar en `../validation/2026-11-02-orders-catalog-layout.md`.

Instalación y aceptación visual del usuario pendientes; no se operó VPS/SQL real ni se enviaron mensajes desde esta sesión.
