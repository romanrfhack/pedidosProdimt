# Continuar PRODIMT

## Prioridad actual

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

## Siguiente trabajo

1. F03 resuelto por corrector y reporte I23; preservar guardas e incorporar la evidencia.
2. Preservar la validación nativa previa: 499 pruebas .NET y **101/101 SQL Server** correctas, incluidas las cuatro regresiones nuevas de auditoría; cero fallos y omisiones, SQL Server 2022 Developer `16.0.4205.1`. Se conserva el primer pase 94/101 y la corrección de sus siete fixtures históricas. Modelo EF sin diferencias pendientes. Python R3: **585 casos, 581 correctos, cuatro omisiones históricas y cero fallos**. **Gate SQL exacto 18→19 y validador portátil 7/7 correctos en SQL Server real**: preservación de 33 tablas, metadata/permisos, Unicode, rollback/replay, rechazo de downgrade con pérdida y limpieza de las bases UUID. R3 no modifica backend ni SQL. El VPS repetirá ambos controles antes de cambios operativos.
3. Cerrar R3: ya pasan **47/47 componentes y 16/16 casos focales de navegador**, incluidos los nueve fallos originales; tipado E2E correcto. Falta ejecutar el script canónico, incluido build, sobre el commit final limpio y obtener 240/240. La evidencia automatizada posterior al commit se adjuntará al paquete como `BROWSER_C1_VALIDACION.json` y `.md`, vinculada al mismo SHA. Después ejecutar desde WSL `PRODIMT_Desplegar_C1_WSL.sh`: repite navegador completo con API simulada, siete pruebas EF/HTTP SQL y SQL exacto en bases temporales antes de cambios operativos. El wrapper conserva `--prepare-only` y `--validate-only`; la opción predeterminada aplica sólo tras los gates. No omitir pruebas ni sustituir el gate por el diagnóstico o por el adjunto.
4. Registrar el JSON del VPS: se espera `updated/completed`, 19 migraciones, salud/backend/frontend y preservación correctos, `recovery_pending=false`. Hasta recibirlo, conservar I23 como último despliegue confirmado. Si falla después de DDL, repetir el mismo paquete para recuperación hacia delante; no volver al instalador I23.
5. Después, completar cobertura SQL de Horarios y el recorrido UI/API/SQL de F13/F14; pulido visual, optimización medida y refactors por responsabilidad permanecen en su fase separada.

El plan contiene dependencias y aceptación; no se autorizan nuevas funciones por inferencia. Las correcciones de código están autorizadas; registrar pruebas y pendientes por hallazgo.

## Límites y preservación

Hay infraestructura y datos operativos existentes. No inicializar otra base, reiniciar cuentas ni sustituir claves. No forzar releases distintos ni quitar un gate para completar una instalación. El siguiente paquete deberá conservar RF1 y cualquier cambio integrado posterior.

Sin publicación GitHub en esta entrega: permanece el antecedente independiente pendiente. Fuentes/documentación se conservan en un bundle autosuficiente para revisión. La aceptación del flujo real de usuarios/rutas/vueltas todavía no queda acreditada por build, tests o assets publicados.

[Validación de la auditoría](docs/audits/2026-11-03/VALIDACION.md). [Continuidad histórica completa](docs/proyecto/historico/CONTINUAR-hasta-aa802.md). La bitácora conserva intentos y resultados fechados; no tratarlos como instrucciones actuales.
