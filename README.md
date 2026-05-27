# PRODIMT Pedidos — Base documental inicial

Fecha: 2026-05-27

Este paquete contiene la documentación inicial para construir la aplicación de pedidos de PRODIMT con enfoque **mobile first**.

## Objetivo del proyecto

Sustituir el flujo manual actual de pedidos por WhatsApp, llamadas, mensajes en grupo y captura en Excel por un sistema donde cada cliente pueda confirmar o editar su pedido desde el celular, y donde PRODIMT pueda consultar los pedidos por área sin depender de transcribir cientos de mensajes.

## Documentos incluidos

- `docs/00-contexto-y-objetivo.md`: contexto operativo y objetivo del sistema.
- `docs/01-analisis-excel-actual.md`: hallazgos principales del archivo Excel actual.
- `docs/02-alcance-y-etapas.md`: alcance inicial, fuera de alcance y fases.
- `docs/03-requerimientos-funcionales.md`: requerimientos funcionales base.
- `docs/04-requerimientos-plus.md`: funcionalidades de valor agregado.
- `docs/05-arquitectura-propuesta.md`: arquitectura general propuesta.
- `docs/06-modelo-datos-inicial.md`: modelo conceptual inicial.
- `docs/07-backlog-mvp.md`: backlog inicial para la primera versión útil.
- `docs/08-contexto-para-codex.md`: instrucciones de continuidad para Codex.
- `docs/09-preguntas-abiertas.md`: dudas que deben resolverse antes o durante el MVP.
- `docs/adrs/ADR-0001-stack-tecnologico.md`: decisión técnica inicial.
- `docs/adrs/ADR-0002-excel-no-sera-fuente-principal.md`: decisión sobre Excel.
- `docs/reference/`: archivos de apoyo extraídos del Excel.

## Estado actual

- No hay código de aplicación generado todavía.
- Esta documentación define el alcance base para iniciar el repositorio.
- El Excel actual se usó como referencia para entender captura, moldes, clientes y vistas derivadas.
- La primera meta de desarrollo debe ser capturar pedidos correctamente, no reemplazar todos los reportes de producción desde el día uno.

## Regla de continuidad para Codex

Antes de crear o modificar código, Codex debe leer:

1. `docs/08-contexto-para-codex.md`
2. `docs/02-alcance-y-etapas.md`
3. `docs/03-requerimientos-funcionales.md`
4. `docs/06-modelo-datos-inicial.md`
5. `docs/07-backlog-mvp.md`
