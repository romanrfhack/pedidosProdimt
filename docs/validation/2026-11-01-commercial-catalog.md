# I11 — Catálogo compartido e historial de precios

Fecha: 1 de noviembre de 2026. Datos técnicos ficticios; imagen y datos reales del usuario fuera del repositorio.

## Fuente y alcance final

Código `148a52dd7428cd1f948441f6709407a22be5944b`, rama `codex/i10-shared-products-prices`, PR #3 en borrador sobre `codex/i09-customer-order-links`. Incluye la base `9998e30` y el modal de tokens I10, sin sobrescribir su contrato/documento. Productos/precios fueron renumerados a I11 al integrar ese avance concurrente. I09 está instalado según reporte del usuario en la rama base; I10 independiente no tiene despliegue confirmado. I11 funciona sobre I09 con o sin ese modal ya instalado.

Un producto físico compartido se relaciona con clientes mediante la configuración existente. Consolidación exige vista previa, equivalencia explícita, unidad igual, motivo, huella actual y ausencia de colisiones. Archiva identidades anteriores y conserva referencias/códigos heredados para importación, sin reescribir pedidos. Precio por cliente-producto con vigencia, instante UTC, actor y motivo en revisiones anexadas; snapshots en nuevas partidas para futura cobranza. Correcciones de cantidad conservan el precio aplicado. Precio faltante/retiro es desconocido; cero explícito es gratuito. No hay tarifas iniciales supuestas, cargos, pagos, saldos, impuestos ni facturas.

## Validación

[CI 41329868536](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41329868536), cuatro trabajos correctos sobre el código empaquetado:

| Comprobación | Resultado |
|---|---|
| .NET, dominio y aplicación | 323 correctas: 5 + 318; 18 casos nuevos de aplicación. |
| SQL Server real | 46 correctas; 9 nuevas de precios/consolidación/contrato nativo. |
| Navegador | 114 correctas; 11 nuevas de consolidación/precios y 103 de base con modal I10. |
| Herramientas Python | 269 correctas, 19 nuevas del paquete nativo I11. |
| Modelo EF y migración | Sin cambios pendientes; SQL idempotente generado. |
| Production/ODBC existente | Smoke aislado correcto; contratos sqlcmd previos conservados. |
| Artefactos | Build Angular y publish .NET linux-x64 framework dependent correctos. |

SQL comprueba reintentos entre hosts, versión concurrente, aislamiento administrativo, conservación del precio histórico, rollback de precio+auditoría y consolidación+perfiles+recibo. Ejecuta el esquema y los permisos exactos del nuevo instalador, también bajo usuario db_datawriter: puede anexar revisiones, pero UPDATE/DELETE del historial quedan denegados. Se conserva la semántica de cero y los campos nullable de pedidos sin tarifa.

Los fixtures de reparación histórica permanecen en once migraciones con modelo/puerto de prueba correspondiente; no se relajan guardas de scripts entregados. La primera CI de este alcance descubrió esos fixtures antiguos y una opción enum faltante en una prueba nueva. La revisión final reprodujo además un motivo de 500 acentos que generaba 3,316 caracteres JSON para un campo general de 2,000: se mantiene el motivo completo en la revisión/recibo inmutables y se referencia desde la auditoría breve. Una regresión local falla antes de la corrección y pasa después; otra lo comprueba contra SQL Server. El código final corregido pasa todas las comprobaciones.

114 E2E locales y 269 Python locales también correctos. Se mantienen medidas de configuración 390/768/1366/1920 y los criterios previos de altura en laptop; el chip de precio no aumenta la fila. Aviso CSS de 7.55 kB sobre presupuesto orientativo 4 kB, dentro del límite de error 8 kB; no se amplían presupuestos. graphify update . no disponible (127), sin grafo anterior.

## Paquete y pines

- ZIP: `PRODIMT_Productos_Precios_I11.zip`, **5,742,775 bytes**.
- SHA256 ZIP: `0f97c7bb460b92c1070d9e0f333e49bbe1e60b1f4bcbf867754e0801affb463c`.
- Payload: `prodimt-commercial-catalog-payload.zip`, SHA256 `8a43682026c7da6d3dc4a3cf0ef0e47475bf1bfe7d7883748005053e32f6d436`.
- Instalador: `PRODIMT_Instalar_I11.py`, revisión `20261101-commercial-catalog-install1`.
- Carpeta: `prodimt-commercial-catalog-update`; LEEME y SHA256SUMS incluidos.
- Nuevo backend/frontend: `prices-148a52dd7428`.
- Migración: `20261101154618_AddSharedProductsAndCustomerPrices`, once→doce versiones.
- 61 archivos de payload, incluidos avisos de terceros; sin mapas, PDB, configuración Development, datos del piloto o credenciales reales.

Se verificó CRC de ambos ZIP, hashes de todos los archivos, pines de origen/payload y carga del instalador final. Inspector y soporte nativo incluidos coinciden byte por byte con el código revisado. El paquete es reproducible con los mismos insumos; la entrega se guardó después de validarla.

## Instalación y aceptación pendiente

Desde WSL: verificar SHA del ZIP, extraer, verificar SHA256SUMS, copiar instalador/payload y ejecutar primero sin --apply. Debe mostrar planned/completed, sin diferencias de base/canales/permisos. Si falla, conservar la salida y resolver la diferencia; no ejecutar bootstrap ni conceder permisos a ciegas.

Aplicación autorizada con SSH -t y --apply, usando las credenciales ocultas del administrador SQL existente. El instalador respalda configuración, prepara releases de forma atómica, detiene sólo PRODIMT, respalda SQL COPY_ONLY/CHECKSUM y ejecuta RESTORE VERIFYONLY antes de migrar. Verifica esquema, conteos y permisos anexables, conserva api.env/JSON productivos, activa enlaces y comprueba salud/endpoints/assets por HTTPS. Ante fallo recupera enlaces anteriores, sin restauración automática de SQL. Reintentar el mismo paquete ya instalado verifica already_current sin reiniciar.

Resultado esperado: updated/completed, migrations_after=12, migrated_schema_verified, existing_data_counts_preserved, price_history_append_only_verified, health_verified y frontend_verified true. No cambia contraseñas, Nginx u otros proyectos. No consolida ni precarga precios durante instalación.

Después de Ctrl+F5: Catálogos → Productos → Revisar productos duplicados. Revisar cada grupo, producto destino, nombre compartido, unidad e impacto; confirmar sólo equivalencias reales. Aprobar de nuevo perfiles afectados y comprobar posiciones/alias/máquinas. En Configuración del cliente abrir Precio e historial, registrar importe real por unidad, fecha de vigencia y motivo. Verificar precio distinto para dos clientes del mismo producto, cambio con historial/actor y conservación del precio de un pedido autorizado anterior. Históricos sin precio siguen pendientes. No reimportar catálogos ni aplicar después un paquete frontend anterior.

No se accedió al VPS ni se ejecutaron consolidaciones, precios o pedidos reales desde este entorno. Pendientes instalación y aceptación operativa del usuario. La etapa de cuenta/cobranza deberá definir la entrega que genera cargo y la valoración explícita de históricos sin precio; usará los snapshots, no la tarifa actual.
