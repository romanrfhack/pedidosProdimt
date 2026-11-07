# Continuar PRODIMT

C2, 7 de noviembre: **gate local completo correcto**, F13 **43/43 HTTP/SQL Server**
y F14 **3/3 navegador/API/SQL**, limpieza verificada, `recoveryPending=false`.
Fuente probada `5e7d8e836f0951aefc613e0363d740b26ec59153`.
[Causa, corrección y reportes](docs/validation/2026-11-07-c2-wsl.md).
CI remoto y aceptación humana pendientes; C1 R5 sigue instalado. Sin push.

## Estado posterior confirmado: C1 R5 instalado; C2 en trabajo

La evidencia posterior del usuario y su log WSL confirman **C1 R5 instalado**:
fuente `fdb81ea2f92011a74f2089642b5d3a9e46b26351`, backend/frontend
`consolidation-c1-fdb81ea2f920`, historia SQL **19**, última
`20261104041928_ExpandAuditMetadata`, AuditLogs **1111**,
`updated/completed`, `mode=apply`, salud/frontend/esquema y preservación
verificados, `recovery_pending=false`. [Procedencia y siete casos reales](docs/validation/2026-11-04-c1-r5-installed.md).
La aceptación humana completa continúa pendiente. C2 trabaja aisladamente
F13 (HTTP/SQL Horarios) y F14 (recorrido real/comandos); no hay instalación C2.
No hacer push. Los apartados anteriores a esta evidencia son historia de su fecha.


## Prioridad vigente: R5, compatibilidad con el I23 real

R4 `4080152f4fd3ba25f94f3ec3e03bd929feda9b3d` pasó 240/240 en WSL,
pero el VPS lo rechazó en preflight por exigir AuditLogs=1100. El diagnóstico
de lectura del usuario confirmó **1111**, 18 migraciones y ambos releases
`ordering-schedules-i23-aa802e210d15`; no hubo cambios, respaldo, DDL ni reinicio.
El reporte del segundo gate R4 es `46de425f49dc2eaf1b09a6a136dbce9ba7ff2f2e90b73bc3be4b597d178a7241`,
distinto del primer pase adjunto. [Detalle y contrato R5](docs/validation/2026-11-04-c1-r5-permissions.md).

R5 exige el perfil fijo 1111, conserva los demás controles y persiste la referencia
inmutable antes de detener/migrar. Recuperación y rollback comprueban esa referencia;
la recuperación con migración ya verificada no repite DDL ni compara escrituras
legítimas nuevas con el snapshot antiguo. `already_current` comprueba fuente,
binarios, esquema19, permisos y salud sin inventar una comparación histórica.
No se modifica backend/frontend/.NET/SQL publicado ni se endurecen permisos.
F24 sigue separado: **1111 permite UPDATE/DELETE; no es append-only**.

Python observado: **74/74 controles C1**, suite completa **607 casos: 604 correctos,
tres omisiones de fixtures históricas que requieren root, cero fallos**. No hay
SQL Server/sqlcmd/Docker local aprobado en esta sesión. SSH directo aborta con
`socket: Operation not permitted`; no se ha observado ningún cambio remoto R5.
Los resultados de navegador posteriores al commit y los hashes del paquete se
guardan fuera del checkout en `REPORTE_C1_PARA_CHATGPT.txt`,
`RESUMEN_C1_PARA_CHATGPT.json` y `evidencias-r5/`. No convertir pendientes en pases.

Construir con el constructor desde esta rama canónica y fuente limpia. Se permite
reutilizar binarios R4 verificados sólo por igualdad de los 471 blobs relevantes
y de los 557 archivos del manifiesto original; la trazabilidad externa identifica
los archivos reutilizados y declara `recompiled=false`. Ejecutar el wrapper R5;
si el gate completo pasó y sólo se cortó la conexión, usar su nueva opción
`--resume-from-transfer DIRECTORIO_RESULTADOS` del mismo paquete. Nunca reutilizar
un gate R4 ni mezclar instaladores/payloads. VPS debe repetir portátil7/7 y SQL
exacto corregido en bases UUID antes del respaldo y DDL operativos. Tras DDL,
repetir exactamente ese paquete para recuperación hacia delante.

## Antecedentes de preparación C1, anteriores al preflight R4

El usuario autorizó continuar las pruebas pendientes y el despliegue de C1. C1 consolida reintentos, acceso administrativo, historial, borradores y validación de entradas. Estado por hallazgo: [C1](docs/corrections/C1.md); controles y evidencia del candidato: [preparación nativa del 4 de noviembre](docs/validation/2026-11-04-c1-native.md). **C1 no está desplegado**; no se modificaron cuentas, claves ni datos productivos.

Leer [ESTADO](docs/proyecto/ESTADO.md), [MATRIZ](docs/proyecto/MATRIZ-FUNCIONAL.md), [AUDITORÍA](docs/audits/2026-11-03/AUDITORIA.md) y [PLAN-CONSOLIDACIÓN](docs/proyecto/PLAN-CONSOLIDACION.md). Para arquitectura usar [ARQUITECTURA-ACTUAL](docs/proyecto/ARQUITECTURA-ACTUAL.md); no una propuesta inicial como descripción de código existente.

## Base y entorno conocidos

- Aplicación instalada: `aa802e210d15c70784f79dea2580709166b843c7`, I23 integrado con RF1.
- Backend/frontend: `ordering-schedules-i23-aa802e210d15`; 18 migraciones. Reporte `updated/completed` del usuario con SQL exacto, rollback/replay, permisos, limpieza y salud correctos.
- Integrados corrector `8621e964` y cierre `d8e1db45`, conservando la documentación de auditoría `aee17c7`.
- Ver [evidencia I23](docs/validation/2026-11-03-schedules-installed.md). No repetir instaladores ni confundir aceptación visual con instalación.
- El usuario autoriza comenzar las correcciones y documentar cada hallazgo atendido. C1 trabaja sobre esta base; los cambios nuevos no se consideran desplegados.

## Resultado WSL posterior

Chromium ya inicia en el WSL del usuario. Después del [pase inicial de 79 correctas, una fallida y 160 no ejecutadas](docs/validation/2026-11-04-c1-browser-wsl.md), el candidato R2 `e9ef18b73e534602df41a1df0fc221586a715e74` ejecutó los 240 casos: **231 correctos, nueve fallidos, cero omitidos y cero inestables**. El diagnóstico completo permitió separar un defecto real de revisión, selectores exactos de reparto y un incumplimiento de altura de configuración I08. La [revisión R3](docs/validation/2026-11-04-c1-browser-r3.md) registra las correcciones y los controles focales. **Todavía no se ha conectado al VPS ni desplegado C1.**

La repetición canónica de R3 completó los 240 casos: **239 correctos, uno
fallido, cero omitidos y cero inestables**. El único fallo fue una altura de
770 px frente al máximo de 769 en 1366×768; el wrapper se detuvo antes de
SCP/SSH. [R4](docs/validation/2026-11-04-c1-browser-r4.md) añade holgura sólo al
padding vertical de escritorio, sin elevar el límite ni ocultar controles; los
cuatro viewports focales pasan. Falta fijar el nuevo SHA, reconstruir el paquete
y ejecutar el gate completo de ese SHA. C1 continúa sin desplegar.

## Siguiente trabajo

1. F03 resuelto por corrector y reporte I23; preservar guardas e incorporar la evidencia.
2. Preservar la validación nativa previa: 499 pruebas .NET y **101/101 SQL Server** correctas, incluidas las cuatro regresiones nuevas de auditoría; cero fallos y omisiones, SQL Server 2022 Developer `16.0.4205.1`. Se conserva el primer pase 94/101 y la corrección de sus siete fixtures históricas. Modelo EF sin diferencias pendientes. Python R3: **585 casos, 581 correctos, cuatro omisiones históricas y cero fallos**. **Gate SQL exacto 18→19 y validador portátil 7/7 correctos en SQL Server real**: preservación de 33 tablas, metadata/permisos, Unicode, rollback/replay, rechazo de downgrade con pérdida y limpieza de las bases UUID. R3 no modifica backend ni SQL. El VPS repetirá ambos controles antes de cambios operativos.
3. Cerrar R4: preservar los resultados R3 y el gate 239/240 que detectó el margen vertical. Ya pasan **47/47 componentes, los 16 casos focales R3 y los cuatro viewports focales R4**. Fijar el nuevo commit limpio, reconstruir frontend/paquete y ejecutar el script canónico para obtener 240/240. La evidencia posterior al commit se adjuntará como `BROWSER_C1_VALIDACION.json` y `.md`, vinculada al mismo SHA. Después ejecutar desde WSL `PRODIMT_Desplegar_C1_WSL.sh`: repite navegador completo con API simulada, siete pruebas EF/HTTP SQL y SQL exacto en bases temporales antes de cambios operativos. El wrapper conserva `--prepare-only` y `--validate-only`; la opción predeterminada aplica sólo tras los gates. No omitir pruebas ni sustituir el gate por el diagnóstico o por un adjunto de otro SHA.
4. Registrar el JSON del VPS: se espera `updated/completed`, 19 migraciones, salud/backend/frontend y preservación correctos, `recovery_pending=false`. Hasta recibirlo, conservar I23 como último despliegue confirmado. Si falla después de DDL, repetir el mismo paquete para recuperación hacia delante; no volver al instalador I23.
5. Después, completar cobertura SQL de Horarios y el recorrido UI/API/SQL de F13/F14; pulido visual, optimización medida y refactors por responsabilidad permanecen en su fase separada.

El plan contiene dependencias y aceptación; no se autorizan nuevas funciones por inferencia. Las correcciones de código están autorizadas; registrar pruebas y pendientes por hallazgo.

## Límites y preservación

Hay infraestructura y datos operativos existentes. No inicializar otra base, reiniciar cuentas ni sustituir claves. No forzar releases distintos ni quitar un gate para completar una instalación. El siguiente paquete deberá conservar RF1 y cualquier cambio integrado posterior.

Sin publicación GitHub en esta entrega: permanece el antecedente independiente pendiente. Fuentes/documentación se conservan en un bundle autosuficiente para revisión. La aceptación del flujo real de usuarios/rutas/vueltas todavía no queda acreditada por build, tests o assets publicados.

[Validación de la auditoría](docs/audits/2026-11-03/VALIDACION.md). [Continuidad histórica completa](docs/proyecto/historico/CONTINUAR-hasta-aa802.md). La bitácora conserva intentos y resultados fechados; no tratarlos como instrucciones actuales.
