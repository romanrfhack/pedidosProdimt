# Validación I19 — Producción y asignación a pedidos

Fecha: 2 de noviembre de 2026, America/Mexico_City.

## Base y alcance

Rama `codex/i19-production-allocation`, unión de I18 `862e9d7ca7756933bc05ff2995c6a648948ae9b0` con I16 `3fb11fa3925283885fbab2a6d4aba5f8d850ddd7`, conservando I17. Las migraciones publicadas no se reescriben. Nueva migración `20261103010000_AddProductionAllocation`; modelo integrado de 16 migraciones, flags `RequiresMachine`, lotes y asignaciones. Nuevos permisos `operations.assign`, `operations.produce` y `operations.allocate`, añadidos únicamente al rol protegido Administrador.

Se verifican separación entre producción física y asignación al cliente, controles de saldo y demanda, coincidencia de fecha/producto/máquina/unidad, reintentos durables, transacciones serializables y reversión con motivo. Las correcciones compatibles conservan identidad del renglón; reducir por debajo de los kilos asignados, cancelar o alterar identidad exige resolver la asignación primero. Asignar máquina publica avisos para demanda retirada/nueva dentro de la misma transacción.

## Resultados locales

- Build Release de la solución .NET: correcto, cero advertencias/errores. Modelo EF sin diferencias pendientes.
- Suite Release final: Domain 5/5 y Application 396/396, ninguna omitida. Incluye las regresiones finales de avisos, asignación de máquina y snapshots sin requisito de máquina.
- SQL Server real aislado: pasada completa de 94 casos con 91 correctos y tres fallos de fixture histórica I11/I12. Se corrigió únicamente el contexto de siembra de esas pruebas, conservando sus migraciones y guardas publicadas; la clase afectada pasó 6/6. El caso posterior de asignación de máquina y avisos pasó 1/1, incluidos replay sin duplicado y bloqueo de cambio con kilos asignados. Los 94 escenarios quedan cubiertos con resultados correctos combinados; no se presenta como una única ejecución completa posterior.
- La clase SQL nueva de producción pasó 16/16: concurrencia sobre el mismo stock, idempotencia entre hosts, rollback de auditoría/recibo, reversión única, límites, incompatibilidades, ausencia de máquina, exclusión explícita, permisos revocados y migración repetida sin reotorgamiento.
- Angular production build correcto; inicial 473.59 kB y módulo de operación lazy 56.98 kB, sin ampliar presupuestos.
- Playwright completo: 189/191 inicialmente. Se corrigieron la altura de configuración tras unir navegación I16/I18 y el fixture de lecturas permitidas a `pricing.manage`. Focal final 11/11 (configuración responsive, permisos aislados y producción/impresión). Los 191 casos están cubiertos con resultados correctos combinados; no se afirma una única pasada completa posterior.
- Se verificaron cierre/rehacer ante respuesta perdida con misma operación, pausa del refresco durante edición, carga de catálogo cancelada, avisos fuera del plan, selección de cliente desde un lote, filtros y preservación del alcance impreso.
- QA visual con datos ficticios: producción 320/768/1440/1920 px y configuración a 1366 px. Sin desbordamiento de página ni superposición de controles; referencias técnicas, Acceso y Cobro retirados de las tarjetas de producción. Último ajuste de cabecera compacto exclusivo de producción verificado en sus cuatro casos responsive/impresión; root inspeccionó PC/móvil y configuración.
- Pruebas Python de instaladores y paquete: pasada completa final de 394 casos correcta, incluidas 14 pruebas del paquete I19.

## Transporte SQL nativo y paquete

`verify_production_sqlcmd_migration.py` pasó las tres bases conocidas (I12, I16 e I18), con 228 llamadas reales de SQLcmd ODBC 18 y el SQL exacto de `migration_sql_for_sqlcmd`. Verificó rollback ante GRANT del primer incremento faltante y de I19, fallo de auditoría posterior a DDL/seed/grants, 16 identidades de migración, 18 permisos de sistema, 3 defaults true, 24 columnas, 4 CHECK, 4 FK y 5 índices, permisos mínimos de la aplicación, conservación de precios/tokens/pendientes y repetición sin reotorgar roles o permisos retirados. El verificador se añadió a CI. SHA256 del SQL I19 exacto con grants: `c5820b8348a6358eb8f367fa11b52868dee4621122123b45ea9dac5b246964f6`.

La prueba corrigió la diferencia canónica ASCII/Unicode del CHECK de unidad sin debilitar la validación: se aceptan únicamente `UnitCode='kg'` y `UnitCode=N'kg'`. Se utilizaron SQL Server 2025 CU7/17.0.4065.4 y datos ficticios aislados; CI conserva su verificación adicional sobre SQL Server 2022.

El instalador fija commit/payload/hashes, verifica respaldo SQL antes de DDL y conserva configuración, claves, contraseñas y tokens. Recuperación con marcador privado avanza exclusivamente hacia I19 después de cambios de esquema; no restaura SQL ni vuelve a binarios que perderían restricciones de seguridad o integridad de producción. La lectura es el comportamiento predeterminado; aplicar requiere `--apply` y las credenciales SQL existentes en entrada oculta.

Paquete generado y verificado: `PRODIMT_Produccion_I19.zip`, 7,510,549 bytes, SHA256 `c9a862885c79910ce13d62c160eb9bc8a9db862c9c395b4a2a8b70016c744faa`. Aplicación/payload fijados a `d3a11420b2025fa0a19f84e1796d5a8b252259a3`; SHA256 payload `c523381d6953afd68764f31d24c9f7170cd2c495b8c96382cd28723814baa3e5`. Noventa archivos, veintisiete frontend/licencias, una sola entrada main y su grafo activo; sin residuos de builds anteriores, PDB, mapas ni configuración privada. AssemblyInformationalVersion contiene el commit correcto. CRC exterior/interior, hashes individuales, `SHA256SUMS.txt`, `load_payload` y creación del instalador exportado comprobados sin ejecutar preflight ni cambios remotos.

La revisión automática rechazó `git push` de la rama porque considera que falta autorización explícita para publicar código en GitHub. No se intenta eludir el rechazo con otro transporte o API. No se creó PR ni se ejecutó CI remoto; se conservan el commit local y una entrega de cambios con bundle incremental, parche y descripción de PR para revisión. La publicación queda pendiente de autorización del usuario.

Graphify no está instalado; `graphify update .` no pudo ejecutarse. No se afirma actualización del grafo.

## Despliegue

Sin SSH, instalación, migración o consultas sobre el VPS/BD reales. I17 sigue siendo el último despliegue confirmado por el usuario. La entrega del paquete y las pruebas aisladas no acreditan aceptación operativa ni instalación remota.
