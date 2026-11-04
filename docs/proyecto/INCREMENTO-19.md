> Contrato del incremento y evidencia de su fecha. La instalación/aceptación vigente se consulta en [ESTADO](ESTADO.md); los pendientes antiguos de este documento no sustituyen esa evidencia.

# I19 — Producción por máquina y asignación a pedidos

Autorizado el 2 de noviembre de 2026, America/Mexico_City. El usuario aprueba el rediseño de `/admin/operacion` y amplía el alcance para registrar producción física y distribuirla entre pedidos concretos. La implementación integra las ramas paralelas I16 e I18 sobre el diseño I17; no se encadenan sus instaladores históricos.

## Reglas confirmadas

- La configuración distingue «No requiere máquina», «Pendiente de asignar», herencia y máquina explícita. No deducir el significado de una máquina vacía ni clasificar productos por su nombre.
- Producción muestra solamente productos que requieren máquina, incluyendo los pendientes de asignación. Burreras/harina configuradas sin máquina quedan fuera; la demanda comercial se conserva.
- Asignar una máquina a la partida diaria no cambia la preferencia permanente del cliente-producto.
- Registrar kilos producidos y asignarlos a un pedido son acciones diferentes. Cada lote identifica fecha, máquina, producto y cantidad en kg. No convertir otras unidades.
- Una asignación identifica lote, pedido y renglón. Deben coincidir fecha, máquina, producto y unidad; no puede exceder el saldo del lote ni lo solicitado que aún está sin cubrir.
- No producir ni asignar automáticamente por recibir un aviso, por estado del pedido o por cantidad solicitada.
- Reintentos conservan su clave de operación. Validación, saldo, auditoría y recibo durable se guardan atómicamente; operaciones concurrentes no pueden reutilizar los mismos kilos.
- Revertir una asignación exige acción explícita y motivo; conserva historial. Una edición de pedido o reasignación de máquina no puede perder producción asignada silenciosamente.
- Los cambios de configuración se capturan en nuevas partidas y no reescriben snapshots históricos automáticamente.

## Cantidades y alcance

El resumen físico por máquina corresponde al día y a **todas las rutas**. El filtro de ruta limita los pedidos mostrados; el stock sin destinatario no se atribuye arbitrariamente a una ruta.

- Demanda asignada: kilos de partidas vigentes que requieren máquina.
- Producido: kilos registrados en lotes físicos.
- Por producir: sumar, por máquina/producto/unidad, `max(demanda - producido, 0)`; un excedente de un producto no compensa otro.
- Disponible para asignar: producido menos asignaciones vigentes.
- En cada pedido: solicitado, kilos asignados y cantidad pendiente de asignar.

## Interfaz

Ancho amplio en PC, tarjetas de resumen por máquina, botones multiselección para máquinas y «Sin máquina asignada», y tarjetas compactas por cliente. Al seleccionar varias máquinas se muestran sus renglones como unión, sin duplicar pedidos o cantidades. Una selección vacía tiene un estado explícito, no significa «Todas».

Se retiran Acceso y Cobro de las tarjetas de producción. Se conservan instrucciones útiles de preparación, fecha/ruta/pedido, productos y cantidades. Los avisos se compactan con su conteo visible; reconocerlos mantiene su semántica I04. Los formularios conservan el borrador ante refresco automático o errores.

## Autorización y persistencia

Conservar seguridad I18 por petición y añadir permisos del sistema `operations.assign`, `operations.produce` y `operations.allocate`; `operations.acknowledge` no habilita estas escrituras. El rol de sistema Administrador recibe las nuevas acciones; no se conceden privilegios a cuentas nuevas o roles ajenos automáticamente.

Persistencia aditiva: indicadores explícitos de necesidad de máquina, lotes y asignaciones. Conservar las migraciones publicadas I16/I18. El paquete I19 reconoce únicamente bases históricas conocidas, aplica las migraciones faltantes por identidad y requiere respaldo SQL verificado antes de cambiar esquema. Preservar configuración, claves, contraseñas y tokens; no restaurar SQL automáticamente ni degradar seguridad al recuperar binarios.

## Validación prevista

Builds .NET/Angular, modelo EF alineado, regresiones de I16/I18, pruebas SQL Server de atomicidad/concurrencia/saldos/compatibilidad, autorización y navegador en móvil/tablet/PC. Verificar paquete y SQL exacto antes de entregarlo. Instalación real y aceptación operativa se registran por separado; completar código o pruebas aisladas no acredita despliegue.

Estado: implementación y validación local completas; paquete generado y verificado. Aplicación `d3a11420b2025fa0a19f84e1796d5a8b252259a3`, ZIP `PRODIMT_Produccion_I19.zip` (7,510,549 bytes). Evidencia y hashes: `../validation/2026-11-02-production-allocation.md`. Publicación de la rama/PR bloqueada por revisión automática, pendiente de autorización explícita. Sin operaciones en el VPS ni datos reales.
