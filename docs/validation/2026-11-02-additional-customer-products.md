# I16 — Pedir otro producto

## Alcance implementado

Captura manual cliente/administración con selector del catálogo compartido y cantidad/unidad. Agregar sólo modifica el borrador; pedido, auditoría, solicitud por cliente-producto y recibo se guardan atómicamente. El pedido continúa con precio null y sin máquina heredada para productos todavía no configurados. La bandeja administrativa reúne referencias a pedidos, permite abrir el perfil completo y completar explícitamente precios iniciales pendientes por fecha/unidad, preservando precios fijados y cero real. Reintento durable y vista previa versionada en captura y resolución.

Nuevo flag de herencia de máquina conserva true en relaciones existentes; el administrador puede elegir Sin máquina / por asignar. No se implementa planificación de producción, clasificación de reventa, cobranza o RBAC nuevo. Administración piloto utiliza AdminAccess existente. I14/I15 y protección de accesos I12 se conservan.

## Validación local

- Build .NET Release correcto, sin errores; quince casos nuevos de Application correctos. Suite anterior 348 Domain/Application correcta antes de los dos casos finales; CI comprueba el total final.
- Modelo sin cambios pendientes contra migración `20261102152307_AddCustomerProductSetup`.
- Build Angular Production correcto, inicial 479.79kB dentro de 500kB; no se amplían presupuestos. Persisten advertencias CSS anteriores.
- 156 E2E correctos con Chromium, trece nuevos: modal/cancelar/foco/unidad/cantidad, guardar sólo al confirmar, conservar borrador tras conflicto, reintentos, ambos capturadores, bandeja, precio cero, perfil completo y recuperación tras recargar. Geometría sin desbordamiento en 320/768/1366px; capturas ficticias móvil/PC revisadas.
- 348 pruebas Python correctas, incluidas trece del instalador/paquete: pines, clave estable, respaldo obligatorio, migración/permisos transaccionales, rechazo de datos/entorno cambiados, repetición, preflight real con consultas permitidas, already_current y rollback sin restaurar SQL.
- Publicación .NET y frontend limpio preparados; las capturas/documentos reales del negocio no se incorporan al repositorio o paquete.

## SQL Server y CI

CI de checkpoint `a95bcf40084e14724619dd8a97eb02fa0aee7dac` comprobó cinco casos HTTP nuevos sobre SQL Server además de los históricos. Los ensayos históricos I12 y reparaciones I07 se fijan expresamente a sus migraciones originales; no se debilitan guardas de los instaladores.

Candidato `a1b035f40af440cb626f2bc3fe884f1e16253633`: build/tests .NET y smoke Production correctos; HTTP SQL Server correcto con prueba adicional de rollback al fallar auditoría. Regresión ODBC nativa verifica rollback DDL/historial cuando falta el principal de aplicación. Después detectó permiso SELECT anterior ausente en el principal ficticio: se reproduce ese permiso previo, sin ampliar los grants de I16. Cierre CI completo y hashes del paquete final pendientes en este checkpoint.

## Instalación prevista

Requiere frontend `customer-catalog-afb8f3ad9c33` y backend `access-tokens-65560ca3cf98`, esquema13, entorno Production y clave I12 existente/efectiva. Preflight de lectura, credenciales SQL ocultas al aplicar, snapshot privado api.env/configuraciones, respaldo COPY_ONLY/CHECKSUM verificado, parada breve de PRODIMT, esquema/permisos en una transacción13→14, activación y comprobación HTTP/archivos/permisos/clave. No cambia claves, contraseñas, tokens, Nginx o datos comerciales.

Permisos nuevos: solicitudes SELECT/INSERT/UPDATE y DELETE denegado; referencias SELECT/INSERT y UPDATE/DELETE denegados. Historial de precios sigue inmutable. Instalador admite repetición con SQL14 y aplicación anterior si falló después del commit; already_current no migra/reinicia. Fallo restaura enlaces/aplicación anterior con salud; no restaura SQL automáticamente. Conservar snapshot privado y backup SQL como conjunto de recuperación.

No se accedió al VPS/BD reales ni se desplegó I16. Instalación y aceptación remota corresponden al siguiente paso del usuario. No repetir importaciones/consolidaciones ni I12–I15.
