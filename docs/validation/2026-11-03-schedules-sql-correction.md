# I23 — Corrección SQL del instalador nativo v3

**Cierre posterior:** el usuario reportó `updated/completed`, migraciones17→18,
salud/frontend correctos y pruebas SQL reales con rollback/reaplicación/limpieza
verificados. Ver [evidencia de instalación](2026-11-03-schedules-installed.md).
Los pendientes descritos abajo corresponden al estado previo a ese reporte.

## Evidencia del destino

El usuario reejecutó la preparación con el paquete compilado conservado. Se copiaron el instalador y payload completos. Inspección `planned/completed`, salud y base17 exacta correctas. Después de autenticación SQL pasaron las tres pruebas EF/HTTP portátiles y la limpieza. La prueba SQL exacta se detuvo con `validation_expected_rollback_not_observed`. No se creó snapshot ni stage ni se detuvo la API: cambios, migración y reinicio falsos.

Las compilaciones/pruebas previas del usuario ya acreditan fuente de aplicación `aa802e210d15c70784f79dea2580709166b843c7`, dominio5/5 y aplicación450/450, coherencia EF y publicación linux-x64. El payload verificado es `f56edb2c88baf3cf389607988d960cba5ece4933a294d2e845677e27267ae4ba`. No es necesario construir otra aplicación para esta corrección de herramientas.

## Qué se sabe y qué no se ha demostrado

El fallo significa que el cliente SQL no acreditó el error intencional después de las instrucciones del incremento. El código anterior sólo buscaba un marcador en stdout y descartaba los números SQL. El registro no permite demostrar si hubo error de compilación antes del marcador o un problema de reconocimiento del resultado. No se afirma un error207 observado.

El normalizador anterior ejecutaba las cinco instrucciones en un único `EXEC`, incluyendo añadir una columna y después usarla en un CHECK. La corrección compila cada instrucción después de ejecutar la anterior, dentro de la misma transacción y guarda. Es un endurecimiento concreto de esa secuencia; las instrucciones SQL originales se conservan literalmente, con cambios sólo de espacios externos y ejecución por lotes.

## Corrección y controles

- Cinco instrucciones revisadas: dos columnas, CHECK de cliente, tabla de horarios de ruta e historial. Cada una tiene su propia ejecución; no se admite una sexta instrucción ni un encabezado diferente. Un solo BEGIN/COMMIT y los grants permanecen dentro de la transacción.
- El ensayo del SQL exacto elimina únicamente el COMMIT final, emite el marcador y ejecuta THROW51023 en su propio lote con el marcador como mensaje. Se exige retorno no exitoso, código51023, marcador y ausencia de otros errores SQL, salvo3621 de terminación. Se leen ambos canales del proceso.
- La detección del error no sustituye la verificación: después deben conservarse el historial anterior, la ausencia de las columnas/tabla nuevas, los permisos y las huellas de reparto/seguridad. La repetición del incremento también debe pasar.
- Los errores conservan sólo códigos numéricos y fase fija. No se imprimen stdout/stderr crudos, mensajes SQL, nombres privados ni credenciales.
- El payload existente permanece byte a byte. El instalador sustituye en memoria el helper antiguo de SHA364e0fa2 por la copia revisada, de hash fijado. Un helper desconocido se rechaza. Los hashes de los demás archivos y ensamblados permanecen obligatorios.
- Mantiene tres pruebas EF/HTTP, base exacta, configuración/clave, limpieza aislada, respaldo COPY_ONLY/CHECKSUM y VERIFYONLY antes de DDL operativo y recuperación hacia delante tras migrar.

## Validación local y límites

Suite Python533:529 correctas y cuatro omitidas por condiciones del entorno, bajo umask077. Las18 regresiones focalizadas incluyen conservación exacta de las cinco instrucciones, transacción, respaldo/recuperación, streams de error, números inesperados, falsas marcas de éxito, corrupción y overlay por SHA. Compilación sintáctica Python correcta.

No hay SQL Server, sqlcmd, .NET SDK ni conexión SSH en este entorno. No se presenta esta corrección como SQL real probado ni despliegue. El instalador vuelve a exigir las pruebas reales en el VPS antes de tocar aplicación/BD operativa. Si falla, compartir el JSON con los nuevos campos `validation_sql_phase` y `validation_sql_error_numbers`.

## Fuentes técnicas

Se revisaron fuentes primarias para compilación de SQL dinámico, límites de lote y semántica del error intencional:

- https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-executesql-transact-sql
- https://learn.microsoft.com/en-us/sql/t-sql/language-elements/sql-server-utilities-statements-go
- https://learn.microsoft.com/en-us/sql/t-sql/language-elements/throw-transact-sql

La documentación sustenta el diseño; no demuestra la causa concreta del error del VPS, cuyo número no se registró en el intento anterior.
