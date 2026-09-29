#!/usr/bin/env python3
"""Exercise deployment swaps in a disposable home with mocked service and HTTP tools."""
import hashlib
import io
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import tarfile
import tempfile
import unittest

WORKER = Path(__file__).with_name('deploy-release.sh').resolve()
RUN = '20260929T120000Z-aaaaaaaaaaaa-1'
COMMIT = 'a' * 40
SHIM = '''#!/usr/bin/env python3
import json,os,pathlib,sys
home=pathlib.Path(os.environ['HOME']); base=home/'cedarclerk'
name=pathlib.Path(sys.argv[0]).name; args=sys.argv[1:]
state=home/'active'
if name=='flock':
    import fcntl
    fcntl.flock(int(args[-1]), fcntl.LOCK_EX | fcntl.LOCK_NB)
elif name=='sha256sum':
    import hashlib
    digest,path=sys.stdin.read().strip().split('  ',1)
    sys.exit(0 if hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()==digest else 1)
elif name=='sudo':
    action=args[2]
    if action=='stop': state.unlink(missing_ok=True)
    if action=='start': state.touch()
    with (home/'actions').open('a') as f: f.write(action+'\\n')
elif name=='systemctl':
    sys.exit(0 if state.exists() else 3)
elif name=='dotnet':
    print('Microsoft.AspNetCore.App 10.0.1 [test]')
    print('Microsoft.NETCore.App 10.0.1 [test]')
elif name=='curl':
    marker=json.loads((base/'app/wwwroot/deployment.json').read_text())
    if '-o' in args:
        pathlib.Path(args[args.index('-o')+1]).write_text('bad' if os.environ.get('BAD_LOGIN') else '<app-root></app-root>')
    else:
        print(json.dumps(marker if any('/deployment.json' in a for a in args) else {'version':marker['version']}))
'''


class DeployTests(unittest.TestCase):
    def scenario(self, *, bad_checksum=False, bad_login=False, traversal=False, wrong_live=False, corrupt_db=False):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        home = Path(temp.name)
        base = home/'cedarclerk'
        stage = base/'staging'/RUN
        stage.mkdir(parents=True)
        for d in ['app/wwwroot', 'app.prev', 'data', 'bin', '.dotnet']:
            (base/d if d not in ['bin', '.dotnet'] else home/d).mkdir(parents=True)
        (base/'app/wwwroot/deployment.json').write_text(json.dumps({'version': '0.23.6', 'commit': 'b'*40}))
        (base/'app.prev/keep').write_text('older release')
        (home/'active').touch()
        with sqlite3.connect(base/'data/cedar.db') as db:
            db.execute('CREATE TABLE sample(value TEXT)')
            db.execute("INSERT INTO sample VALUES ('preserve me')")
        if corrupt_db:
            (base/'data/cedar.db').write_bytes(b'not a database')
        for name in ['sudo', 'systemctl', 'curl', 'flock', 'sha256sum']:
            p = home/'bin'/name
            p.write_text(SHIM)
            p.chmod(0o755)
        p = home/'.dotnet/dotnet'
        p.write_text(SHIM)
        p.chmod(0o755)
        files = {
            'CedarClerk.Server.dll': b'dll',
            'CedarClerk.Server.runtimeconfig.json': b'{"runtimeOptions":{"tfm":"net10.0"}}',
            'wwwroot/index.html': b'<app-root></app-root>',
            'wwwroot/deployment.json': json.dumps({'version':'0.24.0','commit':COMMIT}).encode(),
        }
        if traversal:
            files['../escape'] = b'no'
        archive = stage/'release.tar.gz'
        with tarfile.open(archive, 'w:gz') as tar:
            for name, content in files.items():
                entry = tarfile.TarInfo(name)
                entry.size = len(content)
                tar.addfile(entry, io.BytesIO(content))
        digest = hashlib.sha256(archive.read_bytes()).hexdigest()
        if bad_checksum:
            digest = '0'*64
        env = dict(os.environ, HOME=str(home), PATH=str(home/'bin')+os.pathsep+os.environ['PATH'])
        if bad_login:
            env['BAD_LOGIN'] = '1'
        result = subprocess.run(['bash', str(WORKER), RUN, digest, str(len(files)), '0.24.0', COMMIT, '0.22.0' if wrong_live else '0.23.6'], env=env, capture_output=True, text=True, timeout=15)
        return home, base, stage, result

    def test_success_preserves_previous_and_backs_up_database(self):
        home, base, stage, result = self.scenario()
        self.assertEqual(result.returncode, 0, result.stdout+result.stderr)
        self.assertEqual((stage/'result').read_text().strip(), 'success')
        self.assertTrue((base/f'app.prev.{RUN}/keep').exists())
        self.assertEqual(json.loads((base/'app.prev/wwwroot/deployment.json').read_text())['version'], '0.23.6')
        with sqlite3.connect(base/f'data/backups/predeploy-{RUN}.db') as db:
            self.assertEqual(db.execute('SELECT value FROM sample').fetchone()[0], 'preserve me')
        self.assertTrue((home/'active').exists())

    def test_checksum_failure_never_stops_service(self):
        home, _, stage, result = self.scenario(bad_checksum=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((home/'actions').exists())
        self.assertEqual((stage/'result').read_text().strip(), 'failed-before-stop')

    def test_unsafe_archive_never_stops_service(self):
        home, _, stage, result = self.scenario(traversal=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((home/'actions').exists())
        self.assertFalse((stage/'escape').exists())

    def test_wrong_live_version_never_stops_service(self):
        home, _, stage, result = self.scenario(wrong_live=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((home/'actions').exists())
        self.assertEqual((stage/'result').read_text().strip(), 'failed-before-stop')

    def test_failed_backup_restarts_original_without_swap(self):
        home, base, stage, result = self.scenario(corrupt_db=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((stage/'result').read_text().strip(), 'rolled-back')
        self.assertEqual(json.loads((base/'app/wwwroot/deployment.json').read_text())['version'], '0.23.6')
        self.assertFalse((stage/'app.failed').exists())
        self.assertTrue((home/'active').exists())

    def test_failed_smoke_restores_previous_application(self):
        home, base, stage, result = self.scenario(bad_login=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual((stage/'result').read_text().strip(), 'rolled-back')
        self.assertEqual(json.loads((base/'app/wwwroot/deployment.json').read_text())['version'], '0.23.6')
        self.assertTrue((stage/'app.failed').exists())
        self.assertTrue((home/'active').exists())
        with sqlite3.connect(base/'data/cedar.db') as db:
            self.assertEqual(db.execute('SELECT value FROM sample').fetchone()[0], 'preserve me')


if __name__ == '__main__':
    unittest.main()
