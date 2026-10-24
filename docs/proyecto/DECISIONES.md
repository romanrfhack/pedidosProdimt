# Decisiones vigentes

Las decisiones de esta lista completan y, cuando existe conflicto, sustituyen las reglas documentadas en mayo de 2026.

| ID | Fecha | Decisión y fundamento |
|---|---|---|
| OCT-001 | 2026-10-24 | Reutilizar `pedidosProdimt`; conservar `prodimt` sólo como referencia. Ya existe API, persistencia, autenticación piloto y captura. |
| OCT-002 | 2026-10-24 | El usuario confirma que no hay instalación ni datos operativos existentes que conservar. No implica borrar el historial del repositorio. |
| OCT-003 | 2026-10-24 | Piloto con la ruta de la nueva fotografía: 23 candidatos, incluido el añadido a mano. Código de ruta y orden son configuración del piloto, no deducción automática de rutas históricas. |
| OCT-004 | 2026-10-24 | Icono llave = la empresa tiene llave y deja el producto dentro del local. Instrucciones internas separadas y pendientes de capturar. |
| OCT-005 | 2026-10-24 | Icono documento = pago diario a contraentrega con ticket. Si no aparece, condición desconocida; no inferir crédito/semanal. |
| OCT-006 | 2026-10-24 | Ticket de otro día = referencia de estructura; puede incluir venta y saldo anterior. No prueba pago, no aporta saldo vigente ni incorpora al cliente del ejemplo al piloto. |
| OCT-007 | 2026-10-24 | El repositorio es público: sólo código, documentación y ejemplos ficticios. Clientes reales, llaves, instrucciones interiores y datos comerciales se conservan en paquete privado. |
| OCT-008 | 2026-10-24 | Unidad de venta explícita y nullable cuando no está validada. Un producto sin unidad no puede generar una nueva línea válida. No rellenar historia desconocida como kg. |
| OCT-009 | 2026-10-24 | Asignación de máquina por cliente-producto antes de la asignación general del cliente; conservar dato efectivo en la línea. |
| OCT-010 | 2026-10-24 | Orden visual de productos frecuentes y posición para interpretar cantidades son conceptos distintos. Sólo un perfil aprobado puede sustentar futura interpretación automática. |
| OCT-011 | 2026-10-24 | Fecha operativa en `America/Mexico_City`; timestamps de auditoría en UTC. Hora de reenvío, fecha del servicio y hora límite son datos distintos. |
| OCT-012 | 2026-10-24 | El usuario autoriza el siguiente incremento del plan integral: ampliar campos y consulta de demanda. Esto no habilita todavía WhatsApp real, cobros, saldos calculados ni producción física. |
| OCT-013 | 2026-10-24 | Registro de continuidad en git: `CONTINUAR.md`, estado, plan, decisiones, bitácora e incrementos con validaciones. |

## Pendientes de negocio

1. Confirmar nombre/código operativo de la ruta y si el orden de la hoja es el de visita. No equiparar códigos históricos automáticamente.
2. Confirmar cada producto, unidad, máquina y posición del perfil privado. Nombres parecidos o sucursales diferentes no se fusionan sin evidencia.
3. Capturar las instrucciones reales de colocación de mercancía. No inventar “mesa derecha” o “barra” como asignación de un cliente.
4. Confirmar cobro y acceso cuando no hay icono, y tratamiento operativo cuando ambos aplican.
5. Confirmar fecha de servicio de las hojas de embarques, cuyos encabezados y mensajes no coinciden de forma segura.
6. Ratificar si sigue vigente el corte de pedidos a las 10:00 a.m. y la revisión de adicionales. Se conserva la regla existente mientras no se cambie explícitamente. La foto `10:30 AM` no responde esta duda.
7. Antes de etapa 3, acordar precios vigentes, criterio de cargo, periodos de crédito/corte, pagos parciales y aplicación de abonos.

Estos pendientes no impiden desarrollar ni probar con datos ficticios. Sí impiden declarar validada una carga operativa inferida de las imágenes.
