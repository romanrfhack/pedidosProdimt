# Productos y orden por cliente: archivos preparados

El usuario solicita importar la configuración que ya completó. Los clientes están cargados e I06 instalado. Esta entrega prepara datos privados para la importación existente; no cambia código, esquema, credenciales ni VPS.

## Fuente y normalización

Se recuperó la copia devuelta de `Piloto_Datos_Pendientes_I03_20261024.xlsx`, hoja Productos, filas 13–48, que sustenta la revisión I04. Se cotejaron las identidades y descriptores con las 36 relaciones privadas de I02 y los códigos/nombres de los 23 clientes ya importados. Los originales permanecen intactos y ningún registro real se publica aquí.

- 36 relaciones y 36 posiciones positivas, únicas y consecutivas por cliente.
- 30 unidades kg; seis unidades vacías conservadas como desconocidas.
- 19 preferencias de máquina positivas; 17 pendientes: 16 celdas vacías y una con cero sin significado confirmado. Ese cero no se importa como máquina.
- Máquinas positivas distintas: 1, 2 y 3.
- El orden visual inicial sigue las posiciones explícitas del usuario. Continúan siendo campos distintos y editables por separado.
- Una identidad provisional de producto por relación, con nombre del cliente para garantizar nombres únicos sin consolidar equivalencias no confirmadas. No constituye un catálogo maestro común aprobado.
- Cantidades sugeridas vacías, sin convertir los números de la columna histórica «Adicional» en pedidos. Sin aprobación automática de perfiles.

## Entregable privado

`PRODIMT_Configuracion_Productos_Clientes.zip`, 13,013 bytes; SHA256 `bbfe17adeabe1ba5c28adb6c966bfdbd9cf6fb2d5733dc14dd9d390b55b0f3de`.

Incluye `01_Productos.csv`, `02_Maquinas.csv`, `03_Productos_por_cliente.csv`, `Revision_Configuracion.xlsx`, `LEEME.txt` y `SHA256SUMS.txt`. No incluye altas de clientes, tokens, contraseñas ni instrucciones de acceso a locales. Los tres encabezados coinciden exactamente con las plantillas actuales; CSV con BOM UTF-8 y CRLF. ZIP extraído y cotejado, CRC/checksums correctos.

## Verificación realizada

Se recuperó el paquete I06 previamente verificado y se comprobaron sus hashes exterior/payload y manifiesto. Se ejecutaron esos binarios localmente con runtime ASP.NET 10.0.13 obtenido de metadatos oficiales y SHA512 verificado. Credenciales sintéticas efímeras, sólo loopback y almacenamiento temporal InMemory. No hubo acceso al servidor operativo ni se publicaron los datos en CI.

La API autenticada validó sin errores y aplicó localmente productos, máquinas y frecuentes después de recrear la referencia de clientes en esa instancia temporal. Se consultaron los 23 perfiles y comprobaron sus 36 relaciones, nombres, unidades, orden visual, posición, cantidades vacías y preferencias exactas. Perfiles sin aprobación y cero pedidos creados. La revalidación de frecuentes fue correcta y mostró los 23 avisos de reemplazo esperados. Comprobado también el envío de CSV con BOM intacto. Esta prueba no equivale a persistencia en SQL Server ni a carga real.

El Excel de revisión se generó con Artifact Tool, con referencias de origen, filtros, paneles y validaciones. Conteos calculados de seis unidades y 17 máquinas pendientes. Se alteraron temporalmente unidad/máquina para comprobar que completar una no oculta el otro requisito y que los conteos se actualizan; restaurados antes de exportar. Cero errores de fórmula, cotejo de las 36 filas en archivo exportado y revisión visual correctos. Editar ese Excel no modifica automáticamente los CSV del paquete.

## Siguiente paso operativo

En `/admin/importacion`, Validar y Aplicar en orden: Productos, Máquinas y Productos frecuentes. Revisar primero los cambios propuestos, especialmente si los números de máquina ya existen o se editaron perfiles manualmente. Frecuentes reemplaza la lista completa sólo de los 23 clientes incluidos; no aprueba sus perfiles ni crea pedidos. El paquete no vuelve a importar clientes ni configura una máquina predeterminada por cliente a partir de una preferencia por producto.

Se esperan 36 filas de productos, tres máquinas y 36 filas de frecuentes para 23 perfiles. Las seis unidades desconocidas pueden permanecer en catálogo, pero deben resolverse antes de capturar pedidos de esos productos. Después revisar un perfil conocido en Catálogos → Clientes → Configurar, recargar y comparar contra la hoja. Aplicación real y resultado del usuario pendientes.
