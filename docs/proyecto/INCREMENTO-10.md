# I10 — Tokens de acceso en modal responsivo

Autorizado por el usuario el 1 de noviembre de 2026 al revisar I09 instalado. El formulario de tokens ocupa una columna de 290 px y exige demasiado desplazamiento en PC.

## Cambio

- Conservar en configuración un acceso compacto «Tokens de acceso» con el número de tokens.
- Abrir un diálogo nativo amplio, fuera de la cuadrícula de configuración, con nombre del cliente y cierre visible.
- En PC distribuir configuración y vista previa en dos columnas; historial a todo el ancho. En tableta y teléfono apilar contenido sin desbordamiento horizontal.
- Limitar la altura al viewport dinámico y desplazar sólo el cuerpo; cabecera y cierre permanecen visibles. Permitir Escape y fondo, restaurar foco al botón y bloquear el cierre mientras se crea, comparte o revoca.
- Cerrar/reabrir conserva teléfono, descripción, vencimiento y enlace generado de la configuración actual. Cambiar cliente o salir mantiene la limpieza de secretos vigente. No crear un nuevo token al abrir o cerrar.
- Mostrar errores y confirmación de revocación dentro del modal.

## Límites y aceptación

Reutiliza creación, copia, WhatsApp individual y revocación I09; sin cambios de API, SQL, autenticación, corte de entrega o sesión. El aviso del enlace distingue cerrar el modal de salir de configuración/recargar. Abrir WhatsApp continúa preparando el mensaje para envío manual.

Comprobar teclado/foco, borrador, enlace idéntico tras reapertura, una sola creación y medidas 320/390/768/1366/1920. Usar únicamente clientes y tokens ficticios en pruebas técnicas. Aceptación real del usuario posterior al despliegue.

## Distribución

Paquete nativo PRODIMT_Tokens_Modal_I10.zip, instalador PRODIMT_Instalar_I10.py y payload prodimt-token-modal-payload.zip. Sólo frontend sobre backend/frontend I09 customer-access-6cfdc7ff366a; nuevo release token-modal-<source12>. Pines SHA256, snapshot privado, activación por enlace, verificación de salud y assets HTTPS y reversión del frontend ante fallo. Sin migraciones, cambios de configuración/contraseñas, reinicio o reinstalación del backend.

## Cierre técnico

Código `89b51ddc3308b42e5ab9b4e0532826b568beb684`; [CI 41323199461](https://github.com/romanrfhack/pedidosProdimt/actions/runs/41323199461) con cuatro trabajos correctos: 103 E2E, 305 .NET, 37 SQL Server y smoke Production. Build Angular y 250 Python correctas; revisión independiente y visual de capturas completadas. ZIP final de 180,442 bytes, SHA256 `1ff5df6abbf1ce0066662c1dccb88e1ff503f4291f8fc9754d52c5434f0657fc`. [Evidencia, pines y límites](../validation/2026-11-01-token-modal.md). Instalación I10 confirmada por el reporte del usuario `updated/completed`, con salud y frontend verificados; sin migraciones, cambio de backend o reinicio. La aceptación en navegador sigue pendiente.
