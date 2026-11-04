# Plan de consolidación de PRODIMT

Prioridad: estabilizar y hacer claro lo existente antes de añadir funciones. Base y hallazgos: [auditoría](../audits/2026-11-03/AUDITORIA.md).

## Avance C1

El usuario autorizó comenzar las correcciones. [C1](../corrections/C1.md) conserva el seguimiento por hallazgo. F03 se resuelve con el corrector y reporte posterior I23 (18 migraciones); la aceptación de navegador y las nuevas correcciones siguen separadas. El orden siguiente se mantiene para el trabajo restante.

## Secuencia recomendada

| Fase | Resultado | Esfuerzo orientativo | Dependencia |
|---|---|---|---|
| 0 | Fijar la base y reconciliar documentación | 0.5-1 días-persona | Inicio |
| 1 | Cerrar pérdida de trabajo y contratos de entrada | 4-6 días-persona | Base 0; cada arreglo en cambio pequeño |
| 2 | Convertir calidad y despliegue en controles repetibles | 3-5 días-persona | 0; pruebas de 1 pueden incorporarse en paralelo |
| 3 | Hacer el recorrido claro, consistente y atractivo | 4-6 días-persona | 1 y pruebas de comportamiento listas |
| 4 | Optimizar lecturas con medición | 2-4 días-persona | 2; línea base antes de cambiar consultas |
| 5 | Refactorizar por responsabilidad y cerrar endurecimiento | 4-6 días-persona | Regresión 2 y recorrido 3 estables |

Estimación de orden de magnitud:18-28 días de desarrollo, más validación operativa y tiempos de feedback; aproximadamente 4-6 semanas con una persona dedicada y apoyo de revisión/QA. No es una fecha comprometida. Puede cerrarse una primera entrega de estabilización antes de abordar refactors grandes. El diagnóstico SQL y la compatibilidad de dependencias pueden cambiar el esfuerzo.

## Fase 0: Fijar la base y reconciliar documentación

**Hallazgos:** F03, F19, F20.

**Entrega:** Estado único y evidencia I23; diagnóstico SQL acotado. La reconciliación documental ya queda entregada.

**Salida verificable:** RF1 instalado / I23 preparado diferenciados. Gate SQL explica su fallo; no se elimina ni se fuerza la migración.

## Fase 1: Cerrar pérdida de trabajo y contratos de entrada

**Hallazgos:** F01, F02, F04, F05, F06, F07, F08, F09.

**Entrega:** Recuperación de producción, rate limiting, lecturas/borradores consistentes, notas/auditoría compatibles y fecha preservada.

**Salida verificable:** Cada caso tiene regresión que fallaba antes y pasa después; replay conserva UUID y ningún dato se pierde al navegar o recibir 409.

## Fase 2: Convertir calidad y despliegue en controles repetibles

**Hallazgos:** F03, F13, F14, F15.

**Entrega:** SQL Server Horarios, gate nativo I22/I23, recorrido UI/API/SQL pequeño, typecheck/lint/analyzers y tratamiento de dependencias.

**Salida verificable:** CI confiable sin omitir SQL crítico. Build y scanner con resultados explícitos. Paquete sólo después de rollback/replay/limpieza correctos.

## Fase 3: Hacer el recorrido claro, consistente y atractivo

**Hallazgos:** F10, F11, F17, F18, F23.

**Entrega:** Revisión readonly, shell y tokens ligeros, estados/acciones claros, Maps por alcance real, accesibilidad y responsividad.

**Salida verificable:** 320/390/768/1440/1920 px, teclado y zoom 200%; el usuario completa el flujo con datos ficticios en staging sin perder contexto/borradores.

## Fase 4: Optimizar lecturas con medición

**Hallazgos:** F12, F16.

**Entrega:** Lecturas por lotes, filtros own en repositorio y viewmodels de UI; aviso comercial separado de revisión técnica.

**Salida verificable:** 25/100/500 pedidos medidos antes/después, consultas sin N+1 y ledger/aislamiento/resultados preservados.

## Fase 5: Refactorizar por responsabilidad y cerrar endurecimiento

**Hallazgos:** F21, F22, F24.

**Entrega:** Primeras extracciones de endpoints/importaciones/catálogo, errores tipados internos, retención/permisos/cabeceras y correlación.

**Salida verificable:** Contratos estables, extracciones revisables y pruebas verdes por corte. Restauración/recuperación ensayada en staging antes de cualquier cambio productivo.

## Primera entrega propuesta

Orden: resolver el diagnósticoF03; proteger producciónF01; limitar loginF02; corregir historia/fecha/borradoresF04/F05/F09 y validar entradas/auditoríaF06/F07. Añadir regresiones antes de extender el cambio. El gate I23 ya pasó según reporte posterior; C1 exige un nuevo gate que contemple su migración aditiva, sin reutilizar el instalador I23. F08 requiere decidir compatibilidad del endpoint heredado sin retirarlo a ciegas.

## Criterios visuales y operativos

- Una jerarquía común: título y fecha visibles, acción principal inequívoca, filtros/contadores juntos; secundarias con menor peso visual.
- Revisión ofrece detalle readonly de producto/cantidad/unidad/ruta/horario antes de aceptar, sin entrar a un formulario editable.
- En Reparto, pedido/asignado/contado/cargado/entregado/custodia se distinguen por texto y unidad; no confundir 20 entregados con pedido completo de 50.
- Estados vacíos explican permiso, revisión o asignación pendientes y muestran la siguiente acción que corresponde a esa cuenta.
- Inputs/borradores sobreviven al refresco y al cambio de tamaño; respuestas antiguas no cambian el contexto actual.
- Tokens CSS de color/espaciado/tipo/radio y shell explícito; reutilizar componentes ya existentes antes de añadir dependencias.
- Texto normal>=4.5:1, foco visible, teclado y zoom 200%; sin overflow a 320/390/768/1440/1920 px. Validar capturas ficticias en navegador.

## Política de refactor y entrega

- Cambios cortos por responsabilidad, con contratos HTTP/DTO estables, sin renombrar estados públicos ni migraciones publicadas.
- La capa Domain sigue sin EF/HTTP; Application depende de abstracciones; Infrastructure implementa persistencia. Una interfaz debe resolver un límite real.
- No exigir MediatR, NgRx, microservicios, repositorios genéricos o un framework visual para cerrar hallazgos concretos.
- No sustituir transacciones o recibos por comprobaciones sólo de UI; no paralelizar consultas sobre el mismo DbContext.
- Migraciones aditivas y justificadas, con respaldo, prueba de datos existentes y recuperación; no truncar evidencia ni reescribir pedidos históricos.
- Una entrega combina todos los cambios vigentes y reconoce backend/frontend/esquema por separado. No encadenar instaladores de bases incompatibles.

## Definición de terminado

1. El caso que originó el cambio tiene una prueba de comportamiento y falla con la versión anterior.
2. Build, tests pertinentes, análisis estático y controles de datos/seguridad pasan; omisiones y excepciones se explican.
3. Si cambia UI, comprobar móvil/PC, teclado, foco, error/conflicto, latencia y conservación del borrador.
4. Si cambia SQL/ledger, probar concurrencia, respuesta perdida, rollback/replay y preservación de historia/unidades.
5. Documentar implementado/probado/desplegado/aceptado por separado y conservar fuente/artefacto/evidencia.
6. Validar en staging; producción con cantidades reales sólo en acciones operativas explícitas. No insertar datos ficticios en el negocio.

## Exclusiones de este plan

Cobranza, facturación, tickets emitidos, evidencias foto/firma, inventario de cajas, offline, optimización automática de rutas y envíos de WhatsApp quedan para otro alcance. Completar fichas y asignaciones reales es configuración/aceptación, no una función que deba inventarse durante la auditoría.
