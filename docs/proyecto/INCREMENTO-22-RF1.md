> Contrato del incremento y evidencia de su fecha. La instalación/aceptación vigente se consulta en [ESTADO](ESTADO.md); los pendientes antiguos de este documento no sustituyen esa evidencia.

# RF1 — Conectar Pedidos con la preparación de reparto

## Solicitud y diagnóstico

El 3 de noviembre el usuario capturó pedidos y pidió completar la navegación hacia rutas, conteo, carga y entrega. I22 ya estaba instalado y repetido correctamente. La captura mostraba 23 registros: ocho por revisar y quince NoOrder; ninguno registrado/aceptado. La cuenta mostrada no tenía enlaces de reparto. La captura anterior distinguía el rol personalizado de dos permisos del rol canónico `Repartidor - Camioneta (sistema)`; el nombre similar no acredita permisos efectivos.

La consulta HTTPS pública del índice coincidió byte por byte con `frontend/index.html` del payload I22 instalado. No se consultaron cuentas ni pedidos operativos por API ni se modificaron asignaciones desde este entorno.

Son tres requisitos independientes: permisos efectivos, pedido elegible y vuelta con partidas asignadas. El código de ruta del cliente no crea una vuelta. Un pedido tardío pendiente conserva su revisión explícita. `Submitted` y `Accepted` son elegibles; NoOrder, rechazados y pendientes de revisión no lo son.

## Cambio autorizado

- Navegación Reparto visible para personal autenticado. Gestión abre `/admin/reparto`; consulta/operación propia abre `/reparto`; sin permisos abre una guía autenticada `/personal/reparto`. Las pantallas operativas conservan sus guardas.
- La guía explica el rol canónico, la persona que administra reparto, rutas/vueltas y cantidades. Puede volver a consultar los permisos de su propia cuenta; no concede permisos ni consulta datos de usuarios, pedidos o reparto. Seguridad aparece sólo con `security.manage`.
- Pedidos incorpora “Preparar reparto”, conteos del conjunto consultado y enlaces que conservan fecha. Los filtros de tarjetas no alteran estos conteos. Revisar pedidos aparece sólo con `orders.review`. Los nuevos enlaces se deshabilitan durante una corrección y una guarda impide perderla mediante el menú; guardar/cancelar permite navegar y cerrar sesión explícitamente permite salir.
- La revisión administrativa recibe `?date` para evitar revisar un día distinto al consultado.
- Gestión de reparto muestra una lista de preparación con choferes habilitados, rutas activas, vueltas abiertas y partidas asignadas. Reutiliza el dashboard y acciones existentes. No crea datos al consultar.
- Los mensajes vacíos distinguen falta de pedidos elegibles y pedidos ya reservados/entregados. Asignar requiere una vuelta abierta de la misma ruta y unidad confirmada.

Las cantidades por partida/unidad, Google Maps, observaciones, hora del servidor, bitácora, parciales y custodia permanecen en I22. No se modifica su ledger, API o SQL. Pedido50/carga25/entrega20 conserva 30 pendientes para el cliente y cinco bajo custodia hasta su resolución explícita. No se suman kilos con paquetes.

## Entrega y convivencia con otras actualizaciones

`PRODIMT_Reparto_Flujo_RF1.zip` es una actualización exclusiva del frontend sobre backend y frontend I22 `delivery-0cfe3ef511d8`, o una repetición de su propio release. Fija contenido base, fuente, payload y archivos; verifica índice/imports/chunks, salud y assets HTTPS. Usa snapshot privado, activación atómica del enlace y recuperación del frontend anterior ante fallo. No solicita credenciales SQL, ejecuta SQL ni reinicia la API.

RF1 se identifica por nombre propio: no reutiliza el número I23 del trabajo paralelo Horarios/Confirmación. Si esa u otra actualización ya cambió el release, RF1 debe rechazar la base antes de escribir. No ampliar la lista de releases para forzar su aplicación: primero integrar los cambios en una entrega común.

El paquete I22 anterior permanece como antecedente instalado; RF1 no obliga a volver a migrar. Las fuentes se conservan en el bundle autosuficiente existente, sin publicación GitHub. Compilar/empaquetar no equivale a instalar o aceptar operativamente.

## Aceptación en el destino

1. Tras lectura correcta y aplicación RF1, Ctrl+F5. Comprobar Reparto en la cuenta actual y “Preparar reparto” en Pedidos, con la misma fecha y conteos que los estados reales.
2. Un responsable autorizado revisa explícitamente los pedidos pendientes. Un responsable de accesos asigna el rol canónico o permisos equivalentes al chofer, y `delivery.manage` a quien coordina. La guía permite refrescar el acceso después.
3. En gestión, configurar el código de ruta confirmado, chofer habitual y ubicaciones; crear la vuelta de la fecha correcta y reservar partidas/cantidades reales. No inventar pedidos ni entregas para completar una demostración.
4. El chofer consulta sólo sus vueltas, abre Maps y registra conteo, carga y entrega reales. Verificar parciales, notas, horas y bitácora. La aprobación del pedido no confirma físicamente estas etapas.

La evidencia técnica y sus límites se registran en `docs/validation/2026-11-03-delivery-flow.md`.
