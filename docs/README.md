# Índice y autoridad documental

## Estado vigente

1. [Continuar](../CONTINUAR.md): contexto corto de la próxima sesión.
2. [Estado](proyecto/ESTADO.md): versión y evidencia actuales, sin reconstruir una cronología.
3. [Matriz funcional](proyecto/MATRIZ-FUNCIONAL.md): implementación, prueba y aceptación separadas.
4. [Arquitectura actual](proyecto/ARQUITECTURA-ACTUAL.md): dependencias verificadas y evaluación SOLID.
5. [Auditoría](audits/2026-11-03/AUDITORIA.md), [validación](audits/2026-11-03/VALIDACION.md) y [plan de consolidación](proyecto/PLAN-CONSOLIDACION.md).

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
