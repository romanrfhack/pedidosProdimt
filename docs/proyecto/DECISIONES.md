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
| OCT-014 | 2026-10-24 | El usuario devuelve la hoja revisada y autoriza I02. Se reconocen nombres, ruta y valores de acceso explícitos; no se infieren unidades, posiciones ni orden de visita que siguen vacíos. Fórmulas simples verificadas contra celdas fuente y caché. |
| OCT-015 | 2026-10-24 | La referencia externa del pedido pertenece a un origen estable y evita duplicarlo incluso si aparece en otro lote. La clave del lote identifica un intento completo; contenido diferente con la misma clave es conflicto. |
| OCT-016 | 2026-10-24 | La corrección administrativa requiere motivo, versión y clave de operación; reemplaza explícitamente el conjunto completo de partidas y conserva antes/después. Un adicional continúa siendo otro pedido sujeto a revisión. |
| OCT-017 | 2026-10-24 | La fecha de servicio administrativa se separa del timestamp de captura. Omitirla conserva hoy; la clasificación tardío usa el corte de la fecha elegida. No tomar fechas de revisión del Excel como fechas de servicio. |
| OCT-018 | 2026-10-24 | I02 requiere persistencia relacional para aplicar lotes o correcciones atómicas. La demo InMemory mantiene I01 y vista previa, pero no simula transacciones durables ni resultados guardados de I02. |
| OCT-019 | 2026-10-24 | La validación SQL de CI usa una instancia desechable y sólo datos ficticios. No cargar el paquete privado en workflows, logs, artifacts ni fixtures públicos. |
| OCT-020 | 2026-10-24 | El ticket confirmado y la política de cobro no son equivalentes en todos los casos: la marca positiva permite aplicar la regla de contraentrega ya confirmada; ausencia/No de ticket no prueba crédito o corte semanal. Se conserva evidencia privada. |
| OCT-021 | 2026-10-24 | Usuario autoriza continuar con I03. Se separan preparación técnica, instalación efectiva y ensayo de una semana; no se marca el ensayo como realizado por completar código o CI. |
| OCT-022 | 2026-10-24 | Paquete portátil Linux/WSL con Docker Compose, SQL Express aislado y aplicación en loopback con túnel SSH. El destino no fue seleccionado; no se ejecutan cambios remotos ni se modifican DNS/proxy/servicios existentes. |
| OCT-023 | 2026-10-24 | Production usa credenciales generadas en el destino, JWT propio y bootstrap explícito. El proceso HTTP no migra ni crea datos demo; la conexión habitual no tiene permisos de DDL. Repetir bootstrap no rota ni restablece cuentas. |
| OCT-024 | 2026-10-24 | Para pedidos explícitos se requieren cliente, producto y unidad; para demanda por máquina, asignación aplicable. Posiciones de interpretación son una confirmación posterior; cobro desconocido no se rellena para probar pedidos. |
| OCT-025 | 2026-10-24 | Usuario autoriza I04: una modificación comercial de la misma entrega corrige el pedido existente con total final, motivo e historial. Cambiar 15 a 20 kg produce diferencia +5; no crear otro pedido automáticamente a partir del mensaje «5 más». |
| OCT-026 | 2026-10-24 | Un cambio de demanda genera aviso persistente por revisión y máquina afectada, guardado con el pedido. Requiere reconocimiento explícito; recibir el aviso no significa haber fabricado o entregado la cantidad. |
| OCT-027 | 2026-10-24 | Reutilizar y ampliar la configuración de cliente existente en una pantalla enfocada. Orden visual y posición de interpretación siguen siendo distintos; guardar con versión protege ediciones simultáneas. |
| OCT-028 | 2026-10-24 | El usuario devuelve la hoja I03: 30 unidades kg y 36 posiciones válidas, seis unidades aún vacías, 16 máquinas vacías y una máquina cero sin significado confirmado. No asumir que todas las filas quedaron completas ni fusionar variantes por nombre. |
| OCT-029 | 2026-10-24 | La etiqueta «Adicional» en la evidencia fue una simplificación imprecisa de una columna original sin encabezado. Los valores se conservan como fuente; la hipótesis del usuario de que sean ampliaciones de pedido todavía no los convierte en cantidades actuales. |
| OCT-030 | 2026-10-24 | Usuario elige VPS y dominio; usar pedidos.prodimt.site. Preparar preflight de sólo lectura, aplicación aislada y Nginx/HTTPS propios. No sobrescribir otros proyectos. La red del entorno de trabajo no permite SSH directo; el usuario ejecutará los comandos desde WSL. |
| OCT-031 | 2026-10-24 | Empezar con captura administrativa asistida de WhatsApp/llamadas; la interpretación automática queda para otro incremento. Reintentos técnicos y duplicados comerciales entre canales son problemas distintos. Un segundo envío del cliente/fecha requiere revisión, no suma automática ni deduplicación por parecido. |

## Pendientes de negocio

1. La ruta ya fue confirmada en el XLSX privado; falta confirmar orden de visita. No equiparar códigos históricos automáticamente.
2. Completar las seis unidades pendientes y resolver máquinas faltantes/cero. Las 36 posiciones de la nueva revisión ya están completas. Nombres parecidos o sucursales diferentes no se fusionan sin evidencia.
3. Conservar las dos instrucciones explícitas proporcionadas y completar las faltantes para entrega con llave. No inventar ubicación interior.
4. El acceso con llave quedó explícito para los 23 clientes; completar política de cobro de quienes no tienen ticket y tratamiento operativo cuando ambos aplican.
5. Confirmar fecha de servicio de las hojas de embarques, cuyos encabezados y mensajes no coinciden de forma segura.
6. Ratificar si sigue vigente el corte de pedidos a las 10:00 a.m. y la revisión de adicionales. Se conserva la regla existente mientras no se cambie explícitamente. La foto `10:30 AM` no responde esta duda.
7. Antes de etapa 3, acordar precios vigentes, criterio de cargo, periodos de crédito/corte, pagos parciales y aplicación de abonos.

Estos pendientes no impiden desarrollar ni probar con datos ficticios. Sí impiden declarar validada una carga operativa inferida de las imágenes.
