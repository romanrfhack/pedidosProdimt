# C2: niveles independientes y ejecución reproducible

Desde la raíz, .NET10 y Node24. El nivel C2 necesita Docker con daemon accesible;
no usa una instancia operativa ni solicita claves/contraseñas. No ejecutar contra
`pedidos.prodimt.site` ni `ProdimtPedidos`. [Alcance y matriz](../../docs/corrections/C2.md).

Comandos preservados de C1:

```bash
npm test --prefix apps/prodimt-pedidos-web
npm run typecheck --prefix tests/e2e
npm test --prefix tests/e2e -- --workers=1
```

Son, respectivamente, componentes sin DOM, typecheck estricto de specs y UI mock.
El último sigue colectando240 casos; no acredita SQL. Instalar previamente los
dos lockfiles con `npm ci --prefix ...` si aún no están disponibles.

Orquestación C2 completa (el directorio de salida debe ser nuevo y externo):

```bash
python3 scripts/dev/validate-c2.py --source "$(git rev-parse HEAD)" --output "/tmp/c2-all-$(date -u +%Y%m%dT%H%M%SZ)"
```

Sólo43 casos HTTP/SQL de Horarios, con preparación y limpieza propias:

```bash
python3 scripts/dev/validate-c2.py --source "$(git rev-parse HEAD)" --level http-sql --output "/tmp/c2-http-$(date -u +%Y%m%dT%H%M%SZ)"
```

Sólo los3 recorridos Angular/API/SQL, en otra base propia:

```bash
python3 scripts/dev/validate-c2.py --source "$(git rev-parse HEAD)" --level browser --output "/tmp/c2-browser-$(date -u +%Y%m%dT%H%M%SZ)"
```

Cada comando exige HEAD exacto/limpio y ascendencia de R5. Construye el runner,
enumera escenarios, prepara SQL2022 con UUID y credenciales sintéticas, migra
directamente a19, verifica identidad/permisos, arranca Kestrel en loopback,
ejecuta y limpia. El nivel browser compila Angular, comprueba tipos e instala
Chromium del lockfile. Los reportes y logs quedan sólo en la salida externa.
`gate.json` liga SHA, casos y hashes; un fallo/omisión/retry/error global o
limpieza pendiente impide `passed`. `--level` identifica expresamente qué nivel
se ejecutó: un pase parcial no es el pase completo C2.

La enumeración portátil y el sondeo sin SQL se pueden inspeccionar después del build:

```bash
dotnet build tests/Prodimt.Pedidos.DeploymentValidation/Prodimt.Pedidos.DeploymentValidation.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet tests/Prodimt.Pedidos.DeploymentValidation/bin/Release/net10.0/Prodimt.Pedidos.DeploymentValidation.dll --list-c2
dotnet tests/Prodimt.Pedidos.DeploymentValidation/bin/Release/net10.0/Prodimt.Pedidos.DeploymentValidation.dll --probe-c2
```

El sondeo sólo comprueba Kestrel `/health`; no conecta SQL. El portable nuevo
`--validate-c2 CONFIG_PRIVADA` comparte los43 escenarios xUnit, mientras
`--validate`/`--validate-c1` conservan3/7 históricos. La configuración privada la
genera el orquestador; no se edita con conexiones heredadas. El proyecto C2 está
separado de la suite SQL histórica para no exigir una configuración C2 a sus101
regresiones existentes.

Controles incrementales sin SQL:

```bash
python3 -m unittest discover -s tests/pilot -p test_c2_orchestration.py -v
dotnet test tests/Prodimt.Pedidos.C2.Tests/Prodimt.Pedidos.C2.Tests.csproj -c Release --filter FullyQualifiedName~C2SafetyTests
git diff --check
```

Para una terminación forzada, usar exactamente el directorio registrado:

```bash
python3 scripts/dev/validate-c2.py --recover /ruta/externa/de/esa/ejecucion
```

No se matan puertos ni se borran bases por comodín. El runner de recuperación
no funciona sin un registro de propiedad válido y rehúsa limpiar una ejecución
que siga activa. Las credenciales son privadas/transitorias; la recuperación
retira sólo el contenedor UUID y nunca genera un pase funcional.

Si Codex aborta por sockets/EPERM antes de assertions, ejecutar el mismo comando
desde una terminal WSL normal con Docker disponible. Conservar el gate fallido;
usar otra salida nueva. `docker info` debe funcionar primero. No convertir la
restricción del entorno en cambio de aplicación ni reinstalar C1.

CI `c2-real` configura el mismo comando sin filtros de archivos que excluyan
tests/scripts. Es configuración local hasta observar un run remoto. No hacer
push para satisfacer este criterio pendiente.
