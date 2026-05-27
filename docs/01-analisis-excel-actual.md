# 01 — Análisis del Excel actual

Archivo revisado: `05 EMBARQUES Mayo-04.xlsm`.

## Hojas principales

El archivo contiene hojas por día:

- LUNES
- MARTES
- MIÉRCOLES
- JUEVES
- VIERNES
- SABADO
- DOMINGO

También contiene vistas o auxiliares:

- `Maq. #2 Lunes`
- `Hoja1`
- `FORMATO`
- `GABY (3)`
- `Metricas Produccion`

## Estructura observada en las hojas por día

En las hojas por día, la captura principal está en las columnas:

| Columna | Uso observado |
|---|---|
| A | Molde o categoría de producto |
| B | Maq / máquina / equipo |
| C | Cliente |
| D | Pedido |
| E | Campo operativo asociado a línea, máquina o clasificación; requiere confirmación |
| F | Repartidor |

La columna D contiene la cantidad del pedido. Cuando aparece `x` o `X`, esa marca no suma al total numérico. Debe confirmarse si significa "no pidió", "contactado sin pedido", "pendiente" u otra cosa.

## Hallazgos cuantitativos

Estos conteos son aproximados y deben tratarse como candidatos, porque hay diferencias de escritura en nombres de clientes y moldes.

| Dato | Observación |
|---|---:|
| Líneas candidatas de cliente en LUNES | 306 |
| Líneas candidatas por hoja diaria | 296 a 312 |
| Clientes únicos estimados, sin `Mostrador` | 236 |
| Clientes que aparecen en los 7 días de plantilla | 213 |
| Moldes/categorías observados en hojas diarias | 23 |
| Clientes con un solo molde/categoría candidato | 172 |
| Clientes con dos moldes/categorías candidatos | 45 |
| Clientes con tres moldes/categorías candidatos | 19 |
| LUNES con cantidad numérica capturada | 159 líneas |
| LUNES con marca `x/X` | 130 líneas |
| Suma numérica observada en LUNES | 2400 |

## Interpretación funcional

El Excel funciona como una plantilla operativa por día. El usuario humano localiza el cliente y escribe el pedido en la fila del molde correspondiente.

Para la app, esta estructura debe transformarse a un modelo normalizado:

- Cliente
- Producto / molde
- Preferencias de cliente por producto/moldes
- Pedido
- Detalle de pedido
- Estado de captura / confirmación / entrega

## Vistas derivadas

`Hoja1` y `Maq. #2 Lunes` no parecen ser hojas de captura primaria. Son vistas derivadas con fórmulas `XLOOKUP` que consultan datos de hojas por día.

Ejemplos observados:

- `Hoja1` consulta principalmente la hoja `MIÉRCOLES`.
- `Maq. #2 Lunes` consulta principalmente la hoja `LUNES`.

Esto confirma que no conviene dar acceso al Excel completo a terceros. La app debe publicar vistas filtradas desde base de datos.

## Hoja `GABY (3)`

La hoja `GABY (3)` parece una matriz o catálogo operativo de clientes contra moldes/categorías por ruta. Puede servir para enriquecer el catálogo inicial, pero requiere limpieza antes de usarla como fuente automática.

## Riesgos del Excel actual

1. No está normalizado.
2. Depende de filas fijas y ubicación visual.
3. Tiene nombres escritos con variaciones.
4. Mezcla captura, cálculo, logística y reportes en el mismo archivo.
5. Requiere criterio humano para interpretar marcas como `x/X`.
6. Las hojas derivadas dependen de fórmulas y de nombres de hoja específicos.

## Recomendación

Usar el Excel como fuente de descubrimiento y semilla inicial, no como base operativa permanente.

El sistema nuevo debe guardar pedidos en SQL Server y, si es necesario, exportar vistas con formato similar al Excel para transición operativa.
