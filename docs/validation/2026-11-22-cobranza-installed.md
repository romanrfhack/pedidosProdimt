# Cobranza instalada — reporte del 22 de noviembre de 2026

El usuario ejecutó `PRODIMT_Cobranza_ccc419330104_v2.zip` desde WSL. El reporte final acredita instalación y verificación del destino. No es una inspección SSH independiente ni aceptación funcional de las operaciones de cobro.

| Campo | Resultado |
| --- | --- |
| Estado/fase | `updated/completed` |
| Fuente de aplicación | `ccc4193301043ec1935fb575d516319598fe94d0` |
| Instalador | `collections-preservation-v2`, corrección de `cacfc0d01e89680246d7ae2891e9e1ebc8a8ad31` |
| Backend/frontend | `collections-ccc419330104` |
| Historia SQL | 22; última `20261122201338_AddDeliveryCollections` |
| Destino SQL | `127.0.0.1,14330` / `ProdimtPedidos` |
| Salud/esquema/frontend | Verificados |
| Respaldo | COPY_ONLY verificado por el instalador |
| Configuración/tablas previas/permisos/conteos/seguridad/autenticación | Preservados |
| Recuperación pendiente | `false` |
| Restauración automática de base/secretos impresos | `false` / `false` |

Los hashes reportados coinciden exactamente con los archivos publicados del payload original, conservado por la revisión v2.

| DLL | SHA256 |
| --- | --- |
| `Prodimt.Pedidos.Api.dll` | `33cafe927dce3109380a60ee225d0bde7b48b0554228d0d1d7f5861a69e455b9` |
| `Prodimt.Pedidos.Application.dll` | `42abad18481f3947b3d8acfbcc3228499bc95e9682456beefe3d45cf7bc9514e` |
| `Prodimt.Pedidos.Domain.dll` | `34b364190e5d4274d601ed384b42663df5942c27961604915a1bfe792f858b0a` |
| `Prodimt.Pedidos.Infrastructure.dll` | `4d5e712de7a606c1cb50aa1ae355178adeb2a49369a4befa34615ee90853213f` |

El preflight del mismo intento confirmó Máquina con historia 21 antes de aplicar. El resultado final confirma la migración 21→22, DLL nuevas y ambos enlaces de release nuevos, superando el fallo anterior de metadatos vacíos. No se conservan como resultado final los hashes o la historia de preflight.

La comprobación humana pendiente consiste en recargar la aplicación y revisar `Mi reparto → Registrar entrega` para un cliente contraentrega, su resumen de cobros, y `Caja → Recepción de rutas / Adeudos por cliente` con una cuenta autorizada de Mostrador. Ningún pago real, recepción de efectivo o dato ficticio se registró desde este trabajo.
