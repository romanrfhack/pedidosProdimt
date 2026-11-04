// Execute actual application TypeScript in Node; no DOM, HTTP server or Angular fixture is simulated here.
// Angular dependencies and the compiler are the same locked versions used by the application build.
import { existsSync, readFileSync } from 'node:fs';
import { createRequire, registerHooks } from 'node:module';
import { fileURLToPath, pathToFileURL } from 'node:url';

const appPackage = new URL('../../apps/prodimt-pedidos-web/package.json', import.meta.url);
const appRequire = createRequire(appPackage);
const ts = appRequire('typescript');

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier.startsWith('.') && context.parentURL?.startsWith('file:')) {
      const resolved = new URL(specifier, context.parentURL);
      if (!existsSync(resolved)) {
        for (const suffix of ['.ts', '/index.ts']) {
          const candidate = new URL(resolved.href + suffix);
          if (existsSync(candidate)) return { url: candidate.href, shortCircuit: true };
        }
      }
    }
    try {
      return nextResolve(specifier, context);
    } catch (error) {
      if (error.code !== 'ERR_MODULE_NOT_FOUND' || specifier.startsWith('.') || specifier.includes(':')) throw error;
      return nextResolve(pathToFileURL(appRequire.resolve(specifier)).href, context);
    }
  },
  load(url, context, nextLoad) {
    if (!url.startsWith('file:') || !url.endsWith('.ts')) return nextLoad(url, context);
    const result = ts.transpileModule(readFileSync(new URL(url), 'utf8'), {
      fileName: fileURLToPath(url),
      compilerOptions: {
        target: ts.ScriptTarget.ES2022,
        module: ts.ModuleKind.ESNext,
        experimentalDecorators: true,
        useDefineForClassFields: true,
        inlineSourceMap: true,
        inlineSources: true
      }
    });
    return { format: 'module', source: result.outputText, shortCircuit: true };
  }
});

await import('@angular/compiler');
