# I23 — Horarios y revisión integrados sobre reparto instalado

Autorización: el usuario solicita continuar con implementación/despliegue el 3 de noviembre de 2026 (CDMX).

## Causa confirmada en los paquetes actuales

El instalador de horarios I22 admitía backend I19 y frontend I19/I21, con 16 migraciones. El bundle actualizado de reparto documenta instalación y repetición saludable de `delivery-0cfe3ef511d8`, migración 17 AddDeliveryOperations. Son dos desarrollos paralelos con la misma numeración. El fallo original se produce antes de cambios. Ampliar la lista de releases en el instalador viejo sobrescribiría la aplicación de reparto.

## Revisión3 — Fechas controladas en las pruebas de autorización

El usuario aportó Texto pegado.txt: revisión2/add27bf2 y fuente286b8ac verificados; Python528,525 correctas/tres omitidas; restore y compilación .NET10 correctos; dominio5/5; aplicación445,442 correctas/tres fallidas409. Se detuvo en pruebas-backend antes de EF, paquete nativo, SCP/SSH y cambios de producción. El registro no acredita despliegue ni ejecución SQL operativa.

Las tres pruebas fallidas (auditoría, detalle y revisión concurrente) creaban su pedido mediante /submit y dependían del reloj real. La regla nueva del servicio devuelve409 para hoy cuando el día de negocio es martes o ya pasó18:00. Se fija sólo el reloj de CustomerOrderService en esas tres fixtures a lunes2-nov09:00 CDMX; JWT, autenticación y validación de sesión conservan su reloj real. No se toca el servicio productivo ni sus restricciones.

Se añaden cinco casos HTTP para lunes09:00,17:59:59,18:00, martes09:00 y miércoles09:00, con expectativas200/409 y validación del mensaje mediante JSON. La factory compartida conserva constructor/configuración; la sustitución de servicio es local a cada fixture derivada.

Comprobado aquí: Python528,524 correctas/cuatro omitidas con umask077; diferencias productivas src/ y árbol frontend vacías respecto a286b8ac. No se dispone de SDK .NET ni ruta SSH; las tres correcciones y los cinco casos nuevos no se presentan como ejecutados. El runner mantiene todas las pruebas, guardas, umask077 y validación EF/SQL antes de desplegar. Fuente/frontend/payload con hashes; reemplazar ZIP por revisión3 y compartir salida. La web integrada validada de revisión2 se conserva sin recompilar.

## Revisión2: reproducción y corrección del fallo de preparación

La ejecución del usuario de la fuente10c419c se detuvo en pruebas-python con dos fallos y un error de test_resume_native.py. No alcanzó restore/build ni SCP/SSH. Reproducción exacta bajo umask077: test_reviewed_snapshot_and_complete_stage y los dos recorridos de recuperación fallan por resume_staged_directory_mode_unexpected.

El runner protegía archivos mediante umask077, mientras la fixture esperaba que mkdir(mode0755) produjera0755. La máscara dejaba0700. Se corrige exclusivamente la construcción de las carpetas sintéticas con chmod explícito0755. Se mantienen los controles reales de modo, propietario, enlaces, bytes y recuperación; no se cambian permisos de producción ni se relaja la máscara.

Después de corregir: suite508 completa pasa con máscaras022 y077 (504 correctas/cuatro omitidas). Después de integrar RF1 actual426c360e: suite528 bajo077,524 correctas/cuatro omitidas; Angular integrado compila. Los cuatro omitidos corresponden a condiciones del entorno; no se presentan como aprobados. Backend/SQL real siguen pendientes del runner y del VPS.

RF1 se entregó después de la primera versión de I23. La revisión2 conserva la guía de acceso, preparar reparto desde Pedidos, fecha y protección de corrección. Se admiten sólo los frontends I22 delivery-0cfe3ef511d8 o RF1 delivery-flow-426c360e1365 con el backend I22 exacto y las mismas17 migraciones. Cambiar los pines sin integrar la web habría sobrescrito RF1.

## Implementación

- Merge de las dos ramas, conservando todos los cambios de reparto y sus correcciones de herramientas.
- Horarios permanece como migración 20261103030000_AddOrderingSchedules, posterior a AddDeliveryOperations. Snapshot y Designer final incluyen ambos modelos; migraciones históricas intactas.
- I23v2 sólo admite backend delivery-0cfe3ef511d8 con frontend I22 del mismo nombre o RF1 delivery-flow-426c360e1365 y las 17 identidades exactas. No basta el número de migraciones.
- Prioridad cliente→ruta→10:00, borrar restaura herencia, corte18:00 y martes inhábil; solicitudes pendientes excluidas de producción/reparto hasta aceptación.
- Instalador nuevo separado, sin alterar los instaladores ya entregados. Conserva el rol personalizado y canónico de reparto y sus asignaciones. Huellas completas de seguridad/reparto, incluyendo revisión de seguridad y claves primarias reales.
- SQL de horarios revisado, transacción única con historial/grants. Ejecución condicional compilada dinámicamente para permitir repetición y evitar resolución prematura de columnas nuevas. El SQL raw permanece validado antes de esa transformación.
- Antes de stage/stop: tres casos EF/HTTP de reparto con los mismos cuatro ensamblados de aplicación y prueba SQL exacta aislada (rollback/repetición/permisos/conservación/limpieza). Las bases admitidas para la prueba se generan internamente con prefijos y UUID restringidos.
- Antes del DDL operativo: snapshot privado y BACKUP COPY_ONLY/CHECKSUM + RESTORE VERIFYONLY. Sin restauración SQL automática. Si ya migró y falla activación, conservar servicio detenido/marcador y recuperar con el mismo paquete.

## Antecedente: validación de la revisión1 con máscara022

- Compilación Angular de producción correcta sobre la unión. Se usaron las dependencias locales cuyo package-lock coincide byte a byte. Avisos existentes de presupuestos CSS, sin errores de compilación.
- Suite Python completa: 508 casos, 504 correctos y cuatro omitidos por condiciones de entorno. Incluye 13 regresiones nuevas sobre releases/historial exactos, respaldo antes de DDL, conservación de reparto/seguridad, binarios de validación idénticos, limpieza aislada, corrupción del paquete y fallo previo a stage/stop.
- Comprobación sintáctica Python y Bash correcta.
- Chromium local impide iniciar las 15 pruebas seleccionadas de horarios y reparto: `socket() failed: Operation not permitted`. No son E2E aprobadas.
- No hay SDK .NET en este entorno y el endpoint de descarga no es accesible. No se ejecutaron compilación ni pruebas backend de la integración.
- SSH al VPS: `Network is unreachable`. No se modificaron aplicación, configuración ni BD del VPS en esta sesión.

## Preparación y aplicación desde WSL

El archivo de preparación contiene el bundle completo de fuente integrada, frontend compilado y sus hashes. El runner obtiene SDK .NET10 en carpeta propia si falta; no lo instala en el VPS ni cambia el SDK global. Pruebas Domain/Application, compilación y coherencia del modelo EF deben aprobarse antes de generar y copiar un payload. Cualquier fallo detiene el flujo antes de aplicar. No se ejecuta SQL operativo desde WSL.

El paquete generado queda en `salida/` y se conserva con su hash/fuente para reintentos idénticos. `--prepare-only` no contacta al servidor. `--deploy` copia los dos archivos y ejecuta lectura seguida de aplicación interactiva con las credenciales SQL existentes en entrada oculta. No se incluyen ni solicitan secretos en el chat.

La construcción y las validaciones que sólo pueden ejecutarse en WSL/VPS siguen pendientes. El criterio de despliegue es JSON real `updated`, `completed`, `migrations_after=18`, salud/frontend y verificaciones de conservación verdaderas; después Ctrl+F5 y aceptación operativa.
