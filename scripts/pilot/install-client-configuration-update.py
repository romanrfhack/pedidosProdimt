#!/usr/bin/env python3
"""Install only the I08 frontend on the existing native I07, with rollback.

No SQL access, credentials, migrations, backend changes or service restart.
"""
import argparse
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import resource
import signal
import shutil
import stat
import subprocess
from urllib.parse import quote
import uuid
import zipfile

REVISION = '20261031-client-configuration-install1'
EXPECTED_SOURCE = 'REPLACE_SOURCE_COMMIT'
EXPECTED_PAYLOAD_SHA256 = 'REPLACE_PAYLOAD_SHA256'
ROOT = Path('/var/www/prodimt-pedidos')
BACKUPS = Path('/var/backups/prodimt-pedidos')
LOCK = Path('/run/lock/prodimt-pedidos-i04.lock')
SERVICE = 'prodimt-pedidos-api.service'
DOMAIN = 'pedidos.prodimt.site'
OLD = 'capture-46fed604732e'
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
        require(stat.S_ISREG(meta.st_mode) and not meta.st_mode&0o022,'file_type_or_permissions_unexpected')
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


def load_payload(path):
    require(re.fullmatch(r'[a-f0-9]{40}',EXPECTED_SOURCE) and re.fullmatch(r'[a-f0-9]{64}',EXPECTED_PAYLOAD_SHA256),'installer_not_pinned')
    require(sha(regular(path,20*1024*1024))==EXPECTED_PAYLOAD_SHA256,'payload_hash_mismatch')
    with zipfile.ZipFile(path) as archive:
        rows=archive.infolist()
        require(0<len(rows)<=200 and len({row.filename for row in rows})==len(rows)
                and sum(row.file_size for row in rows)<=40*1024*1024,'payload_size_or_duplicates_invalid')
        for row in rows:
            name=row.filename; pure=PurePosixPath(name)
            require(pure.as_posix()==name and not pure.is_absolute() and not any(x in ('','.','..') for x in pure.parts)
                    and ':' not in name and '\\' not in name and not row.is_dir() and not row.flag_bits&1
                    and not stat.S_ISLNK(row.external_attr>>16),'payload_path_invalid')
        files={row.filename:archive.read(row) for row in rows}
    manifest=json.loads(files.pop('manifest.json'))
    require(manifest.get('feature')=='customer-configuration-v1' and manifest.get('sourceCommit')==EXPECTED_SOURCE
            and set(manifest.get('files',{}))==set(files) and 'frontend/index.html' in files
            and all(name.startswith('frontend/') and not name.endswith('.map') for name in files),'payload_manifest_unexpected')
    require(all(manifest['files'][name]=={'bytes':len(raw),'sha256':sha(raw)} for name,raw in files.items()),'payload_file_hash_mismatch')
    return {name.removeprefix('frontend/'):raw for name,raw in files.items()}


class Installer:
    def __init__(self,package):
        self.package=package
        self.old=ROOT/'frontend/releases'/OLD
        self.new=ROOT/'frontend/releases'/('client-config-'+EXPECTED_SOURCE[:12])
        self.backend=ROOT/'backend/releases'/OLD
        self.current=ROOT/'frontend/current'
        self.files={}
        self.switched=False
        self.state=dict(revision=REVISION,status='planned',phase='preflight',changes_made=False,
                        database_migrations_run=False,backend_changed=False,service_restarted=False,
                        password_changed=False,health_verified=False,frontend_verified=False,secret_values_printed=False)

    def backend_identity(self):
        raw=command(['systemctl','show',SERVICE,'--property=ActiveState,MainPID,User,WorkingDirectory']).decode()
        values=dict(row.split('=',1) for row in raw.splitlines() if '=' in row)
        require(values.get('ActiveState')=='active' and values.get('User')=='www-data'
                and values.get('MainPID','').isdigit() and int(values['MainPID'])>0
                and values.get('WorkingDirectory')==str(ROOT/'backend/current'),'backend_service_unexpected')
        pid=int(values['MainPID'])
        require((ROOT/'backend/current').is_symlink() and (ROOT/'backend/current').resolve(strict=True)==self.backend
                and Path(f'/proc/{pid}/cwd').resolve(strict=True)==self.backend,'backend_release_not_i07')
        return pid,sha(regular(self.backend/'Prodimt.Pedidos.Api.dll',20*1024*1024))

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

    def preflight(self):
        self.files=load_payload(self.package)
        for path in (ROOT,ROOT/'frontend',ROOT/'frontend/releases',BACKUPS):safe_directory(path)
        require(self.current.is_symlink() and self.current.resolve(strict=True) in (self.old,self.new),'frontend_release_not_expected')
        self.previous=self.current.resolve(strict=True)
        self.identity=self.backend_identity()
        self.api_health()
        code,body=self.http('/')
        require(code==200 and body==regular(self.previous/'index.html'),'current_frontend_not_served')
        require(shutil.disk_usage(ROOT).free>2*sum(map(len,self.files.values()))+10*1024*1024,'disk_space_insufficient')

    def verify_stage(self):
        require(self.new.is_dir() and not self.new.is_symlink(),'staged_release_unexpected')
        present={str(path.relative_to(self.new)) for path in self.new.rglob('*') if not path.is_dir()}
        require(present==set(self.files) and all(regular(self.new/name,10*1024*1024)==raw for name,raw in self.files.items()),'staged_files_mismatch')

    def stage(self):
        if self.new.exists():
            self.verify_stage();return
        temp=self.new.parent/('.client-config-stage-'+uuid.uuid4().hex)
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
        link=self.current.parent/('.client-config-link-'+uuid.uuid4().hex)
        try:
            link.symlink_to(target);os.replace(link,self.current)
        finally:
            if link.is_symlink():link.unlink()

    def verify_application(self):
        require(self.backend_identity()==self.identity,'backend_changed_during_update')
        self.api_health()
        for name,expected in self.files.items():
            if name!='index.html' and not name.endswith(('.js','.css')):continue
            code,body=self.http('/' if name=='index.html' else '/'+quote(name,safe='/'))
            require(code==200 and body==expected,'new_frontend_asset_not_served')
        self.state.update(health_verified=True,frontend_verified=True)

    def apply(self):
        self.preflight()
        if self.previous==self.new:
            self.verify_stage();self.verify_application()
            self.state.update(status='already_current',phase='completed');return self.state
        self.state['phase']='private-snapshot'
        snapshot=BACKUPS/('client-configuration-'+datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')+'-'+uuid.uuid4().hex[:12])
        snapshot.mkdir(mode=0o700)
        record=snapshot/'state.json';record.write_text(json.dumps(dict(revision=REVISION,sourceCommit=EXPECTED_SOURCE,
            previousFrontend=str(self.previous),newFrontend=str(self.new),backend=str(self.backend),backendDllSha256=self.identity[1])))
        record.chmod(0o600);self.state.update(changes_made=True,private_snapshot=str(snapshot))
        try:
            self.state['phase']='stage-frontend';self.stage()
            require(self.backend_identity()==self.identity,'backend_changed_during_update')
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
    parser.add_argument('--package',type=Path,default=Path('/root/prodimt-client-configuration-payload.zip'))
    parser.add_argument('--apply',action='store_true')
    args=parser.parse_args();instance=Installer(args.package)
    instance.state['mode']='apply' if args.apply else 'read-only'
    previous_handlers={}
    try:
        require(os.geteuid()==0,'root_required')
        if args.apply:
            fd=os.open(LOCK,os.O_WRONLY|os.O_CREAT|os.O_NOFOLLOW,0o600)
            with os.fdopen(fd,'w') as lock:
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
