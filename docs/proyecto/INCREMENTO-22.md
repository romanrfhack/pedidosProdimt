> Contrato del incremento y evidencia de su fecha. La instalación/aceptación vigente se consulta en [ESTADO](ESTADO.md); los pendientes antiguos de este documento no sustituyen esa evidencia.

# I22 — Mi reparto: camioneta, vueltas y entregas parciales

Autorizado el 2 de noviembre de 2026, America/Mexico_City, después de aprobar la propuesta funcional. Amplía el alcance histórico de Fase 1 para operar el reparto. Base recuperada I21 `496eb7094c05e3269f941ca6f87af6a5a88e4c98`, con I16/I18/I19 integrados, conservando posteriormente las correcciones del instalador I18.

## Resultado esperado

El repartidor inicia sesión con su cuenta interna y entra a Mi reparto. Consulta exclusivamente sus recorridos asignados, fichas e instrucciones de entrega, ubicaciones de Maps, productos y cantidades por cliente. Registra conteo, carga, entregas, observaciones y contingencias sin transcribir pedidos ni modificar la cantidad comercial.

El responsable de reparto configura rutas por sus códigos existentes, chofer habitual y recorrido efectivo por fecha/vuelta. Reserva cantidades de partidas concretas y distribuye pendientes entre posteriores vueltas o repartidores. Una asignación no acredita fabricación, carga o entrega.

## Invariantes de cantidades

- Pedido, reservado para una vuelta, contado, cargado y entregado son hechos distintos.
- Cada partida conserva producto, unidad, orden y referencia al pedido; nunca sumar kg con piezas, docenas o paquetes ni compensar una partida con otra.
- En el ejemplo ficticio pedido50 / asignado25 / contado25 / cargado25 / entregado20, el cliente tiene30 pendientes. Hay25 que aún no se cargaron y5 de la carga cuya custodia debe identificarse. No asumir que esos5 se perdieron o que están disponibles en planta.
- Una visita puede terminar parcialmente; el pedido sólo está completo al sumar las entregas de todas sus partidas y vueltas.
- Las reservas activas impiden prometer dos veces el mismo saldo. Entregar desde custodia posteriormente no crea otra carga ficticia.
- Una transferencia mueve custodia explícitamente entre recorridos y registra ambos responsables. Devolución, liberación de reserva y ajuste son acciones explícitas, con motivo e historial.
- Corregir un registro agrega evidencia; no borra ni modifica silenciosamente movimientos anteriores. El pedido original no se reduce al contar o entregar menos.
- No continuar con cantidades o identidades de partida que quedaron obsoletas por una corrección comercial; mostrar conflicto y exigir resolver el reparto afectado.

## Experiencia

- Ruta propia `/reparto`, entrada por permisos; administración de reparto separada en `/admin/reparto`.
- Fecha de servicio de CDMX, ruta y vuelta, lista ordenada por posición del cliente y acceso a cada destino.
- Ficha con horario, notas del pedido, llave nullable, instrucciones internas, modalidad de pago y ubicación. Datos desconocidos permanecen desconocidos.
- Las instrucciones del cliente y las observaciones de la visita son distintas. No importar al catálogo las fichas históricas incompletas del manual ni las coordenadas sin validar.
- Sugerir lo asignado al contar, lo contado al cargar y la custodia disponible al entregar. Cada sugerencia requiere confirmación explícita.
- Diferencias requieren motivo; la entrega parcial debe indicar qué ocurrió con la carga restante. Cero entregado significa intento sin entrega y requiere explicación.
- Hora, identidad y bitácora proceden del servidor. La UI presenta los instantes en Ciudad de México.
- Conservar borrador ante errores y revisar conflictos antes de enviar otra operación. El reintento de respuesta incierta conserva clave y contenido.
- Colores acompañados de etiquetas; controles táctiles, teclado decimal y diseño desde320px. Sin guardados ficticios sin conexión.

## Seguridad y compatibilidad

Permisos del sistema previstos: `delivery.read.own`, `delivery.operate.own`, `delivery.manage`. El rol canónico Repartidor - Camioneta (sistema) obtiene sólo consulta/operación propias. El rol personalizado homónimo anterior se conserva íntegro; no se adoptan ni trasladan sus asignaciones automáticamente. El administrador conserva acceso por sus permisos; no se convierte a los usuarios existentes en choferes ni se asignan rutas automáticamente.

La API verifica permisos efectivos por petición, cuenta activa y propiedad del recorrido. Una concesión directa tiene la misma validez que un rol, conforme I18. No exponer contraseñas, claves, tokens, datos de otros recorridos ni instrucciones internas a clientes externos.

Mantener los endpoints y estados comerciales anteriores. Se incluyen pedidos Submitted/Accepted sin revisión pendiente; se excluyen NoOrder, rechazados, cancelados, superseded y borradores. Producción I19 y reparto no se sustituyen mutuamente; contar manualmente no fabrica ni consume un lote automáticamente.

## Validación requerida

1. Flujo completo por partida y cantidades parciales50/25/20, observaciones y hora automática.
2. Pedido compuesto10+2 conserva sus componentes; las unidades nunca se mezclan.
3. Segunda vuelta, otro repartidor, transferencias/devoluciones y pendientes sin doble reserva.
4. Doble toque, respuesta perdida, clave reutilizada con contenido distinto y versiones obsoletas.
5. Propiedad de recorrido, cliente externo/anónimo y usuario sin permiso; revocación de permisos vigente.
6. Rollback atómico de reserva, movimiento, auditoría y recibo; concurrencia entre instancias.
7. Corrección de pedido después del conteo/carga/entrega sin pérdida de historia.
8. Build .NET/Angular, coherencia del modelo EF, pruebas focalizadas y regresiones pertinentes.
9. Paquete fijado por commit y SHA256; respaldo SQL verificado antes de migración aditiva y conservación de configuración, claves, tokens, cuentas y pedidos.

## Límites y entrega

Sin cobros, cargos, adeudos, facturación, control automático de envases, optimización de ruta, firma/fotos o sincronización offline. Las notas sobre impagos o cajas son observaciones, no movimientos financieros o de inventario.

La actualización nativa admite exclusivamente I19 `production-d3a11420b202` con frontend I19 o I21 `orders-catalog-867df56de3bb`, además de su propio reintento verificado. Una versión distinta requiere analizar su compatibilidad; no ampliar la lista silenciosamente. Migraciones publicadas y datos existentes se conservan.

Entregar aplicación y fuente revisable por separado, mediante paquete nativo y bundle/parche. El antecedente de rechazo de publicación de I19 permanece pendiente; desarrollar no autoriza eludirlo por otro transporte. No se opera el VPS durante implementación. Instalación y aceptación de la ruta real se registran por separado de compilación y pruebas locales.

Estado: implementación completa; 436 pruebas .NET y 33 Python del paquete/validador correctas, build Angular y modelo EF coherentes. El usuario autoriza desplegar para validar. La revisión 2 exige pruebas SQL reales en bases temporales antes de aplicar y deja la aceptación de navegador para después. Ver `../validation/2026-11-02-driver-van-deployment.md` para condiciones vigentes y `../validation/2026-11-02-driver-van-deliveries.md` para el antecedente de implementación.


## Instalación confirmada

El usuario reportó v4 updated/completed con migraciones 16→17, ambas validaciones SQL y limpieza correctas, respaldo verificado, configuración/datos/seguridad conservados y backend/frontend saludables. La repetición produjo already_current en lectura y apply sin nuevas mutaciones. Fuente compilada 0cfe3ef511d80e4af032a3e64abd97391e86df08. Quedan configuración del chofer/ruta y aceptación visual/operativa; ver ../validation/2026-11-03-driver-van-installed.md. No volver a ejecutar paquetes históricos para validar la interfaz.
