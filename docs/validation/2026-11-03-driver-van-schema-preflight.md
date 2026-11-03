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
