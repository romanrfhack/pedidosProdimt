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

## Siguiente trabajo

1. F03 resuelto por corrector y reporte I23; preservar guardas e incorporar la evidencia.
2. Candidato nativo C1 preparado para ejecutar los controles WSL. Se repitieron 499 pruebas .NET, 42 componentes, build y typecheck correctos. La suite SQL Server real pasó **101/101**, incluidas las cuatro regresiones nuevas de auditoría; cero fallos y omisiones, SQL Server 2022 Developer `16.0.4205.1`. Se conserva el primer pase 94/101 y la corrección de sus siete fixtures históricas, sin cambios nuevos de aplicación. Modelo EF sin diferencias pendientes. Python: **580 casos, 576 correctos, cuatro omisiones históricas y cero fallos**; incluye 47 pruebas nuevas C1. **Gate SQL exacto 18→19 y validador portátil 7/7 correctos en SQL Server real**: preservación de 33 tablas, metadata/permisos, Unicode, rollback/replay, rechazo de downgrade con pérdida y limpieza de las bases UUID. El VPS repetirá ambos controles antes de cambios operativos; la evidencia local no acredita despliegue.
3. Ejecutar desde WSL `PRODIMT_Desplegar_C1_WSL.sh`: fuente exacta y limpia, navegador completo de 240 casos con API simulada, después siete pruebas EF/HTTP SQL y SQL exacto en bases temporales antes de cambios operativos. Chromium local sigue bloqueado por `socket() failed: Operation not permitted`; cero assertions de navegador ejecutadas. El wrapper conserva `--prepare-only` y `--validate-only`; la opción predeterminada aplica sólo tras los gates.
4. Registrar el JSON del VPS: se espera `updated/completed`, 19 migraciones, salud/backend/frontend y preservación correctos, `recovery_pending=false`. Hasta recibirlo, conservar I23 como último despliegue confirmado. Si falla después de DDL, repetir el mismo paquete para recuperación hacia delante; no volver al instalador I23.
5. Después, completar cobertura SQL de Horarios y el recorrido UI/API/SQL de F13/F14; pulido visual, optimización medida y refactors por responsabilidad permanecen en su fase separada.

El plan contiene dependencias y aceptación; no se autorizan nuevas funciones por inferencia. Las correcciones de código están autorizadas; registrar pruebas y pendientes por hallazgo.

## Límites y preservación

Hay infraestructura y datos operativos existentes. No inicializar otra base, reiniciar cuentas ni sustituir claves. No forzar releases distintos ni quitar un gate para completar una instalación. El siguiente paquete deberá conservar RF1 y cualquier cambio integrado posterior.

Sin publicación GitHub en esta entrega: permanece el antecedente independiente pendiente. Fuentes/documentación se conservan en un bundle autosuficiente para revisión. La aceptación del flujo real de usuarios/rutas/vueltas todavía no queda acreditada por build, tests o assets publicados.

[Validación de la auditoría](docs/audits/2026-11-03/VALIDACION.md). [Continuidad histórica completa](docs/proyecto/historico/CONTINUAR-hasta-aa802.md). La bitácora conserva intentos y resultados fechados; no tratarlos como instrucciones actuales.
