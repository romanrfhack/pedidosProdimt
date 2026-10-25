# Prompt SQL del actualizador — 25 de octubre de 2026

## Evidencia del VPS

El usuario ejecutó el script `20261025-env2` y obtuvo preflight completo: paquete original verificado, destinos anteriores esperados, configuración conservada y cinco migraciones previas. El lector informa diez asignaciones válidas y una ignorada por nombre; no muestra claves ni valores. Después falla en `admin-authentication` con `apply_requires_interactive_tty`. `changes_made` y `migrations_may_have_changed` son falsos. No se autentica SQL ni se detiene la aplicación en ese intento.

## Causa y cambio

La llamada `open('/dev/tty', 'r+')` crea un BufferedRandom que exige seekability. Una terminal real no es seekable y Python 3.12 lanza `io.UnsupportedOperation` antes del prompt, capturado como OSError por el script. Se reprodujo con una PTY que sí tiene terminal de control; cambiar opciones de asignación SSH no soluciona ese defecto.

La revisión `20261025-tty3` usa `open('/dev/tty', 'w', encoding='utf-8')` sólo para los prompts. La implementación Unix estándar de getpass usa ese stream para escribir y abre su propia entrada raw de terminal, desactiva eco y restaura los ajustes al salir. Se mantienen isatty, el tratamiento de GetPassWarning como error y el rechazo de una sesión sin terminal; no se incorporan contraseñas por argumentos, stdin inseguro o archivos. Los prompts de usuario y contraseña siguen ocultos.

## Verificación

- Suite Python local: **81/81 correctas**, sin omisiones; diff sin errores de formato.
- Cinco pruebas con PTY real/subproceso, sin sustituir open/getpass: autenticación ficticia aceptada sin eco, login inválido sin eco, EOF durante prompt y restauración exacta de termios, rechazo sin terminal sin consumir/mostrar stdin y reproducción del defecto original.
- Las respuestas se envían sólo después de observar cada prompt y comprobar que ECHO está desactivado. El proceso informa únicamente resultados booleanos/códigos fijos. No se usan identidades o contraseñas reales.
- El programa original falla en las pruebas positivas antes de mostrar prompts; el cambio de modo las hace pasar. Revisión independiente sin bloqueadores, contrastada con la implementación local de getpass de Python 3.12.

El ZIP, los binarios, el SQL y las reglas de respaldo/migración/recuperación permanecen iguales. El éxito de estos ensayos no acredita despliegue en el VPS; se necesita el JSON del nuevo intento, su comprobación de salud y la revisión posterior de la aplicación.
