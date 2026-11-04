#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Скрипторїй — среда разработки для языка «Азъ».
Мѣстный сервер (127.0.0.1) + окно Edge въ режимѣ приложенія.

    python ide/server.py [путь_къ_проекту] [--port N] [--no-browser]
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
import threading
import webbrowser
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from pathlib import Path
from urllib.parse import urlparse, parse_qs, unquote

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
sys.path.insert(0, str(REPO / 'compiler'))
import az  # noqa: E402

STATIC = HERE / 'static'
DOCS = REPO / 'docs'
HIDDEN = {'.git', '__pycache__', '.idea', '.vscode', 'node_modules'}

state = {'root': REPO / 'examples' / 'Адонаи'}
build_lock = threading.Lock()


def write(p: Path, text: str):
    with open(p, 'w', encoding='utf-8', newline=chr(10)) as f:
        f.write(text)


def safe(rel: str) -> Path:
    root = state['root'].resolve()
    p = (root / rel).resolve()
    if p != root and root not in p.parents:
        raise PermissionError('путь внѣ проекта')
    return p


def tree(p: Path, root: Path):
    items = []
    try:
        entries = sorted(p.iterdir(), key=lambda x: (not x.is_dir(), x.name.lower()))
    except OSError:
        return items
    for e in entries:
        if e.name in HIDDEN or e.name.startswith('.'):
            continue
        node = {'name': e.name, 'path': e.relative_to(root).as_posix(), 'dir': e.is_dir()}
        if e.is_dir():
            node['children'] = tree(e, root)
        items.append(node)
    return items


def compile_file(path: Path):
    src = path.read_text(encoding='utf-8')
    return az.compile_to_c(src, path.name)


def diag(e: az.AzError):
    return {'line': e.line, 'col': e.col, 'msg': e.msg}


def lang_info():
    kinds = {}
    for w, k in az.KW.items():
        kinds.setdefault(k, []).append(w)
    return {'keywords': az.KW, 'types': sorted(az.TYPES), 'builtins': sorted(az.BUILTINS), 'version': az.VERSION}


class H(BaseHTTPRequestHandler):
    server_version = 'Skriptorij/1.0'

    def log_message(self, fmt, *args):
        pass

    def send(self, code, body, ctype='application/json; charset=utf-8'):
        if isinstance(body, (dict, list)):
            body = json.dumps(body, ensure_ascii=False).encode('utf-8')
        elif isinstance(body, str):
            body = body.encode('utf-8')
        self.send_response(code)
        self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(body)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        self.wfile.write(body)

    def body(self):
        n = int(self.headers.get('Content-Length') or 0)
        return json.loads(self.rfile.read(n).decode('utf-8') or '{}')

    # ---------- GET ----------
    def do_GET(self):
        u = urlparse(self.path)
        q = {k: v[0] for k, v in parse_qs(u.query).items()}
        path = unquote(u.path)
        try:
            if path == '/api/project':
                root = state['root']
                return self.send(200, {'root': str(root), 'name': root.name, 'tree': tree(root, root)})
            if path == '/api/file':
                p = safe(q['path'])
                return self.send(200, {'path': q['path'], 'content': p.read_text(encoding='utf-8')})
            if path == '/api/lang':
                return self.send(200, lang_info())
            if path == '/api/icons':
                d = STATIC / 'icons'
                have = sorted(f.stem for f in d.glob('*.jpg')) if d.exists() else []
                return self.send(200, {'have': have})
            if path.startswith('/docs/'):
                p = (DOCS / path[6:]).resolve()
                if DOCS.resolve() in p.parents and p.exists():
                    return self.send(200, p.read_bytes(), 'text/markdown; charset=utf-8')
                return self.send(404, 'нѣсть')
            # статика
            rel = 'index.html' if path in ('/', '') else path.lstrip('/')
            p = (STATIC / rel).resolve()
            if STATIC.resolve() not in p.parents or not p.is_file():
                return self.send(404, 'Не обрѣтено', 'text/plain; charset=utf-8')
            ctype = {'.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8',
                     '.js': 'text/javascript; charset=utf-8', '.svg': 'image/svg+xml',
                     '.jpg': 'image/jpeg', '.png': 'image/png', '.json': 'application/json',
                     '.ico': 'image/x-icon'}.get(p.suffix.lower(), 'application/octet-stream')
            return self.send(200, p.read_bytes(), ctype)
        except PermissionError as e:
            return self.send(403, {'error': str(e)})
        except Exception as e:  # noqa
            return self.send(500, {'error': f'{type(e).__name__}: {e}'})

    # ---------- POST ----------
    def do_POST(self):
        path = urlparse(self.path).path
        try:
            data = self.body()
            if path == '/api/save':
                p = safe(data['path'])
                write(p, data['content'])
                return self.send(200, {'ok': True})
            if path == '/api/new':
                p = safe(data['path'])
                if p.exists():
                    return self.send(200, {'ok': False, 'error': 'таковый уже есть'})
                if data.get('dir'):
                    p.mkdir(parents=True)
                else:
                    p.parent.mkdir(parents=True, exist_ok=True)
                    write(p, data.get('content', ''))
                return self.send(200, {'ok': True})
            if path == '/api/open-project':
                p = Path(data['root']).expanduser()
                if not p.is_dir():
                    return self.send(200, {'ok': False, 'error': 'нѣсть таковыя папки'})
                state['root'] = p.resolve()
                return self.send(200, {'ok': True})
            if path == '/api/check':
                try:
                    az.compile_to_c(data['content'], 'провѣрка')
                    return self.send(200, {'ok': True, 'diags': []})
                except az.AzError as e:
                    return self.send(200, {'ok': False, 'diags': [diag(e)]})
            if path == '/api/c':
                try:
                    return self.send(200, {'ok': True, 'c': az.compile_to_c(data['content'], data.get('name', '?'))})
                except az.AzError as e:
                    return self.send(200, {'ok': False, 'diags': [diag(e)]})
            if path in ('/api/build', '/api/run', '/api/console'):
                return self.build_and_run(path, data)
            return self.send(404, {'error': 'нѣсть'})
        except PermissionError as e:
            return self.send(403, {'error': str(e)})
        except Exception as e:  # noqa
            return self.send(500, {'error': f'{type(e).__name__}: {e}'})

    def build_and_run(self, path, data):
        src_path = safe(data['path'])
        if 'content' in data:
            write(src_path, data['content'])
        try:
            c_code = compile_file(src_path)
        except az.AzError as e:
            return self.send(200, {'ok': False, 'stage': 'compile', 'diags': [diag(e)]})
        if path == '/api/build':
            out = state['root'] / 'собрано' / (src_path.stem + '.exe')
        else:
            out = Path(tempfile.gettempdir()) / 'az_run' / (src_path.stem + '.exe')
        try:
            with build_lock:
                cc, log = az.build_exe(c_code, out)
        except az.AzError as e:
            return self.send(200, {'ok': False, 'stage': 'cc', 'log': e.msg})
        except PermissionError:
            return self.send(200, {'ok': False, 'stage': 'cc',
                                   'log': f'Не можно перезаписать {out} — программа ещё бѣжитъ?'})
        res = {'ok': True, 'exe': str(out), 'cc': Path(cc).name}
        if path == '/api/console':
            flags = getattr(subprocess, 'CREATE_NEW_CONSOLE', 0)
            subprocess.Popen(['cmd', '/c', str(out), '&', 'echo.', '&', 'pause'],
                             cwd=str(src_path.parent), creationflags=flags)
        elif path == '/api/run':
            try:
                r = subprocess.run([str(out)], input=data.get('stdin', '').encode('utf-8'),
                                   capture_output=True, timeout=float(data.get('timeout', 20)),
                                   cwd=str(src_path.parent))
                res.update(stdout=r.stdout.decode('utf-8', 'replace'),
                           stderr=r.stderr.decode('utf-8', 'replace'), code=r.returncode)
            except subprocess.TimeoutExpired as e:
                res.update(stdout=(e.stdout or b'').decode('utf-8', 'replace'),
                           stderr='\nПрограмма бѣжала слишкомъ долго и была остановлена.', code=-1)
        return self.send(200, res)


def find_edge():
    for base in (os.environ.get('ProgramFiles(x86)'), os.environ.get('ProgramFiles'),
                 os.environ.get('LOCALAPPDATA')):
        if base:
            p = Path(base) / 'Microsoft' / 'Edge' / 'Application' / 'msedge.exe'
            if p.exists():
                return p
    return shutil.which('msedge') or shutil.which('chrome')


def main():
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8')
        except Exception:
            pass
    args = sys.argv[1:]
    port, browser = 0, True
    if '--port' in args:
        i = args.index('--port')
        port = int(args[i + 1])
        del args[i:i + 2]
    if '--no-browser' in args:
        browser = False
        args.remove('--no-browser')
    if args:
        state['root'] = Path(args[0]).resolve()
    srv = ThreadingHTTPServer(('127.0.0.1', port), H)
    url = f'http://127.0.0.1:{srv.server_address[1]}/'
    print('☦  Скрипторїй отверзся:', url)
    print('   Проектъ:', state['root'])
    print('   Затворите сие окно (или Ctrl+C), дабы окончити.')
    if browser:
        edge = find_edge()
        if edge:
            prof = Path(tempfile.gettempdir()) / 'skriptorij-profile'
            subprocess.Popen([str(edge), f'--app={url}', '--window-size=1400,900',
                              f'--user-data-dir={prof}', '--no-first-run'])
        else:
            webbrowser.open(url)
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print('\nСпаси Господи. Аминь.')


if __name__ == '__main__':
    main()
