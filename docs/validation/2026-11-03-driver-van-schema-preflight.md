# I22 — Diagnóstico del preflight de esquema

## Evidencia recibida

El 3 de noviembre el usuario reporta la ejecución del ZIP `5fbf124e49a5b7ea422cc333a0a044b554726f75dec18a7ea8929b51c289ba92`. Verificación del archivo y copia al VPS correctas. La primera invocación termina con:

```json
{"revision":"20261102-delivery-install2","status":"failed","phase":"read-only-preflight","issue":"delivery_schema_unexpected","changes_made":false,"database_migrations_run":false,"migrations_may_have_changed":false,"service_restarted":false,"health_verified":false,"frontend_verified":false}
```

El encadenamiento `&&` impide la segunda invocación con `--apply`. No se ha instalado I22 ni ejecutado su validación SQL aislada. Los flags de salud no verificada no prueban indisponibilidad: el código llega a `/health/db` después de la comprobación que falló.

La consulta HTTPS independiente a `https://pedidos.prodimt.site/health/db` durante el análisis devolvió `{"status":"ok","database":"reachable"}`. Esto confirma la salud de la API actual, no la instalación de I22.

## Alcance del hallazgo

En `schemas()` la historia exacta se comprueba antes del contrato de reparto. Sin la migración I22, el contrato esperado contiene cinco listas vacías. Una prueba de esa comparación local pasa. Los contratos comercial, tokens, productos, seguridad y producción ocurren antes de reparto. No hay evidencia suficiente para corregir normalizando null, omitiendo claves o ignorando objetos; se conservan las guardas.

Fuente original de aplicación/paquete: `0cfe3ef511d80e4af032a3e64abd97391e86df08`.

- Instalador exportado: `706c78a0fb134f4d3ece20229d217edc72c540161ce56b5e3ab55840ec622b34`.
- Payload: `fb0485190d68285cc121603a3b946a19fb9c6a8a5f6e0da77ddfdcd5d079fb2d`.
- Esos archivos y sus pines se conservan. El bundle de continuación añade el diagnóstico; no implica recompilar ni sustituir la aplicación o el payload.

## Diagnóstico de sólo lectura

`PRODIMT_Diagnostico_Reparto_I22.py` verifica el instalador exportado, usa su cargador fijado y observa la comparación original sin modificar su retorno. Sólo llama al preflight. Adquiere el bloqueo existente sin crearlo ni escribirlo; no solicita SQL administrativo. La salida se limita a tipos, cantidades, propiedades conocidas y nombres de tablas del contrato. Las propiedades y valores desconocidos sólo se contabilizan. No imprime filas de clientes, pedidos, credenciales, rutas de configuración, errores crudos o definiciones SQL.

Una comparación fallida sigue fallando. No se cambia el esquema, no se conceden permisos y no se ejecutan migración, respaldo, staging, recuperación o reinicio. Con el JSON observado se decidirá si corresponde corregir el diagnóstico o investigar objetos reales.

## Validación local del diagnóstico

Pasaron 40 pruebas Python focalizadas: las 33 existentes de paquete/validación y siete nuevas de diagnóstico. Se ejecuta la composición real `make_installer`/`preflight` con transporte de lectura simulado: el mismatch conserva `delivery_schema_unexpected`; una comparación correcta conserva su retorno. Se rechazan instalador o payload alterados antes de ejecutar el código y se prueban bloqueo ocupado/ausente, rechazo sin root y ausencia de valores canario. Spies prohíben apply, rollback, stage, SQL administrativo y cambios de servicio. La CLI local sin privilegios termina con `diagnosis_root_required`, sin acceder al VPS.

Esto valida la herramienta, no prueba la causa del rechazo en el VPS. Se espera `status=observed` del diagnóstico, con el resultado original en `preflight`. Un exit0 del diagnóstico no indica instalación ni autoriza omitir el fallo que conserve ese resultado.

SHA256 del archivo de diagnóstico: `521905742d13a2e456a829b83881fc7d41331aa472e151013857b7f619cc6c9a`.

## Resultado real del diagnóstico y revisión 3

El usuario ejecutó el diagnóstico de SHA `521905742d13a2e456a829b83881fc7d41331aa472e151013857b7f619cc6c9a`. Ambos archivos fijados se verificaron, el bloqueo se adquirió y hubo una captura. `migrated_schema_expected=false`, `strict=false`, comparación false. La respuesta observada era un objeto sin propiedades conocidas ni desconocidas (`{}`), frente a las cinco listas esperadas; no se observaron nombres ni datos de objetos. Preflight falló con el código original. Ningún cambio operativo.

Microsoft documenta que FOR JSON omite propiedades NULL y que JSON_QUERY conserva un fragmento JSON. Esto es consistente con el resultado observado, pero la fuente exacta del NULL no se capturó: https://learn.microsoft.com/en-us/sql/relational-databases/json/format-nested-json-output-with-path-mode-sql-server y https://learn.microsoft.com/en-us/sql/t-sql/functions/json-query-transact-sql.

La revisión `20261103-delivery-install3` cambia únicamente la consulta: cada sección usa `JSON_QUERY(COALESCE((SELECT ... FOR JSON PATH),N'[]'))`, conservando proyecciones, scope, filtros, joins, orden y definiciones. `canonical` y `matches` permanecen estrictos: `{}`, secciones ausentes/null y objetos inesperados siguen rechazados. Se actualiza el helper fuente y se adapta, en memoria, el helper antiguo del payload exacto al cargar los módulos de contrato. El adaptador exige cinco expresiones antiguas o cinco ya corregidas; es idempotente y rechaza layouts distintos.

El instalador nuevo conserva fuente de aplicación y SHA de payload v2. Las consultas adaptadas se incluyen en la allowlist DML y son las que se pasan a la validación aislada del SQL exacto. También se usa la forma estricta administrativa antes de cualquier cambio. Las pruebas SQL temporales, limpieza, respaldo, migración y recuperación siguen siendo obligatorias.

Se entrega un instalador independiente para evitar volver a copiar el payload de 20MB. El ZIP completo se actualiza únicamente en instalador, instrucciones y checksums exteriores; el payload, la migración SQL, las cuatro DLL de aplicación y el runner se mantienen byte por byte. La fuente de continuación tiene un commit de herramientas posterior al commit de la aplicación compilada; ambos se distinguen explícitamente.

La corrección local no acredita la lectura real ni instalación remota. Siguiente paso: ejecutar v3 sin --apply, y sólo si termina correctamente, continuar con --apply, que verifica SQL aislado antes de producción.

### Cierre local de revisión 3

Pasaron las 47 pruebas Python focalizadas (40 anteriores más siete regresiones). La composición de preflight con transporte simulado reproduce la respuesta vacía del query antiguo y acepta las cinco listas del query corregido. Se comprueba la allowlist exacta, el uso de la misma consulta en validación SQL aislada, idempotencia/layouts y rechazo de `{}`, objetos inesperados y drift. No se ejecutó SQL real localmente; el gate obligatorio del VPS lo verificará antes de aplicar.

Artefactos fijados para entrega:

- `PRODIMT_Instalar_I22_v3.py`: 47,133 bytes; SHA256 `1c5dec8f3bd590f109ce76a6fb356af33b6e5eca78209fd2d2a62f7528cf5806`.
- `PRODIMT_Reparto_I22.zip` actualizado: 20,557,519 bytes; SHA256 `c307dc3e81413e660aebe4b8c4bd4d9966a38bda7faebf6662b93aa56f4106cd`.
- El SHA del payload continúa siendo `fb0485190d68285cc121603a3b946a19fb9c6a8a5f6e0da77ddfdcd5d079fb2d`; no es necesario volver a copiarlo al VPS.

La plantilla actual sólo sustituye `REPLACE_SOURCE_COMMIT` por la fuente de aplicación `0cfe3ef511d80e4af032a3e64abd97391e86df08` y `REPLACE_PAYLOAD_SHA256` por ese SHA para producir el instalador independiente. El script interior del ZIP contiene exactamente los mismos bytes. Se recalculan las instrucciones y SHA256SUMS exteriores sin alterar el payload. El bundle de fuentes se exporta desde el commit limpio de continuación y conserva toda la historia alcanzable.
