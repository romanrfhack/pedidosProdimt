#!/usr/bin/env python3
"""Export the guarded installer correction for the already built I23 payload."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
APPLICATION_SOURCE = 'aa802e210d15c70784f79dea2580709166b843c7'
PAYLOAD_SHA256 = 'f56edb2c88baf3cf389607988d960cba5ece4933a294d2e845677e27267ae4ba'
PREFIX = 'PRODIMT_I23_Correccion_SQL_v3/'


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def archive(files):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w', zipfile.ZIP_DEFLATED) as result:
        for name, raw in sorted(files.items()):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            result.writestr(info, raw)
    return stream.getvalue()


def build(output):
    if subprocess.check_output(['git','status','--porcelain'], cwd=ROOT, text=True).strip():
        raise ValueError('source_tree_not_clean')
    tool_source = subprocess.check_output(['git','rev-parse','HEAD'], cwd=ROOT, text=True).strip()
    template = (ROOT/'scripts/pilot/install-ordering-schedules-on-delivery-update.py').read_text()
    if template.count('REPLACE_SOURCE_COMMIT') != 1 or template.count('REPLACE_PAYLOAD_SHA256') != 1:
        raise ValueError('installer_template_unexpected')
    installer = template.replace('REPLACE_SOURCE_COMMIT', APPLICATION_SOURCE).replace('REPLACE_PAYLOAD_SHA256', PAYLOAD_SHA256)
    source_bundle = output.with_suffix('.source.bundle')
    output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(['git','bundle','create',str(source_bundle),'refs/heads/codex/i23-schedules-on-delivery'], cwd=ROOT, check=True)
    files = {
        'PRODIMT_Instalar_I23_v3.py': installer.encode(),
        'source.bundle': source_bundle.read_bytes(),
        'VALIDACION.md': (ROOT/'docs/validation/2026-11-03-schedules-sql-correction.md').read_bytes(),
        'source.json': json.dumps(dict(toolSourceCommit=tool_source, applicationSourceCommit=APPLICATION_SOURCE,
            payloadSha256=PAYLOAD_SHA256, rebuildRequired=False, sqlServerExecutedHere=False,
            githubPublished=False), indent=2).encode(),
        'LEEME.txt': '''PRODIMT I23 — CORRECCIÓN SQL DEL INSTALADOR v3

Reutiliza el payload ya compilado y copiado al VPS. No recompila ni reemplaza
binarios, frontend, configuración o tokens del payload.
Payload exigido: f56edb2c88baf3cf389607988d960cba5ece4933a294d2e845677e27267ae4ba.
Fuente de aplicación: aa802e210d15c70784f79dea2580709166b843c7.

Separa las cinco instrucciones revisadas en compilaciones SQL sucesivas,
manteniendo una sola transacción y la guarda del historial. El ensayo inyecta
THROW51023 con marcador único después del DDL/permisos y antes del COMMIT.
Exige ese error y verifica realmente que historial, esquema y huellas anteriores
no cambiaron. El mismo SQL se usa para probar y para aplicar. La corrección del
helper antiguo se hace en memoria sólo bajo sus hashes revisados; no edita el ZIP.

Lectura:
python3 /root/PRODIMT_Instalar_I23_v3.py --package /root/prodimt-ordering-schedules-i23-payload.zip
Aplicación autorizada, desde SSH con terminal:
repetir el comando con --apply.
Para validar aisladamente sin desplegar: --validate-only.

Antes de cambios operativos: tres pruebas EF/HTTP y SQL exacto en bases UUID
temporales con limpieza verificada. Después snapshot y respaldo COPY_ONLY/
CHECKSUM + RESTORE VERIFYONLY antes de migrar17→18. Conserva las demás guardas.
No restaura SQL automáticamente. Mantener payload, archivos y marcador si falla.

Si vuelve a detenerse, el JSON conserva códigos SQL y fase de validación sin
mensajes privados ni credenciales. Compartir el JSON. No se solicita aprobación
adicional para aplicar: el despliegue ya está autorizado y conserva sus pruebas.

Validación local: regresiones de normalización, detección estricta de51023,
stdout/stderr, rechazo de errores ajenos, conservación y limpieza.
No hay SQL Server/SDK .NET ni SSH disponibles aquí. El SQL real se ejecuta en el
VPS como condición previa; no se presenta esta entrega como ya desplegada.
'''.encode(),
    }
    files['SHA256SUMS.txt'] = ''.join(digest(raw)+'  '+name+'\n' for name,raw in sorted(files.items())).encode()
    output.write_bytes(archive({PREFIX+name:raw for name,raw in files.items()}))
    source_bundle.unlink()
    return dict(file=str(output), sha256=digest(output.read_bytes()), bytes=output.stat().st_size,
                toolSourceCommit=tool_source, applicationSourceCommit=APPLICATION_SOURCE,
                payloadSha256=PAYLOAD_SHA256)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    print(json.dumps(build(parser.parse_args().output.resolve()), indent=2))
