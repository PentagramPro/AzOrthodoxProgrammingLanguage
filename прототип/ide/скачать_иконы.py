#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Скачиваетъ настоящіе образы для иконостаса Скрипторія съ Викисклада
(commons.wikimedia.org): древнія иконы — общественное достояніе.
Кладётъ ихъ въ ide/static/icons/<id>.jpg и пишетъ icons/CREDITS.md
съ авторами и лицензіями. Провѣрьте глазами, что найдено то, что нужно:
не понравилось — удалите файлъ или подмѣните своимъ (<id>.jpg).

    python ide/скачать_иконы.py [--force]
"""
import json
import re
import time
import urllib.error
import sys
import urllib.parse
import urllib.request
from pathlib import Path

OUT = Path(__file__).resolve().parent / 'static' / 'icons'
UA = 'SkriptorijIDE/1.0 (local hobby IDE; python-urllib)'
API = 'https://commons.wikimedia.org/w/api.php'

# Имена файловъ подобраны и провѣрены глазами; «File:…» — точное имя на Викискладѣ.
QUERIES = {
    'troica': ['File:Andrey Rublev - Св. Троица - Google Art Project.jpg'],
    'spas': ['File:Christos Acheiropoietos.jpg'],
    'kirill-mefodiy': ['File:Sts Cyril and Methodius Icon in Tresonche.png'],
    'ioann-bogoslov': ['File:John the Evangelist in Silence by Nectarius Kulyuksin 1679.jpg'],
    'nestor': ['File:Нестор Летописец.jpg', 'File:Nestor 1842.png'],
    'maksim-grek': ['File:Maxim Grek (18th c., GIM).jpg'],
    'stefan-permsky': ['File:Stephen of Perm by O.Chirikov.jpg'],
    'gavriil': ['File:Archanioł Gabriel.jpg'],  # «Ангелъ Златыя власы», Новгородъ, XII в.
    'isidor': [],  # православной иконы на Викискладѣ нѣтъ — остаётся рисованная заставка
    'bogorodica': ['File:Virgin of Vladimir.jpg'],
}


def get(url, tries=5):
    for k in range(tries):
        time.sleep(1.5)  # вѣжливо къ Викискладу
        req = urllib.request.Request(url, headers={'User-Agent': UA})
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code != 429 or k == tries - 1:
                raise
            time.sleep(10 * (k + 1))


def find(query):
    if query.startswith('File:'):  # точное имя файла
        q = urllib.parse.urlencode({'action': 'query', 'format': 'json', 'titles': query, 'prop': 'imageinfo',
                                    'iiprop': 'url|extmetadata', 'iiurlwidth': 240})
        for p in json.loads(get(f'{API}?{q}')).get('query', {}).get('pages', {}).values():
            ii = (p.get('imageinfo') or [{}])[0]
            if ii.get('thumburl'):
                return p['title'], ii
        return None, None
    q = urllib.parse.urlencode({
        'action': 'query', 'format': 'json', 'generator': 'search', 'gsrnamespace': 6, 'gsrlimit': 5,
        'gsrsearch': query + ' filetype:bitmap', 'prop': 'imageinfo', 'iiprop': 'url|extmetadata',
        'iiurlwidth': 240,
    })
    data = json.loads(get(f'{API}?{q}'))
    pages = sorted((data.get('query') or {}).get('pages', {}).values(), key=lambda p: p.get('index', 99))
    for p in pages:
        ii = (p.get('imageinfo') or [{}])[0]
        if ii.get('thumburl'):
            return p['title'], ii
    return None, None


def main():
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    force = '--force' in sys.argv
    OUT.mkdir(parents=True, exist_ok=True)
    credits = ['# Источники образовъ (Викискладъ)', '']
    for sid, query in QUERIES.items():
        dst = OUT / f'{sid}.jpg'
        if dst.exists() and not force:
            print(f'{sid}: уже есть')
            continue
        title = ii = None
        for q in query:
            title, ii = find(q)
            if title:
                break
        if not title:
            print(f'{sid}: не обрѣтено')
            continue
        dst.write_bytes(get(ii['thumburl']))
        meta = ii.get('extmetadata', {})
        lic = meta.get('LicenseShortName', {}).get('value', '?')
        artist = meta.get('Artist', {}).get('value', '?')
        artist = re.sub(r'<span style="display: ?none;?">.*?</span>', '', artist, flags=re.S)
        artist = ' '.join(re.sub(r'<[^>]+>', ' ', artist).split())[:120] or '?'
        credits.append(f'- **{sid}**: [{title}]({ii.get("descriptionurl")}) — {lic}; {artist}')
        print(f'{sid}: {title} ({lic})')
    (OUT / 'CREDITS.md').write_text('\n'.join(credits) + '\n', encoding='utf-8')
    print('Готово. Перезагрузите Скрипторій (F5).')


if __name__ == '__main__':
    main()
