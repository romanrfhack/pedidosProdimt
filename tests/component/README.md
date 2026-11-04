# Pruebas de comportamiento sin navegador

Estas pruebas ejecutan las clases TypeScript de la aplicación y el inyector real
de Angular en Node.js 24. Las dependencias de transporte/almacenamiento se
sustituyen explícitamente dentro de cada caso para controlar respuestas
invertidas, errores y recargas. No renderizan HTML ni sustituyen Playwright,
SQL Server o la aceptación visual.

Desde la raíz del repositorio:

```bash
npm ci --prefix apps/prodimt-pedidos-web
npm test --prefix apps/prodimt-pedidos-web
```

Para ejecutar un archivo concreto:

```bash
node --import ./tests/component/register.mjs --test tests/component/admin-pending.test.mjs
```

`register.mjs` transpila el código original usando el TypeScript del lockfile de
la aplicación y precarga su compilador Angular. No copia métodos de producción
ni cambia su implementación. La transpilación **no comprueba tipos**; esa
comprobación, incluida la plantilla, corresponde a `npm run build` en la
aplicación. Los specs de navegador tienen su propio control estricto:

```bash
npm ci --prefix tests/e2e
npm run typecheck --prefix tests/e2e
npm test --prefix tests/e2e -- --workers=1
```

El último comando inicia Angular y la API simulada de Playwright. Requiere su
Chromium instalado y un entorno que permita iniciar el navegador. Un fallo de
inicio de Chromium se informa como limitación, nunca como prueba funcional
correcta.
