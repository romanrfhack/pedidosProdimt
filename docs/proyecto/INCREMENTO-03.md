# I03 — Instalación de prueba y ensayo operativo

## Hitos separados

**I03-A: preparación técnica.** Construir y verificar una instalación reproducible, con datos persistentes y credenciales propias. Preparar los catálogos confirmados y una hoja breve de los campos que faltan.

**I03-B: instalación del piloto.** Ejecutar el paquete en el destino elegido, comprobar acceso y persistencia, y cargar sólo datos revisados. Este paso requiere ejecutar los comandos en ese equipo; el éxito de CI no prueba una instalación en el VPS del usuario.

**I03-C: ensayo de una semana.** Comparar pedidos recibidos, demanda por máquina y preparación de la ruta, registrar diferencias y evaluar la salida de etapa 1. No puede declararse completado al construir software.

## Decisión de instalación

Se prepara Linux/WSL con Docker Compose como opción portátil. Después de completar I03-A, el usuario eligió su VPS y `pedidos.prodimt.site`. La instalación I03-B comienza con un inventario de sólo lectura del host; no se da por ejecutada por preparar scripts o comprobar CI. El paquete no requiere instalar Node ni .NET globalmente en el servidor.

- Frontend compilado para el mismo origen de la API.
- SQL Server Express en un volumen propio; puerto SQL no publicado.
- Aplicación escuchando inicialmente en loopback; acceso por túnel SSH para el ensayo técnico.
- Credenciales generadas en el destino una sola vez y conservadas al repetir el arranque.
- Inicialización explícita de migraciones, canales básicos y administrador propio.
- Arranque HTTP normal sin migraciones ni clientes, productos o tokens de demostración.
- Conexión privilegiada sólo durante inicialización; conexión de la aplicación con permisos limitados.

La exposición pública mediante dominio/HTTPS se prepara por etapas, con un virtual host propio, comprobación de conflictos y respaldo de sus cambios. El inventario del servidor debe revisarse antes de aplicar; no se modifican otros servicios ni se asume su configuración.

## Datos necesarios

Para capturar un pedido explícito se necesita identidad del cliente, producto vendible y unidad validada. Una posición de interpretación se necesita para futuras abreviaturas; no debe impedir la captura explícita. Para la demanda por máquina también debe definirse la máquina aplicable. Para comparar preparación por ruta hace falta el orden de visita validado.

Cobro, precios, pagos y saldos pertenecen a la etapa correspondiente. Conservar sus desconocidos no obliga a inventarlos para probar pedidos.

La hoja privada I03 conserva 23 clientes y 36 relaciones candidatas, con las confirmaciones de I02. Sus entradas pendientes se distinguen de los valores ya confirmados. No se ejecuta una carga completa de productos sólo por existir una descripción candidata.

## Criterios de ensayo

1. Abrir una jornada con fecha de servicio explícita y pedidos reales revisados por operación.
2. Confirmar o revisar los pedidos tardíos/adicionales antes de producir.
3. Comparar las cantidades por producto y unidad con la lista de máquina.
4. Comparar la preparación por cliente, variante y ruta; registrar cualquier diferencia.
5. Probar una corrección real autorizada y verificar que cambie la demanda una sola vez.
6. Comprobar que reenviar el mismo archivo no duplique pedidos.
7. Registrar resultado diario, responsable y pendientes; no confundir demanda con producto físicamente elaborado/entregado.

La bitácora del ensayo debe permanecer privada cuando incluya clientes o instrucciones de acceso. Los resultados agregados y las decisiones técnicas pueden registrarse aquí.

## Verificación de I03-A

El workflow añade una prueba de la instalación empaquetada en Docker: inicialización, arranque, mismo origen, ausencia de datos demo, permisos de acceso y persistencia al reiniciar. Las pruebas de API/SQL existentes siguen ejecutándose por separado. Consultar `ESTADO.md` para resultados efectivamente ejecutados y los límites pendientes.
