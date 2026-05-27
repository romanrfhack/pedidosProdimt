# ADR-0002 — Excel no será fuente principal de verdad

## Estado

Propuesto.

## Contexto

El proceso actual depende de un archivo Excel con hojas diarias, fórmulas, vistas derivadas y captura manual.

El archivo es útil operativamente, pero mezcla:

- Catálogo.
- Captura.
- Cálculo.
- Producción.
- Reparto.
- Reportes.
- Vistas derivadas.

## Decisión

SQL Server será la fuente principal de verdad para pedidos.

Excel podrá usarse como:

- Fuente inicial para descubrir clientes y moldes.
- Semilla de datos.
- Exportación temporal para operación.
- Referencia histórica.

Excel no debe usarse como base en tiempo real de la aplicación.

## Consecuencias positivas

- Menos riesgo de errores por filas o fórmulas.
- Mejor control de permisos.
- Auditoría real.
- Consultas por departamento.
- Menos recaptura manual.
- Mejor base para WhatsApp y estadísticas.

## Consecuencias negativas

- Requiere migración o carga inicial.
- Requiere definir catálogo canónico.
- Al inicio puede convivir con Excel hasta reemplazar procesos.
