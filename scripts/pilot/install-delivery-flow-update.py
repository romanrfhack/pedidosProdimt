#!/usr/bin/env python3
"""Install the reparto integration frontend on the exact installed I22, with rollback.

No SQL access, credentials, migrations, backend changes or service restart.
"""
import argparse
from datetime import datetime, timezone
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import posixpath
import re
import resource
import signal
import shutil
import stat
import subprocess
from urllib.parse import quote
import uuid
import zipfile

REVISION = '20261103-delivery-flow-install1'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
OLD = 'delivery-0cfe3ef511d8'
BACKEND_RELEASE = OLD
ENV_FILE = Path('/etc/prodimt-pedidos/api.env')
BASE_API_SHA256 = '0a7c1a74ad74c6ee91d4ac48044d44605ddf2c421c681f459be14c3625b678e9'
BASE_BACKEND_HASHES = {
    "Prodimt.Pedidos.Api.dll": "0a7c1a74ad74c6ee91d4ac48044d44605ddf2c421c681f459be14c3625b678e9",
    "Prodimt.Pedidos.Application.dll": "7a9e45dfd3e5871714138c6ec887f9ffe930b0803027998067bed48ae5e03926",
    "Prodimt.Pedidos.Domain.dll": "07928daa4deda2034a2a12fd5d2e6e292530fb44ec164b274f5ba05935b67ea2",
    "Prodimt.Pedidos.Infrastructure.dll": "e40708e2839da1e512da85afb5da468509879a7eca9d0434e8a4762110fc4e0c"
}
BASE_FRONTEND_HASHES = {
    "3rdpartylicenses.txt": "620aaa0d12759d2f42b0ebd4b56b717401562f9f1c7bff0391461638e407f294",
    "chunk-65O6XA4S.js": "d4fc54bed1343116d2a57774583fffa5ed578bd757a5dad6786e48ee8f58c6d4",
    "chunk-74I2EL5M.js": "3e9b9a97c77819d150a5ee7c368cd1fbe8544a6d5563d3738cf4c24dafe8de67",
    "chunk-AIGM43NK.js": "196410711e3f46a7c3381b10076b0b7c229c37ad8ea62bf7fc9dc6b0c7e7d4f7",
    "chunk-ATZPE7RE.js": "e22df83c1a895c358051e87cd602c2094473e1eecdf744bb2eca8c09852f4331",
    "chunk-CHTRCUXN.js": "2bc5eb4d3ee82652cbe288c9f40031ec743647bb8f2003973f2018fe778ae34f",
    "chunk-D5AS5TFU.js": "fae25bda331d8366c24b3864b64ea5d54efea25458bc864e38e3c9148f433ec7",
    "chunk-FEK7QPUC.js": "80c0dd2611b55b69200df8607735236b503ccaf3165b89ec053f6b355e5f813f",
    "chunk-GIKPNXMM.js": "e56daece8562f8a2d25246f3c839361204f096f99476f2a33cceb5aaa333b8d6",
    "chunk-GIMHC2SS.js": "c3d92a04963cf189de2b889d8dc1312d77c5f958359229dcc56d9010db38ed2f",
    "chunk-IG7DWRLX.js": "776d6b97991299db196b191c251cf7dca6cd54352ed3ded67f5328db4bb8a1b7",
    "chunk-ITQSDDTV.js": "2d88b0e0d131d42135717c74fe6ed4900149db2487b465ce98c2fbfd3d34c86e",
    "chunk-JAHVAT6O.js": "c981fa6bebd0cd9caf6921b99d491162dea5eafa8bbed7dfeae40d44fae522fa",
    "chunk-LEDZELSA.js": "64794d7a78091cda71adbedf3c04b03b0286a940fb17a0c470027c9566b1b38a",
    "chunk-O74RR6FB.js": "6ae971bb587db691af5ec69879da4a6032d890532fc6c1bbfaea417567a7912c",
    "chunk-PFELM5AO.js": "e7d2a572dc5ce1cb4a1e9fc443c1e29cd1134998ad7581db3d9bb4b1b6766409",
    "chunk-PTCE5TV4.js": "92b375c9becc9bd9d2d02ba38a495cf801903a2d57cf09b8f68080057197f2d2",
    "chunk-S7GMMLJS.js": "aa40c37f049ee227b5f51e85190b32ea393d5e6a1c7b7b54704de930fbdc4295",
    "chunk-TKJAHOHP.js": "6523adfd956500b76e8ffb5fd7d11383a05bcdd838bc1e8c462dc94025d3afe4",
    "chunk-TXYGS3E7.js": "935386b62a244e9483750feb676798bf1574a8d4a0224cff0ddc406303e6091d",
    "chunk-UCKMMINW.js": "6b88117d8416c2a3917deaef0d81d5a206a6b351b126d98978dcc34e18ad76aa",
    "chunk-V7FOOOUG.js": "e98accf8319c9d408d0a89387c5f2c545df27ebea4c16d39b6591523cab43d00",
    "chunk-WKJSOQYK.js": "7fd8805f2a46abfdbad9af2f204246c74a24d98a371e30d330f6174d2b51fbbb",
    "chunk-XX7VO3JY.js": "e373ac6458f56bd90e204591fbfc01f3a84f6c346fa79d3bf792a7b47228a756",
    "chunk-YIL6ZTU4.js": "79770ab074751c8e8fcee2ebbc3b7e2aef5e551659cd3d98ef0f4e22e3f6a113",
    "chunk-YLHAGBSG.js": "0518172376bfb23333bce57badd21a7edebb6ecec4a53cc6284495b38d7bde7c",
    "index.html": "3f067596a66a8fdd7b4c2ad9fb8acc71fec9247b76742ba71c0430c7e04375a4",
    "main-QQIB5T7M.js": "76d76715d8e8d0f2548d309e2717d92cb2026b4a458cd79c7f45c7ef3e575641",
    "polyfills-LVNOU2XZ.js": "f1b35a3b4db37b73ca0281d6b6247701465ff9e56a7a7b3aff9784518dc2c30a",
    "styles-DFZKJNR6.css": "1e7cb776da6771dbd689f4f03165161e0a9cd85cd1d375c4c3ade2fc012edd2d"
}
CLEAN_ENV = {'PATH':'/usr/bin:/bin','LANG':'C.UTF-8','LC_ALL':'C.UTF-8'}


class InstallError(Exception):
    """Only fixed, non-sensitive codes may be passed."""


def require(condition, issue):
    if not condition:
        raise InstallError(issue)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def regular(path, limit=1024*1024):
    fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW|os.O_NONBLOCK)
    with os.fdopen(fd,'rb') as stream:
        meta=os.fstat(stream.fileno())
        require(stat.S_ISREG(meta.st_mode) and meta.st_uid==0 and meta.st_nlink==1 and not meta.st_mode&0o022,'file_type_or_permissions_unexpected')
        raw=stream.read(limit+1)
    require(len(raw)<=limit,'file_too_large')
    return raw


def safe_directory(path):
    meta=path.lstat()
    require(stat.S_ISDIR(meta.st_mode) and meta.st_uid==0 and not meta.st_mode&0o022
            and path.resolve(strict=True)==path,'directory_owner_or_path_unexpected')


def command(args):
    try:
        result=subprocess.run(args,capture_output=True,stdin=subprocess.DEVNULL,env=CLEAN_ENV,timeout=20,check=False)
        require(result.returncode==0,'read_command_failed')
        return result.stdout
    except (OSError,subprocess.TimeoutExpired):
        raise InstallError('read_command_failed') from None


def validate_asset_closure(files):
    """Check local HTML entry points, compiled module imports and CSS URLs."""
    from html.parser import HTMLParser
    references=[]
    class EntryPoints(HTMLParser):
        def handle_starttag(self,tag,attributes):
            attrs=dict(attributes)
            if tag=='script' and 'src' in attrs:references.append(('index.html',attrs['src']))
            if tag=='link' and attrs.get('rel') in ('stylesheet','modulepreload','preload','icon'):
                require(bool(attrs.get('href')),'frontend_asset_reference_invalid')
                references.append(('index.html',attrs['href']))
    require('index.html' in files and '3rdpartylicenses.txt' in files,'frontend_build_missing')
    EntryPoints().feed(files['index.html'].decode('utf-8'))
    for name,raw in files.items():
        if name.endswith('.js'):
            # Angular's emitted static/dynamic imports use relative .js URLs.
            references.extend((name,value) for value in re.findall(r'''["']((?:\./|\.\./)[^"'\s]+\.js)["']''',raw.decode('utf-8')))
        elif name.endswith('.css'):
            references.extend((name,value.strip().strip('"\'')) for value in re.findall(r'url\(([^)]+)\)',raw.decode('utf-8'))
                              if not value.strip().strip('"\'').startswith(('data:','#')))
            references.extend((name,value) for value in re.findall(r'''@import\s+["']([^"']+)["']''',raw.decode('utf-8')))
    require(any(name.endswith('.js') for name in files),'frontend_build_missing')
    for origin,value in references:
        require(value and not value.startswith('//') and not re.match(r'[a-zA-Z][a-zA-Z0-9+.-]*:',value)
                and '\\' not in value and '?' not in value and '#' not in value and '%' not in value,
                'frontend_asset_reference_invalid')
        target=posixpath.normpath(posixpath.join(posixpath.dirname(origin),value)) if not value.startswith('/') else value[1:]
        require(target in files,'frontend_asset_reference_missing')


def load_payload(path):
    require(re.fullmatch(r'[a-f0-9]{40}',EXPECTED_SOURCE) and re.fullmatch(r'[a-f0-9]{64}',EXPECTED_PAYLOAD_SHA256),'installer_not_pinned')
    raw=regular(path,20*1024*1024)
    require(sha(raw)==EXPECTED_PAYLOAD_SHA256,'payload_hash_mismatch')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        rows=archive.infolist()
        require(0<len(rows)<=200 and len({row.filename for row in rows})==len(rows)
                and sum(row.file_size for row in rows)<=40*1024*1024
                and all(row.file_size<=10*1024*1024 for row in rows),'payload_size_or_duplicates_invalid')
        for row in rows:
            name=row.filename; pure=PurePosixPath(name)
            require(pure.as_posix()==name and not pure.is_absolute() and not any(x in ('','.','..') for x in pure.parts)
                    and ':' not in name and '\\' not in name and not row.is_dir() and not row.flag_bits&1
                    and not stat.S_ISLNK(row.external_attr>>16),'payload_path_invalid')
        files={row.filename:archive.read(row) for row in rows}
    manifest=json.loads(files.pop('manifest.json'))
    require(manifest.get('feature')=='delivery-flow-v1' and manifest.get('sourceCommit')==EXPECTED_SOURCE
            and manifest.get('baseRelease')==OLD and manifest.get('backendRelease')==BACKEND_RELEASE
            and set(manifest.get('files',{}))==set(files) and 'frontend/index.html' in files
            and all(name.startswith('frontend/') and not name.endswith('.map')
                    and not any(part.startswith('.') for part in PurePosixPath(name).parts) for name in files),'payload_manifest_unexpected')
    require(all(manifest['files'][name]=={'bytes':len(raw),'sha256':sha(raw)} for name,raw in files.items()),'payload_file_hash_mismatch')
    frontend={name.removeprefix('frontend/'):raw for name,raw in files.items()}
    validate_asset_closure(frontend)
    return frontend


class Installer:
    def __init__(self,package):
        self.package=package
        self.old=ROOT/'frontend/releases'/OLD
        self.new=ROOT/'frontend/releases'/('delivery-flow-'+EXPECTED_SOURCE[:12])
        self.backend=ROOT/'backend/releases'/BACKEND_RELEASE
        self.current=ROOT/'frontend/current'
        self.files={}
        self.switched=False
        self.state=dict(revision=REVISION,status='planned',phase='preflight',changes_made=False,
                        database_migrations_run=False,database_queries_run=False,backend_changed=False,service_restarted=False,
                        password_changed=False,health_verified=False,frontend_verified=False,secret_values_printed=False,
                        schema_verification='installed-i22-identity-and-health-only')

    def backend_identity(self):
        raw=command(['systemctl','show',SERVICE,'--property=ActiveState,MainPID,User,WorkingDirectory']).decode()
        values=dict(row.split('=',1) for row in raw.splitlines() if '=' in row)
        require(values.get('ActiveState')=='active' and values.get('User')=='www-data'
                and values.get('MainPID','').isdigit() and int(values['MainPID'])>0
                and values.get('WorkingDirectory')==str(ROOT/'backend/current'),'backend_service_unexpected')
        pid=int(values['MainPID'])
        require((ROOT/'backend/current').is_symlink() and (ROOT/'backend/current').resolve(strict=True)==self.backend
                and Path(f'/proc/{pid}/cwd').resolve(strict=True)==self.backend,'backend_release_not_i22')
        dll_hash=sha(regular(self.backend/'Prodimt.Pedidos.Api.dll',20*1024*1024))
        require(dll_hash==BASE_API_SHA256 and all(sha(regular(self.backend/name,20*1024*1024))==expected
                for name,expected in BASE_BACKEND_HASHES.items()),'backend_binary_not_expected')
        # Read only: detect concurrent configuration changes, without using or
        # printing credentials and without modifying any configuration file.
        settings=[]
        for name in ('appsettings.json','appsettings.Production.json'):
            path=self.backend/name
            settings.append(sha(regular(path)) if path.exists() or path.is_symlink() else None)
        return pid,dll_hash,sha(regular(ENV_FILE)),tuple(settings)

    def http(self,path):
        require(path.startswith('/') and not path.startswith('//'),'http_path_invalid')
        raw=command(['curl','--silent','--show-error','--noproxy','*','--max-time','10','--max-filesize','10485760',
                     '--resolve',DOMAIN+':443:127.0.0.1','--write-out','\n%{http_code}','https://'+DOMAIN+path])
        body,code=raw.rsplit(b'\n',1)
        return int(code),body

    def api_health(self):
        code,body=self.http('/health/db')
        require(code==200 and json.loads(body).get('database')=='reachable','api_health_failed')
        require(self.http('/api/admin/customers')[0]==401,'admin_authorization_unexpected')
        require(self.http('/api/customer-orders/11111111-1111-1111-1111-111111111111/next')[0]==401,
                'customer_authorization_unexpected')
        require(self.http('/api/delivery')[0]==401,'delivery_authorization_unexpected')
        require(self.http('/api/admin/delivery')[0]==401,'delivery_admin_authorization_unexpected')

    def verify_frontend(self,path,hashes,public=False):
        safe_directory(path)
        present=set()
        for candidate in path.rglob('*'):
            require(not candidate.is_symlink(),'frontend_tree_unexpected')
            if candidate.is_dir():safe_directory(candidate)
            else:present.add(candidate.relative_to(path).as_posix())
        require(present==set(hashes),'frontend_files_unexpected')
        for name,expected in hashes.items():
            raw=regular(path/name,10*1024*1024)
            require(sha(raw)==expected,'frontend_file_hash_mismatch')
            if public:
                code,body=self.http('/' if name=='index.html' else '/'+quote(name,safe='/'))
                require(code==200 and body==raw,'current_frontend_not_served')

    def require_no_pending_recovery(self):
        require(not any(BACKUPS.glob('*-pending.json')),'pending_recovery_requires_original_installer')

    def preflight(self):
        self.files=load_payload(self.package)
        for path in (ROOT,ROOT/'frontend',ROOT/'frontend/releases',ROOT/'backend',ROOT/'backend/releases',self.backend,BACKUPS):safe_directory(path)
        self.require_no_pending_recovery()
        require(self.current.is_symlink() and self.current.resolve(strict=True) in (self.old,self.new),'frontend_release_not_expected')
        self.previous=self.current.resolve(strict=True)
        self.identity=self.backend_identity()
        self.api_health()
        self.state['health_verified']=True
        hashes=BASE_FRONTEND_HASHES if self.previous==self.old else {name:sha(raw) for name,raw in self.files.items()}
        self.verify_frontend(self.previous,hashes,public=True)
        self.state['base_release_verified']=True
        require(shutil.disk_usage(ROOT).free>2*sum(map(len,self.files.values()))+10*1024*1024,'disk_space_insufficient')

    def verify_stage(self):
        self.verify_frontend(self.new,{name:sha(raw) for name,raw in self.files.items()})

    def stage(self):
        if self.new.exists():
            self.verify_stage();return
        temp=self.new.parent/('.delivery-flow-stage-'+uuid.uuid4().hex)
        temp.mkdir(mode=0o700)
        try:
            for name,raw in self.files.items():
                path=temp/name;path.parent.mkdir(parents=True,exist_ok=True)
                path.write_bytes(raw);path.chmod(0o644)
            for path in temp.rglob('*'):
                if path.is_dir():path.chmod(0o755)
            temp.chmod(0o755)
            temp.rename(self.new)
            self.state['changes_made']=True
        finally:
            if temp.exists():shutil.rmtree(temp)
        self.verify_stage()

    def switch(self,target):
        require(self.current.is_symlink() and self.current.resolve(strict=True) in (self.previous,self.new),'frontend_changed_during_update')
        link=self.current.parent/('.delivery-flow-link-'+uuid.uuid4().hex)
        try:
            link.symlink_to(target);os.replace(link,self.current)
        finally:
            if link.is_symlink():link.unlink()

    def verify_application(self):
        require(self.backend_identity()==self.identity,'backend_changed_during_update')
        self.api_health()
        for name,expected in self.files.items():
            code,body=self.http('/' if name=='index.html' else '/'+quote(name,safe='/'))
            require(code==200 and body==expected,'new_frontend_asset_not_served')
        self.state.update(health_verified=True,frontend_verified=True)

    def apply(self):
        self.preflight()
        if self.previous==self.new:
            self.verify_stage();self.verify_application()
            self.state.update(status='already_current',phase='completed');return self.state
        self.state['phase']='private-snapshot'
        snapshot=BACKUPS/('delivery-flow-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')+'-'+uuid.uuid4().hex[:12])
        snapshot.mkdir(mode=0o700)
        record=snapshot/'state.json';record.write_text(json.dumps(dict(revision=REVISION,sourceCommit=EXPECTED_SOURCE,
            previousFrontend=str(self.previous),newFrontend=str(self.new),backend=str(self.backend),backendDllSha256=self.identity[1])))
        record.chmod(0o600);self.state.update(changes_made=True,private_snapshot=str(snapshot))
        try:
            self.require_no_pending_recovery()
            self.state['phase']='stage-frontend';self.stage()
            require(self.backend_identity()==self.identity,'backend_changed_during_update')
            self.require_no_pending_recovery()
            self.state['phase']='activate-frontend';self.switched=True;self.switch(self.new)
            self.state['phase']='verify-application';self.verify_application()
            self.state.update(status='updated',phase='completed',source_commit=EXPECTED_SOURCE,new_frontend=str(self.new))
        except Exception:
            if self.switched:
                # A dropped SSH session must not interrupt restoration halfway.
                if getattr(self,'signals_installed',False):
                    for number in (signal.SIGINT,signal.SIGTERM,signal.SIGHUP):signal.signal(number,signal.SIG_IGN)
                try:
                    self.switch(self.previous)
                    code,body=self.http('/')
                    self.state['previous_frontend_restored']=code==200 and body==regular(self.previous/'index.html')
                except Exception:self.state['previous_frontend_restored']=False
            raise
        return self.state


def main():
    resource.setrlimit(resource.RLIMIT_CORE,(0,0))
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package',type=Path,default=Path('/root/prodimt-delivery-flow-payload.zip'))
    parser.add_argument('--apply',action='store_true')
    args=parser.parse_args();instance=Installer(args.package)
    instance.state['mode']='apply' if args.apply else 'read-only'
    previous_handlers={}
    try:
        require(os.geteuid()==0,'root_required')
        if args.apply:
            fd=os.open(LOCK,os.O_WRONLY|os.O_CREAT|os.O_NOFOLLOW,0o600)
            with os.fdopen(fd,'w') as lock:
                meta=os.fstat(lock.fileno())
                require(stat.S_ISREG(meta.st_mode) and meta.st_uid==0 and meta.st_nlink==1
                        and stat.S_IMODE(meta.st_mode)==0o600,'lock_identity_unexpected')
                fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
                def interrupted(number,frame):raise InstallError('installation_interrupted')
                for number in (signal.SIGINT,signal.SIGTERM,signal.SIGHUP):
                    previous_handlers[number]=signal.getsignal(number);signal.signal(number,interrupted)
                instance.signals_installed=True;instance.apply()
        else:instance.preflight();instance.state.update(status='planned',phase='completed')
    except InstallError as exc:instance.state.update(status='failed',issue=str(exc))
    except Exception:instance.state.update(status='failed',issue='installation_failed_without_sensitive_details')
    finally:
        for number,handler in previous_handlers.items():signal.signal(number,handler)
    print(json.dumps(instance.state,ensure_ascii=False,indent=2))
    return 1 if instance.state['status']=='failed' else 0


if __name__=='__main__':raise SystemExit(main())
