#!/usr/bin/env python3
"""C2: owned Docker SQL -> real Kestrel/JWT/SQL -> compiled Angular/Playwright.

No target URL, connection string, api.env or operational credential is accepted.
Only the UUID-labelled container created by this invocation can be removed.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import signal
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
RUNNER = ROOT / "tests/Prodimt.Pedidos.DeploymentValidation/bin/Release/net10.0/Prodimt.Pedidos.DeploymentValidation.dll"
BASE = "fdb81ea2f92011a74f2089642b5d3a9e46b26351"


class Failure(Exception):
    pass


def durable_record(path, value):
    with path.open("w") as record:
        json.dump(value, record, indent=2)
        record.write("\n")
        record.flush()
        os.fsync(record.fileno())


def recover(directory):
    """Finish only a recorded, abandoned C2 container; never infer ownership."""
    directory = directory.resolve()
    owner = json.loads((directory / "container-owner.json").read_text())
    run_id = owner.get("owner", "")
    if (not re.fullmatch(r"[a-f0-9]{32}", run_id) or owner.get("name") != "prodimt-c2-" + run_id or
            owner.get("hostname") != "c2-" + run_id or ROOT == directory or ROOT in directory.parents):
        raise Failure("recovery_owner_invalid")
    process_file = Path("/proc") / str(owner.get("supervisorPid")) / "stat"
    if process_file.exists() and process_file.read_text().split()[21] == owner.get("supervisorStart"):
        raise Failure("recovery_supervisor_still_active")
    def docker(*args):
        return subprocess.run(["docker", *args], env=clean_environment(), text=True, capture_output=True, timeout=60)
    current = docker("inspect", owner["name"])
    if current.returncode == 0:
        data = json.loads(current.stdout)[0]
        if data["Name"] != "/" + owner["name"] or data["Config"]["Labels"].get("prodimt.c2.owner") != run_id:
            raise Failure("recovery_container_ownership_mismatch")
        if docker("rm", "--force", data["Id"]).returncode:
            raise Failure("recovery_remove_failed")
    remaining = docker("ps", "--all", "--quiet", "--filter", "label=prodimt.c2.owner=" + run_id)
    if remaining.returncode or remaining.stdout.strip():
        raise Failure("recovery_absence_unverified")
    for private in ("sql.private.env", "config.private.json", "browser-private.json"):
        (directory / private).unlink(missing_ok=True)
    result = {"owner": run_id, "cleanupVerified": True, "recoveryPending": False,
              "validationStatus": "failed", "reason": "abandoned_run_recovered_no_test_pass_inferred"}
    (directory / "recovery.json").write_text(json.dumps(result, indent=2) + "\n")
    report = json.loads((directory / "gate.json").read_text())
    report.update(status="failed", cleanupVerified=True, recoveryPending=False, issue="abandoned_run_recovered")
    (directory / "gate.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(result))
    return 0


def clean_environment():
    keys = ("PATH", "HOME", "USER", "LANG", "LC_ALL", "TMPDIR", "DOTNET_ROOT",
            "NUGET_PACKAGES", "PLAYWRIGHT_BROWSERS_PATH", "PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH")
    env = {key: os.environ[key] for key in keys if key in os.environ}
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
               NG_CLI_ANALYTICS="false", CI="true")
    return env


def source_identity(source):
    if not re.fullmatch(r"[0-9a-f]{40}", source):
        raise Failure("source_sha_required")
    def git(*args):
        return subprocess.check_output(["git", "-C", str(ROOT), *args], text=True).strip()
    if git("rev-parse", "HEAD") != source:
        raise Failure("source_mismatch")
    if git("status", "--porcelain", "--untracked-files=all"):
        raise Failure("source_dirty")
    if subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor", BASE, source],
                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
        raise Failure("installed_base_not_ancestor")


def checked_browser(report, expected):
    stats = report.get("stats", {})
    specs = []
    def visit(suite):
        specs.extend(suite.get("specs", []))
        for child in suite.get("suites", []):
            visit(child)
    for suite in report.get("suites", []):
        visit(suite)
    if (report.get("errors") or len(specs) != expected or stats.get("expected") != expected or
            any(stats.get(key, 0) for key in ("unexpected", "skipped", "flaky"))):
        raise Failure("browser_incomplete_or_failed")
    for spec in specs:
        tests = spec.get("tests", [])
        if len(tests) != 1 or tests[0].get("status") != "expected":
            raise Failure("browser_missing_or_unstable")
        results = tests[0].get("results", [])
        if len(results) != 1 or results[0].get("status") != "passed":
            raise Failure("browser_retry_or_failure")
    return {"expected": expected, "passed": expected, "failed": 0, "skipped": 0, "flaky": 0}


def redact_diagnostics(content):
    content = re.sub(r"c2-synthetic-[a-f0-9]{32}", "[synthetic-token-omitted]", content)
    return re.sub(r"\b(?:[a-f0-9]{32}|[a-f0-9]{48})Aa1!", "[synthetic-password-omitted]", content)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", help="Exact clean C2 commit")
    parser.add_argument("--output", type=Path, help="New private directory outside the checkout")
    parser.add_argument("--recover", type=Path, help="Recover the exact recorded container after a forced termination")
    parser.add_argument("--level", choices=("all", "http-sql", "browser"), default="all")
    args = parser.parse_args()
    if args.recover:
        if args.source or args.output:
            parser.error("recovery does not accept source/output")
        return recover(args.recover)
    if not args.source or not args.output:
        parser.error("source and output are required for a validation")
    directory = args.output.resolve()
    if directory == ROOT or ROOT in directory.parents or directory.exists():
        parser.error("output must be a NEW directory outside the checkout")
    directory.mkdir(parents=True, mode=0o700)
    os.chmod(directory, 0o700)
    os.umask(0o077)
    env = clean_environment()
    run_id = uuid.uuid4().hex
    name = "prodimt-c2-" + run_id
    processes = []
    container_started = False
    container_id = None
    report = {"schema": "prodimt.c2.gate.v1", "sourceCommit": args.source, "baseInstalled": BASE,
              "level": args.level, "status": "failed", "runId": run_id, "steps": [],
              "cleanupVerified": True, "recoveryPending": False, "productionChanged": False,
              "ciRemoteExecuted": False, "humanAcceptance": "pending"}

    def run(command, log, timeout=300, extra=None):
        with (directory / log).open("w") as output:
            process = subprocess.Popen(command, cwd=ROOT, env=extra or env,
                                       stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            processes.append(process)
            try:
                code = process.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                raise Failure("timeout:" + log) from None
        report["steps"].append({"log": log, "exitCode": code})
        if code:
            raise Failure("command_failed:" + log)

    def docker(*command, check=True):
        result = subprocess.run(["docker", *command], env=env, text=True, capture_output=True, timeout=60)
        if check and result.returncode:
            # Docker errors may contain host information; fixed issue + exit code only.
            raise Failure("docker_command_failed:" + command[0])
        return result

    def stop_process(process):
        if process.poll() is None:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=10)

    def interrupt(_signum, _frame):
        raise Failure("interrupted")

    signal.signal(signal.SIGTERM, interrupt)
    signal.signal(signal.SIGINT, interrupt)
    try:
        source_identity(args.source)
        for binary in ("dotnet", "node", "npm", "docker"):
            if not shutil.which(binary, path=env.get("PATH")):
                raise Failure(binary + "_missing")
        node = subprocess.check_output(["node", "--version"], env=env, text=True).strip()
        dotnet = subprocess.check_output(["dotnet", "--version"], env=env, text=True).strip()
        report["tools"] = {"node": node, "dotnet": dotnet}
        if not node.startswith("v24.") or not dotnet.startswith("10."):
            raise Failure("node24_and_dotnet10_required")
        docker("info", "--format", "{{.ServerVersion}}")
        if docker("image", "inspect", "mcr.microsoft.com/mssql/server:2022-latest", check=False).returncode:
            run(["docker", "pull", "mcr.microsoft.com/mssql/server:2022-latest"], "sql-image-pull.log", 600)
        # Operational connections and API URLs cannot enter children, including
        # environment variables left over from a user's other shell commands.
        report["inheritedApplicationEnvironment"] = "excluded_by_allowlist"
        run(["dotnet", "build", "tests/Prodimt.Pedidos.DeploymentValidation/Prodimt.Pedidos.DeploymentValidation.csproj",
             "-c", "Release", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false"], "dotnet-build.log", 600)
        cases = json.loads(subprocess.check_output(["dotnet", str(RUNNER), "--list-c2"], env=env, text=True))
        if not cases or len(set(cases)) != len(cases):
            raise Failure("http_sql_enumeration_invalid")
        report["httpSqlCases"] = cases
        (directory / "http-sql-cases.json").write_text(json.dumps(cases, indent=2) + "\n")
        if args.level in ("all", "browser"):
            run(["npm", "ci", "--prefix", "apps/prodimt-pedidos-web"], "npm-web.log")
            run(["npm", "ci", "--prefix", "tests/e2e"], "npm-e2e.log")
            run(["npm", "run", "build", "--prefix", "apps/prodimt-pedidos-web"], "angular-build.log", 600)
            run(["npm", "run", "typecheck", "--prefix", "tests/e2e"], "typecheck.log")
            run(["node", "tests/e2e/node_modules/playwright/cli.js", "install", "chromium"], "chromium-install.log", 600)
        password = secrets.token_hex(24) + "Aa1!"
        private_env = directory / "sql.private.env"
        private_env.write_text("ACCEPT_EULA=Y\nMSSQL_PID=Developer\nMSSQL_MEMORY_LIMIT_MB=2048\nMSSQL_SA_PASSWORD=" + password + "\n")
        # Record attempted ownership before docker creates anything, including a lost response.
        report["container"] = {"name": name, "owner": run_id, "hostname": "c2-" + run_id}
        report["container"].update(supervisorPid=os.getpid(), supervisorStart=Path("/proc/self/stat").read_text().split()[21])
        durable_record(directory / "container-owner.json", report["container"])
        durable_record(directory / "gate.json", dict(report, status="running", cleanupVerified=False, recoveryPending=True))
        container_started = True
        result = docker("run", "--pull=never", "--detach", "--name", name, "--hostname", "c2-" + run_id,
                        "--label", "prodimt.c2.owner=" + run_id, "--publish", "127.0.0.1::1433",
                        "--env-file", str(private_env), "mcr.microsoft.com/mssql/server:2022-latest")
        container_id = result.stdout.strip()
        if not re.fullmatch(r"[a-f0-9]{64}", container_id):
            raise Failure("container_identity_invalid")
        inspect = json.loads(docker("inspect", container_id).stdout)[0]
        report["sqlImageId"] = inspect["Image"]
        binding = inspect["NetworkSettings"]["Ports"]["1433/tcp"]
        if len(binding) != 1 or binding[0]["HostIp"] != "127.0.0.1":
            raise Failure("sql_loopback_binding_invalid")
        port = int(binding[0]["HostPort"])
        if port == 1433 or not 1024 <= port <= 65535:
            raise Failure("sql_test_port_invalid")
        config = {"runId": run_id, "dataSource": "127.0.0.1," + str(port), "serverName": "c2-" + run_id,
                  "adminPassword": password, "directory": str(directory),
                  "webRoot": str(ROOT / "apps/prodimt-pedidos-web/dist/prodimt-pedidos-web/browser")}
        config_file = directory / "config.private.json"
        config_file.write_text(json.dumps(config))
        deadline = time.monotonic() + 150
        while True:
            ready = docker("exec", container_id, "/bin/bash", "-c",
                           'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b -Q "SELECT 1"', check=False)
            if ready.returncode == 0:
                break
            if time.monotonic() >= deadline:
                raise Failure("sql_availability_timeout")
            time.sleep(2)
        if args.level in ("all", "http-sql"):
            run(["dotnet", str(RUNNER), "--validate-c2", str(config_file)], "http-sql.log", 900)
            sql = json.loads((directory / "http-sql.json").read_text())
            if (sql["status"] != "passed" or sql["expected"] != len(cases) or
                    [case["name"] for case in sql["results"]] != cases or
                    any(case["status"] != "passed" for case in sql["results"]) or not sql["cleanupVerified"]):
                raise Failure("http_sql_incomplete")
            report["httpSql"] = {"passed": len(cases), "expected": len(cases), "cleanupVerified": True}
        if args.level in ("all", "browser"):
            host_output = (directory / "browser-host.log").open("w")
            host = subprocess.Popen(["dotnet", str(RUNNER), "--serve-c2", str(config_file)], cwd=ROOT,
                                    env=env, stdout=host_output, stderr=subprocess.STDOUT, start_new_session=True)
            processes.append(host)
            deadline = time.monotonic() + 120
            while not (directory / "browser-private.json").exists():
                if host.poll() is not None or time.monotonic() >= deadline:
                    raise Failure("real_api_availability_failed")
                time.sleep(0.2)
            browser_env = dict(env, PRODIMT_C2_DIRECTORY=str(directory))
            run(["npm", "run", "test:real", "--prefix", "tests/e2e", "--", "--list"], "browser-collection.log", extra=browser_env)
            # Collection uses the same configured project; fix the count only after enumeration.
            collection = json.loads((directory / "playwright-real.json").read_text())
            def count(suite):
                return len(suite.get("specs", [])) + sum(count(child) for child in suite.get("suites", []))
            expected = sum(count(suite) for suite in collection.get("suites", []))
            if expected != 3:
                raise Failure("browser_collection_changed_review_expected_count")
            run(["npm", "run", "test:real", "--prefix", "tests/e2e"], "browser.log", 600, browser_env)
            report["browser"] = checked_browser(json.loads((directory / "playwright-real.json").read_text()), expected)
            (directory / "browser-finished").write_text("verify independent SQL now\n")
            try:
                code = host.wait(timeout=90)
            except subprocess.TimeoutExpired:
                raise Failure("browser_sql_verification_timeout") from None
            host_output.close()
            if code or not (directory / "sql-browser-verification.json").exists():
                raise Failure("browser_sql_verification_failed")
            host_report = json.loads((directory / "browser-host.json").read_text())
            if host_report["status"] != "passed" or not host_report["cleanupVerified"]:
                raise Failure("browser_sql_cleanup_failed")
            report["browserIndependentSql"] = json.loads((directory / "sql-browser-verification.json").read_text())
        source_identity(args.source)
        report["status"] = "passed"
    except (Failure, OSError, ValueError, subprocess.SubprocessError) as failure:
        report["status"] = "blocked" if str(failure).endswith("_missing") else "failed"
        report["issue"] = str(failure) if isinstance(failure, Failure) else type(failure).__name__
    finally:
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        signal.signal(signal.SIGINT, signal.SIG_IGN)
        for process in reversed(processes):
            try:
                stop_process(process)
            except (OSError, subprocess.SubprocessError):
                report["cleanupVerified"] = False
        if container_started:
            try:
                current = docker("inspect", name, check=False)
                if current.returncode == 0:
                    data = json.loads(current.stdout)[0]
                    if (data["Config"]["Labels"].get("prodimt.c2.owner") != run_id or
                            data["Name"] != "/" + name or (container_id and data["Id"] != container_id)):
                        raise Failure("cleanup_ownership_mismatch")
                    docker("rm", "--force", data["Id"])
                # A missing container means all its ephemeral SQL storage is absent.
                # No host or named SQL volume is ever mounted by this runner.
                remaining = docker("ps", "--all", "--quiet", "--filter", "label=prodimt.c2.owner=" + run_id)
                if remaining.stdout.strip():
                    raise Failure("cleanup_container_remaining")
                report["containerRemovalVerified"] = True
            except (Failure, OSError, ValueError, subprocess.SubprocessError):
                report["cleanupVerified"] = False
        if not report["cleanupVerified"]:
            report.update(status="failed", recoveryPending=True, issue="cleanup_pending")
        # Credentials are private while running and destroyed even on failure.
        for private in ("sql.private.env", "config.private.json", "browser-private.json"):
            (directory / private).unlink(missing_ok=True)
        # Playwright diagnostics can include fragment tokens and password inputs.
        # Do not retain synthetic credentials in logs or error-context Markdown.
        for artifact in directory.rglob("*"):
            if artifact.is_file() and artifact.suffix in (".json", ".jsonl", ".log", ".txt", ".md"):
                content = artifact.read_text(errors="replace")
                artifact.write_text(redact_diagnostics(content))
        report["artifacts"] = {str(path.relative_to(directory)): hashlib.sha256(path.read_bytes()).hexdigest()
                               for path in sorted(directory.rglob("*")) if path.is_file() and path.name != "gate.json"}
        (directory / "gate.json").write_text(json.dumps(report, indent=2) + "\n")
        summary = {key: report[key] for key in ("status", "sourceCommit", "cleanupVerified", "recoveryPending")}
        summary.update(reportDirectory=str(directory), issue=report.get("issue"))
        print(json.dumps(summary))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    sys.exit(main())
