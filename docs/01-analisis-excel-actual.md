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

| Columna | Uso confirmado |
|---|---|
| A | Molde o categoría de producto |
| B | Maq / máquina / equipo, según formato de captura |
| C | Cliente |
| D | Pedido |
| E | Número de máquina que atenderá el pedido |
| F | Repartidor |

La columna D contiene la cantidad del pedido. Cuando aparece `x` o `X`, significa **no pidió**. En el sistema nuevo esto no debe tratarse como texto de pedido ni como cantidad cero sin contexto; debe representarse como un estado explícito de cliente sin pedido para esa fecha.

La columna E representa la máquina asignada para atender ese pedido. Esta asignación es interna y no debe mostrarse al cliente. Normalmente cada máquina tiene clientes asignados, pero un administrador puede cambiar la máquina en situaciones especiales.

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
- Estado de captura / no pedido / revisión / aceptación / rechazo
- Canal de captura o venta
- Máquina asignada de forma interna
- Hora o ventana deseada de entrega

## Mostrador

`Mostrador` no debe tratarse como cliente externo. Debe modelarse como un canal de venta interno o captura interna, para que no contamine las estadísticas de clientes ni las preferencias de moldes por cliente.

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
4. Mezcla captura, cálculo, logística, máquinas y reportes en el mismo archivo.
5. La marca `x/X` debe transformarse a un estado de negocio claro: no pidió.
6. Las hojas derivadas dependen de fórmulas y de nombres de hoja específicos.
7. La asignación de máquina no debe exponerse al cliente.

## Recomendación

Usar el Excel como fuente de descubrimiento y semilla inicial, no como base operativa permanente.

El sistema nuevo debe guardar pedidos en SQL Server y, si es necesario, exportar vistas con formato similar al Excel para transición operativa.
