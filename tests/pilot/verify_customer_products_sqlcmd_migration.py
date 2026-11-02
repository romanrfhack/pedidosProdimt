"""Exercise I16's exact native migration/grants through actual ODBC sqlcmd.

Only the container transport and randomly generated disposable database differ
from the delivered sql_run implementation. Never connects to the production VPS.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import types
import uuid

CONTAINER='prodimt-i02-sqlserver'
SQLCMD='/opt/mssql-tools18/bin/sqlcmd'


class FixtureFailure(Exception):pass


def require(condition,issue):
    if not condition:raise FixtureFailure(issue)


def load_module(name,path):
    spec=importlib.util.spec_from_file_location(name,path)
    result=importlib.util.module_from_spec(spec);spec.loader.exec_module(result);return result


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline',type=Path,required=True);parser.add_argument('--migration',type=Path,required=True)
    args=parser.parse_args();root=Path(__file__).resolve().parents[2]
    runtime=load_module('i16_native_transport',root/'scripts/pilot/install-quick-capture-update.py')
    inventory=load_module('i16_native_inventory',root/'scripts/pilot/inspect-native.py')
    installer=load_module('i16_native_contract',root/'scripts/pilot/install-customer-products-update.py')
    commercial=load_module('i16_price_contract',root/'scripts/pilot/install-commercial-catalog-update.py')
    baseline=args.baseline.read_text(encoding='utf-8-sig');raw=args.migration.read_text(encoding='utf-8-sig')
    require(installer.TOKEN_MIGRATION in baseline and installer.MIGRATION not in baseline
        and not re.search(r'(?im)^\s*(USE|CREATE\s+DATABASE|DROP\s+DATABASE)\b',baseline),'baseline_not_pinned_i12')
    # Preserve the verified I12 batch repair when creating the historical base
    # through sqlcmd; EF's combined export still has its original SQL207 layout.
    token_native=load_module('i16_historical_token_batches',root/'scripts/pilot/install-access-token-management-update.py')
    token_original=(root/'tests/pilot/fixtures/i12-original-migration.sql').read_text(encoding='utf-8-sig')
    require(baseline.endswith(token_original),'historical_i12_export_changed')
    baseline=baseline[:-len(token_original)]+token_native.migration_sql_for_sqlcmd(token_original)
    fixture=(root/'tests/pilot/fixtures/i16-additional-products-migration.sql').read_text(encoding='utf-8-sig')
    require(raw==fixture,'generated_migration_differs_from_reviewed_fixture')
    sql=installer.migration_sql_for_sqlcmd(raw)
    connection=os.environ.get('PRODIMT_SQL_TEST_CONNECTION','')
    match=re.fullmatch(r'Server=127\.0\.0\.1,14333;Database=master;User Id=sa;Password=([0-9a-f]{48}Aa1!);Encrypt=True;TrustServerCertificate=True',connection)
    require(match is not None,'disposable_connection_unexpected');password=match[1]
    target='ProdimtI16Cli_'+uuid.uuid4().hex
    require(re.fullmatch(r'ProdimtI16Cli_[0-9a-f]{32}',target),'disposable_database_name_invalid')
    runtime.SERVER,runtime.DATABASE='localhost','master'
    instance=runtime.Installer('synthetic-unused-payload.zip')
    instance.inspector,instance.sql_tool=inventory,SQLCMD
    instance.sql_login,instance.sql_password='sa',password
    calls=[]
    def container_run(argv,*,input,capture_output,text,env,timeout,check):
        require(argv[0]==SQLCMD and all(flag in argv for flag in ('-C','-I','-b','-x','-y','-w')),'sqlcmd_flags_changed')
        require(argv[argv.index('-S')+1]=='localhost' and argv[argv.index('-d')+1] in ('master',target),'target_not_disposable')
        require(capture_output is True and text is True and check is False and input.startswith(runtime.OPTIONS),'sqlcmd_protocol_changed')
        require(set(env)==set(runtime.CLEAN_ENV)|{'SQLCMDUSER','SQLCMDPASSWORD'} and env['SQLCMDPASSWORD']==password
            and env['SQLCMDUSER']=='sa' and password not in '\0'.join(argv),'sqlcmd_secret_protocol_changed')
        calls.append(hashlib.sha256(input[len(runtime.OPTIONS):].encode()).hexdigest())
        return subprocess.run(['/usr/bin/docker','exec','--interactive','--env','SQLCMDUSER','--env','SQLCMDPASSWORD',CONTAINER,*argv],
            input=input,capture_output=True,text=True,env=env,timeout=timeout,check=False)
    runtime.subprocess=types.SimpleNamespace(run=container_run,TimeoutExpired=subprocess.TimeoutExpired)
    execute=lambda script,timeout=90:instance.sql_run(script,timeout=timeout)
    query=instance.sql_json
    def as_app(statement):
        return inventory.parse_sql_output(execute("EXECUTE AS USER=N'prodimt_pedidos_app';\nSELECT ("+
            statement.rstrip().removesuffix(';')+") AS ProdimtInspectionJson;\nREVERT;"))
    def expect_error(script,code,issue):
        instance.state.pop('sql_error_numbers',None)
        try:execute(script)
        except runtime.InstallError:require(code in instance.state.get('sql_error_numbers',[]),issue)
        else:raise FixtureFailure(issue)
    created=False
    try:
        execute('CREATE DATABASE ['+target+'];');created=True;runtime.DATABASE=target
        execute(baseline,timeout=180)
        require(len(query(inventory.SQL_MIGRATIONS))==13,'baseline_history_invalid')
        require(query(installer.SETUP_SCHEMA)==dict.fromkeys(installer.EXPECTED_SETUP_SCHEMA,0),'baseline_contains_partial_i16')
        customer,product,channel,order,request=(str(uuid.uuid4()) for _ in range(5))
        execute("""
INSERT INTO dbo.Customers(Id,Name,PhoneNumber,IsActive,CreatedAt,UpdatedAt)
 VALUES('"""+customer+"""',N'Synthetic native I16 client',N'',1,'2026-11-02T12:00:00+00:00','2026-11-02T12:00:00+00:00');
INSERT INTO dbo.Products(Id,Name,IsActive,UnitCode) VALUES('"""+product+"""',N'Synthetic native package',1,N'package');
INSERT INTO dbo.CustomerFrequentProducts(CustomerId,ProductId,SortOrder,IsActive) VALUES('"""+customer+"""','"""+product+"""',1,1);
INSERT INTO dbo.SalesChannels(Id,Name,Type,IsInternal) VALUES('"""+channel+"""',N'Synthetic customer channel',N'Customer',0);
INSERT INTO dbo.Orders(Id,CustomerId,SalesChannelId,OrderDate,SubmittedAt,Status,SequenceNumber,IsLate,RequiresAdminReview)
 VALUES('"""+order+"""','"""+customer+"""','"""+channel+"""','2026-11-02','2026-11-02T12:00:00+00:00',N'Submitted',1,0,0);
INSERT INTO dbo.OrderLines(Id,OrderId,ProductId,Quantity,UnitCode,UnitPriceSnapshot,CurrencyCodeSnapshot)
 VALUES(NEWID(),'"""+order+"""','"""+product+"""',2,N'package',0,N'MXN');
INSERT INTO dbo.CustomerAccessTokens(Id,CustomerId,TokenHash,DisplayName,IsActive,CreatedAt,ProtectedToken,MetadataVersion,IsReminderLink)
 VALUES(NEWID(),'"""+customer+"""',N'synthetic-i16-current-hash',N'Synthetic protected access',1,'2026-11-02T12:00:00+00:00',N'v1.synthetic-opaque-preserved',7,1);
""")
        counts=query(runtime.COUNTS_QUERY);fingerprint=query(installer.DATA_FINGERPRINT);tokens=query(installer.TOKEN_COUNTS)
        # The application principal is intentionally absent: failing GRANT must
        # roll back the earlier DDL and inserted migration-history row as well.
        expect_error(sql,15151,'missing_app_principal_did_not_fail_grant')
        require(calls[-1]==hashlib.sha256(sql.encode()).hexdigest(),'native_script_not_sent_verbatim')
        require(len(query(inventory.SQL_MIGRATIONS))==13 and query(installer.SETUP_SCHEMA)==dict.fromkeys(installer.EXPECTED_SETUP_SCHEMA,0),
            'grant_failure_left_partial_schema_or_history')
        require(query(runtime.COUNTS_QUERY)==counts and query(installer.DATA_FINGERPRINT)==fingerprint,'grant_failure_changed_existing_data')
        # The deployed application already reads frequent products. Reproduce
        # that pre-existing permission so sys.columns exposes its new flag to
        # the same limited principal; I16 must not grant unrelated base rights.
        execute('CREATE USER [prodimt_pedidos_app] WITHOUT LOGIN;\n'
            'GRANT SELECT ON dbo.CustomerFrequentProducts TO [prodimt_pedidos_app];\n'+commercial.PRICE_GRANTS)
        price_permissions=as_app(commercial.PRICE_PERMISSIONS)
        execute(sql)
        require(len(query(inventory.SQL_MIGRATIONS))==14,'migration_history_not_fourteen')
        require(query(installer.SETUP_SCHEMA)==installer.EXPECTED_SETUP_SCHEMA,'native_setup_schema_invalid')
        require(as_app(installer.SETUP_SCHEMA)==installer.EXPECTED_SETUP_SCHEMA,'application_cannot_verify_native_schema')
        require(as_app(installer.SETUP_PERMISSIONS)==installer.EXPECTED_SETUP_PERMISSIONS,'application_permissions_not_minimal')
        require(as_app(commercial.PRICE_PERMISSIONS)==price_permissions==commercial.EXPECTED_PRICE_PERMISSIONS,'price_history_permissions_changed')
        require(query(installer.SETUP_COUNTS)==dict(requests=0,links=0,open_requests=0,inherit_default=1,independent=0),'legacy_machine_inheritance_changed')
        require(query(runtime.COUNTS_QUERY)==counts and query(installer.TOKEN_COUNTS)==tokens and query(installer.DATA_FINGERPRINT)==fingerprint,
            'migration_changed_existing_data_or_token')
        execute("EXECUTE AS USER=N'prodimt_pedidos_app';\n"+"""
INSERT INTO dbo.CustomerProductSetupRequests(Id,CustomerId,ProductId,ProductNameSnapshot,UnitCode,Version,IsResolved,CreatedAt,LastRequestedAt)
 VALUES('"""+request+"""','"""+customer+"""','"""+product+"""',N'Synthetic package',N'package',1,0,'2026-11-02T12:00:00+00:00','2026-11-02T12:00:00+00:00');
INSERT INTO dbo.CustomerProductSetupOrders(SetupRequestId,OrderId,Source,RequestedAt)
 VALUES('"""+request+"""','"""+order+"""',N'Customer','2026-11-02T12:00:00+00:00');
UPDATE dbo.CustomerProductSetupRequests SET Version=2 WHERE Id='"""+request+"""';
REVERT;
""")
        stable=query(installer.SETUP_COUNTS)
        for _ in range(2):
            execute(sql)
            require(query(installer.SETUP_COUNTS)==stable and query(installer.DATA_FINGERPRINT)==fingerprint
                and len(query(inventory.SQL_MIGRATIONS))==14,'repeated_script_changed_setup_price_or_token')
        expect_error("EXECUTE AS USER=N'prodimt_pedidos_app'; DELETE FROM dbo.CustomerProductSetupRequests;",229,'request_delete_not_denied')
        expect_error("EXECUTE AS USER=N'prodimt_pedidos_app'; UPDATE dbo.CustomerProductSetupOrders SET Source=N'Admin';",229,'link_update_not_denied')
        print('I16 native ODBC verified: grant failure rolls back DDL/history; 13-to-14 preserves tokens, zero prices and existing inheritance; minimal DML and repeated-script preservation pass.')
    finally:
        if created:
            runtime.DATABASE='master';execute('ALTER DATABASE ['+target+'] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ['+target+'];')
        instance.sql_password='';password=''


if __name__=='__main__':
    try:main()
    except FixtureFailure as error:
        print('I16 native SQL regression failed: '+str(error),file=sys.stderr);sys.exit(1)
    except (Exception,KeyboardInterrupt):
        print('I16 native SQL regression failed: unexpected_fixture_failure_without_sensitive_details',file=sys.stderr);sys.exit(1)
