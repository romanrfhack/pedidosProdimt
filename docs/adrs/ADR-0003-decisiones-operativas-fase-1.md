# ADR-0003 — Decisiones operativas para Fase 1

Fecha: 2026-05-27

## Estado

Aceptada.

## Contexto

El sistema debe sustituir gradualmente la captura manual de pedidos en Excel. Durante el análisis se confirmaron reglas operativas que afectan el diseño del dominio y el alcance del MVP.

## Decisión

Se adoptan estas reglas para Fase 1:

1. `x/X` significa no pidió.
2. Pedidos después de las 10:00 a.m. son tardíos y requieren revisión administrativa.
3. Un segundo pedido o cambio del mismo día requiere revisión administrativa.
4. El cliente puede tener hora o ventana deseada de entrega.
5. `Mostrador` es canal interno, no cliente externo.
6. La columna E del Excel es máquina asignada.
7. La máquina asignada es información interna y no se expone al cliente.
8. Fase 1 se concentra en capturar pedidos; producción, embarques y repartidores quedan para módulos posteriores.

## Consecuencias

- El modelo debe incluir estados y razones de revisión administrativa.
- El perfil de cliente debe incluir datos opcionales de entrega.
- El sistema debe distinguir cliente externo, canal de captura y canal de venta.
- El backend debe proteger endpoints para que datos internos como máquina no salgan en DTOs de cliente.
- La UI de cliente debe enfocarse en pedido rápido, no en operación interna.
