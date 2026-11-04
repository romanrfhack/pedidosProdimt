# I23 — Despliegue confirmado por reporte del VPS

## Procedencia y alcance

El 3 de noviembre de 2026 el usuario compartió la salida completa del instalador
`20261103-ordering-schedules-on-delivery-install3`. El ZIP corrector se verificó
con SHA256 `18deb0c9c791260197bcd8c3ba14cf9953a6915c45a3fbfd74c3bd6cba475e46`.
La lectura terminó `planned/completed`; la ejecución `--apply` terminó
`updated/completed`. Esta evidencia confirma la instalación y sustituye el
pendiente de ejecución SQL/despliegue de los registros anteriores.

La fuente de aplicación instalada es
`aa802e210d15c70784f79dea2580709166b843c7`. El corrector procede de
`8621e964e4159917ae795d8b2cc439ca80baf8d7` y reutiliza el payload fijado
`f56edb2c88baf3cf389607988d960cba5ece4933a294d2e845677e27267ae4ba`.
Ambos releases reportados terminan en `ordering-schedules-i23-aa802e210d15`.
El reporte se recibió del usuario; no se hizo una conexión SSH independiente
desde este entorno ni una prueba de aceptación visual.

## Resultados reportados

| Control | Resultado |
| --- | --- |
| Instalador | `updated/completed`, modo `apply` |
| Migraciones | 17 → 18; esquema migrado verificado |
| Pruebas portátiles EF/HTTP | 3/3 correctas; limpieza verificada |
| SQL exacto aislado | Correcto; metadatos estrictos y permisos de aplicación |
| Rollback y reaplicación del SQL | Ambos verificados |
| Bases temporales | Limpieza verificada |
| Snapshot y respaldo SQL | Ambos reportados; respaldo verificado antes del DDL |
| Servicio | Reiniciado; `health_verified=true` |
| Frontend | `frontend_verified=true` |
| Datos existentes | Conteos conservados; historial de precios sólo anexado |
| Seguridad, producción y reparto | Permisos verificados; reparto existente conservado |
| Tokens y clave de cifrado | Conservados; clave efectiva verificada |
| Recuperación | `recovery_pending=false`; sin restauración automática de SQL |
| Contraseña/clave/tokens | Sin cambio de contraseña, creación/rotación de clave o revocación |

Las rutas privadas de snapshot y respaldo se mantienen en la salida del usuario;
no se reproducen en este registro. El resumen JSON adjunto conserva campos
verificables, sin credenciales ni datos de clientes.

El éxito real del SQL corregido cierra el bloqueo anterior
`validation_expected_rollback_not_observed`. No permite reconstruir el número
SQL perdido en aquel intento ni atribuirle una causa exacta retrospectiva.

## Siguiente validación operativa

No repetir el instalador para revisar la interfaz. Con capturas pendientes
resueltas, recargar con Ctrl+F5 y comprobar con permisos apropiados:

1. `/admin/horarios`: horarios por ruta y excepción por cliente; al borrar la
   excepción vuelve a heredarse el de ruta. Cambiar configuraciones reales sólo
   con la decisión operativa correspondiente.
2. `/admin/pendientes`: Pedidos por confirmar, detalle, decisiones y mensaje
   preparado para revisión manual en WhatsApp.
3. Con un caso controlado autorizado: captura fuera del corte automático queda
   pendiente y sólo se habilita para operación al aceptarla; no confundir la
   validación aislada con una entrega o un pedido real realizado.
4. Reparto: continuidad de acceso y presentación RF1 con la fecha correcta.

La aceptación visual y de negocio sigue pendiente. No se enviaron mensajes ni
se modificaron pedidos, horarios, roles o rutas reales desde este agente.

## Continuidad

Este cierre sólo añade documentación. Conserva la aplicación/payload instalados
y la autorización previa. La fuente completa se entrega por bundle
autosuficiente; la publicación GitHub mantiene su antecedente independiente.
