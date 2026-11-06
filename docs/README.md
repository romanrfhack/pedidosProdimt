# Índice y autoridad documental

C2: [seguimiento F13/F14](corrections/C2.md) y [comandos](../tests/Prodimt.Pedidos.C2.Tests/README.md).

## Estado posterior confirmado: C1 R5 instalado; C2 en trabajo

La evidencia posterior del usuario y su log WSL confirman **C1 R5 instalado**:
fuente `fdb81ea2f92011a74f2089642b5d3a9e46b26351`, backend/frontend
`consolidation-c1-fdb81ea2f920`, historia SQL **19**, última
`20261104041928_ExpandAuditMetadata`, AuditLogs **1111**,
`updated/completed`, `mode=apply`, salud/frontend/esquema y preservación
verificados, `recovery_pending=false`. [Procedencia y siete casos reales](validation/2026-11-04-c1-r5-installed.md).
La aceptación humana completa continúa pendiente. C2 trabaja aisladamente
F13 (HTTP/SQL Horarios) y F14 (recorrido real/comandos); no hay instalación C2.
No hacer push. Los apartados anteriores a esta evidencia son historia de su fecha.


## Estado vigente

1. [Continuar](../CONTINUAR.md): contexto corto de la próxima sesión.
2. [Estado](proyecto/ESTADO.md): versión y evidencia actuales, sin reconstruir una cronología.
3. [Matriz funcional](proyecto/MATRIZ-FUNCIONAL.md): implementación, prueba y aceptación separadas.
4. [Arquitectura actual](proyecto/ARQUITECTURA-ACTUAL.md): dependencias verificadas y evaluación SOLID.
5. [Auditoría](audits/2026-11-03/AUDITORIA.md), [validación](audits/2026-11-03/VALIDACION.md) y [plan de consolidación](proyecto/PLAN-CONSOLIDACION.md).

Seguimiento de correcciones autorizado: [C1](corrections/C1.md), con evidencia por hallazgo y límites de validación.

Preparación y validación del instalador propio: [candidato nativo C1 del 4 de noviembre](validation/2026-11-04-c1-native.md). El último despliegue confirmado sigue siendo I23; preparar un paquete no acredita su instalación.

## Contratos y antecedentes

- `proyecto/INCREMENTO-*.md` define contratos y decisiones del incremento correspondiente. Su sección de entrega refleja la fecha en que se redactó; para estado actual usar ESTADO.
- [Decisiones](proyecto/DECISIONES.md) enlaza sustituciones explícitas. [Plan integral](proyecto/PLAN.md) conserva el alcance funcional; consolidación es ahora la prioridad.
- `validation/` conserva pruebas e intentos fechados. Un fallo o pendiente antiguo no describe por sí solo el último despliegue.
- [Bitácora](proyecto/BITACORA.md) es cronológica. [Archivos de continuidad](proyecto/historico/CONTINUAR-hasta-aa802.md) preservan contexto completo.
- Los documentos numerados iniciales y `adrs/` explican el origen. Si difieren de una ampliación autorizada, prevalece el contrato actual y su decisión; no ejecutar una instalación inicial sobre la base operativa existente.

## Vocabulario de evidencia

| Estado | Significado |
|---|---|
| Diseñado | Contrato acordado, no acredita código |
| Implementado | Código localizado en una fuente identificada |
| Compilado | Build correcto para ese commit/entorno |
| Probado | Casos ejecutados con resultado y tipo de persistencia/entorno |
| Instalado | Resultado del destino o comprobación que lo acredita |
| Aceptado | Recorrido humano/operativo comprobado; no se deduce de publicar assets |

SQLite, InMemory, SQL Server, UI con API simulada y recorrido UI/API/SQL responden preguntas diferentes. Los reportes deben nombrarlos y registrar omisiones, sin agregarlos en un único conteo de cobertura.
