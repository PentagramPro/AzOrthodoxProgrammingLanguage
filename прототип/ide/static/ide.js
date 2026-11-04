'use strict';
/* ═══════════════ Скрипторїй — среда для языка «Азъ» ═══════════════ */

const $ = (s, el = document) => el.querySelector(s);
const esc = s => s.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const api = async (url, body) => {
  const r = await fetch(url, body === undefined ? {} : {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
  });
  const j = await r.json();
  if (!r.ok) throw new Error(j.error || r.statusText);
  return j;
};

/* ---------- нормализація словъ (зеркало compiler/az.py) ---------- */
const LMAP = {
  'ѣ': 'е', 'є': 'е', 'ѥ': 'е', 'ё': 'е', 'і': 'и', 'ї': 'и', 'ѵ': 'и', 'ѷ': 'и', 'ꙇ': 'и', 'й': 'и',
  'ѳ': 'ф', 'ѿ': 'от', 'ѡ': 'о', 'ѻ': 'о', 'ꙍ': 'о', 'ѽ': 'о', 'ꚙ': 'о', 'ꙩ': 'о', 'ꙫ': 'о', 'ꙭ': 'о',
  'ꙋ': 'у', 'ѹ': 'у', 'ѫ': 'у', 'ѭ': 'ю', 'ѕ': 'з', 'ꙃ': 'з', 'ꙁ': 'з', 'ꙗ': 'я', 'ѧ': 'я', 'ѩ': 'я',
  'ѯ': 'кс', 'ѱ': 'пс', 'ꙑ': 'ы', 'ꙟ': 'я',
};
function norm(w) {
  w = w.normalize('NFD').toLowerCase().replace(/[\p{M}\u0482]/gu, '');
  const cyr = /[\u0400-\u052f\ua640-\ua69f]/.test(w);
  let o = '';
  for (let ch of w) { if (cyr && ch === 'i') ch = 'и'; o += LMAP[ch] ?? ch; }
  o = o.replace(/оу/g, 'у');
  if (o.length > 1 && o.endsWith('ъ')) o = o.slice(0, -1);
  return o;
}
const NUMERAL_LETTERS = new Set('авгдєеѕзꙁиѳіiклмнѯѻопчҁрстуѵꙋфхѱѿѡц');
function isNumeral(raw) {
  if (!raw.includes('\u0483')) return false;
  const base = raw.normalize('NFC').toLowerCase().replace(/[\p{M}\u0482]/gu, '');
  return base.length > 0 && [...base].every(c => NUMERAL_LETTERS.has(c));
}

let LANG = { keywords: {}, types: [], builtins: [] };
let TYPES = new Set(), BUILTINS = new Set();
const KW_CLASS = {
  def: 't-def', return: 't-kw', if: 't-kw', else: 't-kw', while: 't-kw', for: 't-kw', in: 't-kw',
  break: 't-kw', continue: 't-kw', print: 't-kw', anathema: 't-kw', is: 't-op', assign: 't-op',
  decl_many: 't-type', decl_one: 't-type', not: 't-op', and: 't-op', or: 't-op', gt: 't-op', lt: 't-op',
  true: 't-const', false: 't-const', none: 't-const', index: 't-op',
};

/* ---------- подсвѣтка ---------- */
const TOK_RE = /(#.*$)|(«[^»]*»?|"(?:\\.|[^"\\])*"?)|([\p{L}\p{N}\p{M}_\u0482]+)|(\s+)|([\s\S])/uy;
function highlightLine(line, errCol) {
  let out = '', m, prev = null;
  TOK_RE.lastIndex = 0;
  while (TOK_RE.lastIndex < line.length && (m = TOK_RE.exec(line))) {
    const start = m.index, t = m[0];
    let cls = '';
    if (m[1]) cls = 't-com';
    else if (m[2]) cls = 't-str';
    else if (m[3]) {
      if (/^[0-9]/.test(t) || isNumeral(t)) cls = 't-num';
      else {
        const n = norm(t), kind = LANG.keywords[n];
        if (kind) cls = KW_CLASS[kind] || 't-kw';
        else if (prev === 'def') cls = 't-fn';
        else if (prev === 'decl' && TYPES.has(n)) cls = 't-type';
        else if (BUILTINS.has(n)) cls = 't-bi';
        prev = kind === 'def' ? 'def' : (kind === 'decl_many' || kind === 'decl_one') ? 'decl' : null;
      }
    } else if (!m[4]) prev = null;
    const isErr = errCol && errCol - 1 >= start && errCol - 1 < start + t.length && !m[4];
    if (isErr) cls += ' t-err';
    if (cls === 't-fn') {
      // буквица киноварью
      const g = t.match(/^.[\p{M}]*/u)[0];
      out += `<span class="t-fn"><span class="buk">${esc(g)}</span>${esc(t.slice(g.length))}</span>`;
    } else out += cls ? `<span class="${cls.trim()}">${esc(t)}</span>` : esc(t);
  }
  if (errCol && errCol > line.length) out += '<span class="t-err"> </span>';
  return out;
}
function highlight(text, diags) {
  const errs = new Map((diags || []).map(d => [d.line, d.col || 1]));
  return text.split('\n').map((l, i) => highlightLine(l, errs.get(i + 1))).join('\n') + '\n ';
}

/* ---------- состояніе ---------- */
const S = { project: null, tabs: [], active: null, expanded: new Set(), selPath: null, lastFind: '' };

/* ---------- вкладки и редакторъ ---------- */
function makeEditor(tab) {
  const ed = document.createElement('div');
  ed.className = 'ed' + (tab.readonly ? ' readonly' : '');
  ed.innerHTML = '<div class="ed-inner"><div class="gutter"></div><div class="code"><pre></pre><textarea spellcheck="false" wrap="off" autocapitalize="off" autocomplete="off"></textarea></div></div>';
  tab.ed = ed; tab.gut = $('.gutter', ed); tab.pre = $('pre', ed); tab.ta = $('textarea', ed);
  tab.ta.value = tab.content;
  tab.ta.setSelectionRange(0, 0);
  tab.ta.readOnly = !!tab.readonly;
  tab.ta.addEventListener('input', () => onEdit(tab));
  tab.ta.addEventListener('keydown', e => onKey(e, tab));
  ['keyup', 'click', 'select'].forEach(ev => tab.ta.addEventListener(ev, () => updatePos(tab)));
  $('#editor-host').appendChild(ed);
  render(tab);
}
function render(tab) {
  const text = tab.ta.value;
  tab.pre.innerHTML = tab.readonly ? esc(text) + '\n ' : highlight(text, tab.diags);
  const n = text.split('\n').length, errs = new Map((tab.diags || []).map(d => [d.line, d.msg]));
  let g = '';
  for (let i = 1; i <= n; i++) g += errs.has(i) ? `<div class="err" title="${esc(errs.get(i))}">${i}</div>` : `<div>${i}</div>`;
  tab.gut.innerHTML = g;
}
function onEdit(tab) {
  render(tab);
  setDirty(tab, tab.ta.value !== tab.saved);
  clearTimeout(tab.chk);
  tab.chk = setTimeout(() => liveCheck(tab), 500);
}
async function liveCheck(tab) {
  if (tab.readonly || !tab.path.endsWith('.азъ')) return;
  try {
    const r = await api('/api/check', { content: tab.ta.value });
    tab.diags = r.diags;
    render(tab);
    if (tab === S.active) status(r.ok ? 'Прегрѣше́нїй не ѡ҆брѣте́но.' : `Стрк. ${r.diags[0].line}: ${r.diags[0].msg}`, !r.ok);
  } catch (e) { /* сервер молчитъ */ }
}
function setDirty(tab, d) { tab.dirty = d; tab.tabEl?.classList.toggle('dirty', d); }
function updatePos(tab) {
  const v = tab.ta.value.slice(0, tab.ta.selectionStart).split('\n');
  $('#st-pos').textContent = `Стрк. ${v.length}, стлб. ${[...v[v.length - 1]].length + 1}`;
}

function insertText(ta, text) {
  ta.focus();
  if (!document.execCommand('insertText', false, text)) ta.setRangeText(text, ta.selectionStart, ta.selectionEnd, 'end');
}
function lineBounds(ta) {
  const v = ta.value, s = v.lastIndexOf('\n', ta.selectionStart - 1) + 1;
  let e = v.indexOf('\n', ta.selectionEnd - (ta.selectionEnd > ta.selectionStart && v[ta.selectionEnd - 1] === '\n' ? 1 : 0));
  if (e < 0) e = v.length;
  return [s, e];
}
function replaceLines(ta, fn) {
  const [s, e] = lineBounds(ta);
  const lines = ta.value.slice(s, e).split('\n').map(fn).join('\n');
  ta.setSelectionRange(s, e);
  insertText(ta, lines);
  ta.setSelectionRange(s, s + lines.length);
}
function onKey(e, tab) {
  const ta = tab.ta;
  if (tab.readonly) return;
  if (e.key === 'Tab') {
    e.preventDefault();
    if (e.shiftKey) replaceLines(ta, l => l.replace(/^ {1,4}/, ''));
    else if (ta.value.slice(ta.selectionStart, ta.selectionEnd).includes('\n')) replaceLines(ta, l => '    ' + l);
    else insertText(ta, '    ');
  } else if (e.key === 'Enter' && !e.ctrlKey && !e.altKey) {
    e.preventDefault();
    const before = ta.value.slice(0, ta.selectionStart);
    const line = before.slice(before.lastIndexOf('\n') + 1);
    let ind = line.match(/^ */)[0];
    if (/:\s*(#.*)?$/.test(line.replace(/«[^»]*»/g, ''))) ind += '    ';
    insertText(ta, '\n' + ind);
  } else if (e.key === '/' && e.ctrlKey) {
    e.preventDefault();
    const [s, en] = lineBounds(ta);
    const all = ta.value.slice(s, en).split('\n').filter(l => l.trim()).every(l => /^\s*#/.test(l));
    replaceLines(ta, l => all ? l.replace(/^(\s*)# ?/, '$1') : l.replace(/^(\s*)/, '$1# '));
  } else if (e.key === 'Backspace' && ta.selectionStart === ta.selectionEnd) {
    const before = ta.value.slice(0, ta.selectionStart), line = before.slice(before.lastIndexOf('\n') + 1);
    if (line.length && /^ +$/.test(line) && line.length % 4 === 0) {
      e.preventDefault();
      ta.setSelectionRange(ta.selectionStart - 4, ta.selectionStart);
      insertText(ta, '');
    }
  }
}

async function openFile(path, opts = {}) {
  let tab = S.tabs.find(t => t.path === path);
  if (!tab) {
    let content = opts.content;
    if (content === undefined) {
      if (/\.(exe|jpg|png|ico)$/i.test(path)) { status('Се́й фа́йлъ не чита́емъ.'); return; }
      content = (await api('/api/file?path=' + encodeURIComponent(path))).content;
    }
    tab = { path, name: opts.name || path.split('/').pop(), content, saved: content, diags: [], readonly: !!opts.readonly };
    S.tabs.push(tab);
    makeEditor(tab);
    if (!tab.readonly) liveCheck(tab);
  }
  activate(tab);
}
function activate(tab) {
  S.active = tab;
  S.tabs.forEach(t => t.ed.style.display = t === tab ? '' : 'none');
  $('#welcome').style.display = tab ? 'none' : '';
  renderTabs(); renderCrumbs();
  if (tab) {
    document.title = `${S.project?.name || ''} - [${S.project?.root || ''}] - …\\${tab.path.replace(/\//g, '\\')} — Скрипторїй`;
    S.selPath = tab.path; renderTree();
    setTimeout(() => tab.ta.focus(), 0);
    updatePos(tab);
  } else document.title = 'Скрипторїй';
}
function renderTabs() {
  const host = $('#tabs');
  host.innerHTML = '';
  S.tabs.forEach(t => {
    const el = document.createElement('div');
    el.className = 'tab' + (t === S.active ? ' active' : '') + (t.dirty ? ' dirty' : '');
    el.innerHTML = `<span class="fi ${fileIcon(t.path)}"></span><span class="tname">${esc(t.name)}</span><span class="tx" title="Затвори́ти">✕</span>`;
    el.onmousedown = e => { if (e.button === 1) { e.preventDefault(); closeTab(t); } };
    el.onclick = e => e.target.classList.contains('tx') ? closeTab(t) : activate(t);
    t.tabEl = el;
    host.appendChild(el);
  });
}
function closeTab(t) {
  t = t || S.active;
  if (!t) return;
  if (t.dirty && !confirm(`«${t.name}» не сохране́нъ. Затвори́ти безъ сохране́нїѧ?`)) return;
  t.ed.remove();
  const i = S.tabs.indexOf(t);
  S.tabs.splice(i, 1);
  activate(S.tabs[Math.min(i, S.tabs.length - 1)] || null);
}
async function save(t = S.active) {
  if (!t || t.readonly) return;
  await api('/api/save', { path: t.path, content: t.ta.value });
  t.saved = t.ta.value; setDirty(t, false);
  status(`Сохране́но: ${t.name}`);
}

/* ---------- древо проекта ---------- */
function fileIcon(p) {
  if (p.endsWith('.азъ')) return 'az';
  if (/\.exe$/i.test(p)) return 'exe';
  return 'other';
}
async function loadProject() {
  S.project = await api('/api/project');
  if (!S.expanded.size) {
    const walk = (nodes, d) => nodes.forEach(n => { if (n.dir && d < 2) { S.expanded.add(n.path); walk(n.children, d + 1); } });
    S.expanded.add('');
    walk(S.project.tree, 0);
  }
  renderTree(); renderCrumbs();
}
function renderTree() {
  if (!S.project) return;
  const host = $('#tree');
  let html = '';
  const rootOpen = S.expanded.has('');
  html += `<div class="tn" data-dir="" style="padding-left:4px"><span class="tw">${rootOpen ? '▾' : '▸'}</span><span class="fi folder"></span><b>${esc(S.project.name)}</b> <span style="color:#999;font-size:13px">(${esc(S.project.root)})</span></div>`;
  const walk = (nodes, depth) => nodes.forEach(n => {
    const pad = 4 + depth * 16;
    if (n.dir) {
      const open = S.expanded.has(n.path);
      html += `<div class="tn" data-dir="${esc(n.path)}" style="padding-left:${pad}px"><span class="tw">${open ? '▾' : '▸'}</span><span class="fi folder"></span>${esc(n.name)}</div>`;
      if (open) walk(n.children, depth + 1);
    } else {
      html += `<div class="tn${n.path === S.selPath ? ' sel' : ''}" data-file="${esc(n.path)}" style="padding-left:${pad}px"><span class="tw"></span><span class="fi ${fileIcon(n.name)}"></span>${esc(n.name)}</div>`;
    }
  });
  if (rootOpen) walk(S.project.tree, 1);
  host.innerHTML = html;
}
$('#tree').addEventListener('click', e => {
  const n = e.target.closest('.tn');
  if (!n) return;
  if (n.dataset.dir !== undefined) {
    const p = n.dataset.dir;
    S.expanded.has(p) ? S.expanded.delete(p) : S.expanded.add(p);
    S.selDir = p;
    renderTree();
  } else {
    S.selPath = n.dataset.file;
    S.selDir = n.dataset.file.split('/').slice(0, -1).join('/');
    openFile(n.dataset.file);
  }
});
function renderCrumbs() {
  const parts = [S.project?.name || ''];
  if (S.active && !S.active.readonly) parts.push(...S.active.path.split('/'));
  $('#breadcrumbs').innerHTML = parts.map((p, i) =>
    `<span class="crumb"><span class="fi ${i === parts.length - 1 && i > 0 ? fileIcon(p) : 'folder'}"></span>${esc(p)}</span>`).join('');
}

/* ---------- исходъ (нижняя панель) ---------- */
function out(text, cls = '') {
  const o = $('#output');
  const span = document.createElement('span');
  if (cls) span.className = cls;
  span.textContent = text;
  o.appendChild(span);
  o.scrollTop = o.scrollHeight;
}
function outDiag(d, file) {
  const o = $('#output');
  const a = document.createElement('a');
  a.textContent = `${file}:${d.line}:${d.col}`;
  a.onclick = () => gotoLine(d.line, d.col);
  o.appendChild(a);
  out(`: прегрѣше́нїе: ${d.msg}\n`, 'o-err');
}
function gotoLine(line, col = 1) {
  const t = S.active;
  if (!t) return;
  const lines = t.ta.value.split('\n');
  let off = 0;
  for (let i = 0; i < line - 1 && i < lines.length; i++) off += lines[i].length + 1;
  off += Math.max(0, col - 1);
  t.ta.focus();
  t.ta.setSelectionRange(off, off);
  const lh = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--line-h'));
  t.ed.scrollTop = Math.max(0, (line - 6) * lh);
  updatePos(t);
}
function status(msg, err) {
  const el = $('#st-msg');
  el.textContent = msg;
  el.style.color = err ? '#b00' : '';
}

async function runAction(kind) {
  const t = S.active;
  if (!t || t.readonly) { status('Сперва̀ ѿверзи́ фа́йлъ .азъ', true); return; }
  if (!t.path.endsWith('.азъ')) { status('Пꙋска́ти мо́жно то́кмѡ фа́йлы .азъ', true); return; }
  $('#bottom').classList.remove('hidden');
  const verb = { run: 'Пꙋ́скъ', build: 'Сбо́рка', console: 'Пꙋ́скъ въ консо́ли' }[kind];
  out(`\n☦ ${verb}: ${t.name} …\n`, 'o-sys');
  status(`${verb}…`);
  const t0 = performance.now();
  let r;
  try {
    r = await api('/api/' + kind, { path: t.path, content: t.ta.value, stdin: $('#stdin').value });
  } catch (e) { out(String(e) + '\n', 'o-err'); return; }
  t.saved = t.ta.value; setDirty(t, false);
  if (!r.ok) {
    if (r.diags) { r.diags.forEach(d => outDiag(d, t.name)); t.diags = r.diags; render(t); gotoLine(r.diags[0].line, r.diags[0].col); }
    else out((r.log || 'Неꙋда́ча') + '\n', 'o-err');
    status('Ѡ҆брѣте́ны прегрѣше́нїѧ.', true);
    return;
  }
  t.diags = []; render(t);
  const ms = Math.round(performance.now() - t0);
  if (kind === 'build') { out(`Со́брано: ${r.exe}  (компиля́торъ ${r.cc}, ${ms} мс). Сла́ва Бо́гꙋ!\n`, 'o-ok'); loadProject(); }
  if (kind === 'console') out(`Пꙋще́но во ѻ҆кнѣ̀ консо́ли: ${r.exe}\n`, 'o-ok');
  if (kind === 'run') {
    if (r.stdout) out(r.stdout);
    if (r.stderr) out(r.stderr, 'o-err');
    out(`\n— Ко́нчисѧ съ ко́домъ ${r.code} (${ms} мс). ${r.code === 0 ? 'А҆ми́нь.' : 'Гдⷭ҇и, поми́лꙋй.'}\n`, r.code === 0 ? 'o-ok' : 'o-err');
  }
  status(r.code ? `Ко́дъ исхо́да ${r.code}` : 'Гото́во. А҆ми́нь.', !!r.code);
}

/* ---------- окна (modal) ---------- */
function modal(html) { $('#modal-body').innerHTML = html; $('#modal').hidden = false; $('#modal .modal-box').scrollTop = 0; }
function iconTile(s, big) {
  const photo = S.photos?.has(s.id);
  return `<div class="icon${photo ? ' photo' : ''}" data-saint="${s.id}" title="${esc(s.title)}"${photo ? ` style="background-image:url('icons/${s.id}.jpg')"` : ''}>
    <div class="halo"></div><div class="figure"></div><div class="mono">${s.mono}</div><div class="cap">${s.cap}</div></div>`;
}
function saintCard(s) {
  return `<div class="saint">${iconTile(s, true)}<div><h2 style="margin-top:0">${esc(s.title)}</h2>
    <div class="feast">Память: ${esc(s.feast)}</div><p>${esc(s.life)}</p><div class="why"><b>Чемꙋ̀ покрови́тельствꙋетъ:</b> ${esc(s.why)}</div></div></div>`;
}
function md(src) {
  const inline = s => esc(s).replace(/`([^`]+)`/g, '<code>$1</code>').replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>')
    .replace(/\*([^*]+)\*/g, '<i>$1</i>').replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" target="_blank">$1</a>');
  const L = src.replace(/\r/g, '').split('\n');
  let h = '', i = 0;
  while (i < L.length) {
    const l = L[i];
    if (l.startsWith('```')) {
      const buf = []; i++;
      while (i < L.length && !L[i].startsWith('```')) buf.push(L[i++]);
      i++;
      h += `<pre><code>${/азъ|az/.test(l) ? highlight(buf.join('\n')).slice(0, -2) : esc(buf.join('\n'))}</code></pre>`;
      continue;
    }
    let m;
    if ((m = l.match(/^(#{1,4})\s+(.*)/))) { h += `<h${m[1].length}>${inline(m[2])}</h${m[1].length}>`; i++; continue; }
    if (l.startsWith('|')) {
      const rows = [];
      while (i < L.length && L[i].startsWith('|')) rows.push(L[i++]);
      const cells = r => r.replace(/^\||\|$/g, '').split('|').map(c => c.trim());
      h += '<table>' + rows.filter(r => !/^\|[\s:|-]+\|$/.test(r)).map((r, k) =>
        '<tr>' + cells(r).map(c => k ? `<td>${inline(c)}</td>` : `<th>${inline(c)}</th>`).join('') + '</tr>').join('') + '</table>';
      continue;
    }
    if (/^\s*([-*]|\d+\.)\s/.test(l)) {
      const ord = /^\s*\d+\./.test(l);
      h += ord ? '<ol>' : '<ul>';
      while (i < L.length && /^\s*([-*]|\d+\.)\s/.test(L[i])) h += `<li>${inline(L[i++].replace(/^\s*([-*]|\d+\.)\s/, ''))}</li>`;
      h += ord ? '</ol>' : '</ul>';
      continue;
    }
    if (l.startsWith('>')) { h += `<blockquote>${inline(l.replace(/^>\s?/, ''))}</blockquote>`; i++; continue; }
    if (!l.trim()) { i++; continue; }
    const buf = [];
    while (i < L.length && L[i].trim() && !/^(#|```|\||>|\s*([-*]|\d+\.)\s)/.test(L[i])) buf.push(L[i++]);
    h += `<p>${inline(buf.join(' '))}</p>`;
  }
  return h;
}

/* ---------- поискъ ---------- */
function findBar() {
  const t = S.active;
  if (!t) return;
  let bar = $('.find-bar', t.ed.parentElement);
  if (!bar) {
    bar = document.createElement('div');
    bar.className = 'find-bar';
    bar.innerHTML = '<input placeholder="Найтѝ…"><button title="Да́лѣе (F3)">↓</button><button title="Затвори́ти">✕</button>';
    $('#editor-host').appendChild(bar);
    const inp = $('input', bar);
    inp.onkeydown = e => { if (e.key === 'Enter') findNext(inp.value); if (e.key === 'Escape') bar.remove(); };
    bar.children[1].onclick = () => findNext(inp.value);
    bar.children[2].onclick = () => bar.remove();
  }
  const sel = t.ta.value.slice(t.ta.selectionStart, t.ta.selectionEnd);
  if (sel && !sel.includes('\n')) $('input', bar).value = sel;
  $('input', bar).focus(); $('input', bar).select();
}
function findNext(q = S.lastFind) {
  const t = S.active;
  if (!t || !q) return;
  S.lastFind = q;
  const v = t.ta.value.toLowerCase(), qq = q.toLowerCase();
  let i = v.indexOf(qq, t.ta.selectionEnd);
  if (i < 0) i = v.indexOf(qq);
  if (i < 0) { status(`«${q}» не ѡ҆брѣте́но`, true); return; }
  const line = v.slice(0, i).split('\n').length;
  gotoLine(line);
  t.ta.setSelectionRange(i, i + q.length);
  status(`Ѡ҆брѣте́но въ строцѣ̀ ${line}`);
}

/* ---------- дѣйства ---------- */
const PRAYER = `<h2>Моли́тва предъ на́чатїемъ всѧ́кагѡ дѣ́ла</h2>
<p style="font-size:20px">Царю̀ Нбⷭ҇ный, Ѹ҆тѣ́шителю, Дꙋ́ше и҆́стины, И҆̀же вездѣ̀ сы́й и҆ всѧ̑ и҆сполнѧ́ѧй, Сокро́вище бл҃ги́хъ и҆ жи́зни Пода́телю,
прїидѝ и҆ всели́сѧ въ ны̀, и҆ ѡ҆чи́сти ны̀ ѿ всѧ́кїѧ скве́рны, и҆ спасѝ, бл҃же, дꙋ́шы на́шѧ.</p>
<p class="hint" style="color:#777">Пото́мъ — провѣ́рь ло́ги, пере́чти ᲂу҆сло́вїѧ ци́кла и҆ не грешѝ на компиля́торъ.</p>`;
const ACTIONS = {
  newFile: async () => {
    const dir = S.selDir ?? (S.active ? S.active.path.split('/').slice(0, -1).join('/') : '');
    let name = prompt('И҆́мѧ но́вагѡ фа́йла:', (dir ? dir + '/' : '') + 'но́вый.азъ');
    if (!name) return;
    if (!/\.[^/]+$/.test(name)) name += '.азъ';
    const r = await api('/api/new', { path: name, content: '# ' + name.split('/').pop() + '\n\nглаго́ли «Бл҃гослове́нъ Бг҃ъ на́шъ»\n' });
    if (!r.ok) return alert(r.error);
    name.split('/').slice(0, -1).reduce((a, p) => { const x = a ? a + '/' + p : p; S.expanded.add(x); return x; }, '');
    await loadProject(); openFile(name);
  },
  newDir: async () => {
    const name = prompt('И҆́мѧ но́выѧ па́пки:');
    if (!name) return;
    const r = await api('/api/new', { path: name, dir: true });
    if (!r.ok) return alert(r.error);
    loadProject();
  },
  openProject: async () => {
    const p = prompt('Пꙋ́ть къ па́пкѣ проекта:', S.project?.root || '');
    if (!p) return;
    const r = await api('/api/open-project', { root: p });
    if (!r.ok) return alert(r.error);
    S.tabs.slice().forEach(t => { t.dirty = false; closeTab(t); });
    S.expanded.clear(); loadProject();
  },
  refreshTree: () => loadProject(),
  save: () => save(),
  saveAll: async () => { for (const t of S.tabs) if (t.dirty) await save(t); },
  closeTab: () => closeTab(),
  quotes: () => S.active && insertText(S.active.ta, '«»'),
  comment: () => S.active && onKey({ key: '/', ctrlKey: true, preventDefault() {} }, S.active),
  toggleKbd: () => $('#slavkbd').classList.toggle('hidden'),
  find: () => findBar(),
  findNext: () => findNext(),
  toggleIcons: () => $('#iconostasis').classList.toggle('hidden'),
  zoomIn: () => zoom(1), zoomOut: () => zoom(-1),
  toggleFont: () => {
    const r = document.documentElement.style;
    r.setProperty('--font-slav', r.getPropertyValue('--font-slav').includes('Segoe') ? '' : '"Segoe UI", "Times New Roman", serif');
    S.tabs.forEach(render);
  },
  showC: async () => {
    const t = S.active;
    if (!t) return;
    const r = await api('/api/c', { content: t.ta.value, name: t.name });
    if (!r.ok) { r.diags.forEach(d => outDiag(d, t.name)); return; }
    const i = r.c.indexOf('/* ============== конецъ рантайма');
    const body = i >= 0 ? '/* … рантаймъ опущенъ (compiler/runtime.c) … */\n' + r.c.slice(r.c.indexOf('\n', i) + 1) : r.c;
    const old = S.tabs.find(x => x.path === '§c:' + t.path);
    if (old) { old.dirty = false; closeTab(old); }
    openFile('§c:' + t.path, { content: body, name: t.name.replace(/\.азъ$/, '') + '.c', readonly: true });
  },
  check: async () => { if (S.active) { await liveCheck(S.active); $('#bottom').classList.remove('hidden');
    if (S.active.diags.length) S.active.diags.forEach(d => outDiag(d, S.active.name)); else out('\nПрегрѣше́нїй не ѡ҆брѣте́но. А҆ми́нь.\n', 'o-ok'); } },
  run: () => runAction('run'), build: () => runAction('build'), console: () => runAction('console'),
  clearOut: () => $('#output').innerHTML = '',
  pray: () => modal(PRAYER),
  toggleProject: () => { $('#project').classList.toggle('hidden'); $('#split-left').style.display = $('#project').classList.contains('hidden') ? 'none' : ''; },
  toggleBottom: () => $('#bottom').classList.toggle('hidden'),
  docs: async () => modal(md(await (await fetch('/docs/синтаксисъ.md')).text())),
  saints: () => modal('<h1>Покрови́тели пи́шꙋщихъ и҆ программи́рꙋющихъ</h1>' + SAINTS.map(saintCard).join('<hr>')),
  about: () => modal(`<h1 style="text-align:center">☦ Скрипторїй</h1><p style="text-align:center">Среда̀ разрабо́тки для ꙗ҆зы́ка <b>А҆́зъ</b>, версія ${esc(LANG.version || '')}.<br>
    Трансля́торъ: А҆́зъ → C → .exe (MSVC / gcc / clang).<br>Шрифтъ «Пономарь» — Slavonic Computing Initiative (SIL OFL).<br><br>
    <i>«Въ нача́лѣ бѣ̀ сло́во»</i> (І҆ѡ҃ а҃, а҃).</p>`),
  closeModal: () => $('#modal').hidden = true,
};
function zoom(d) {
  const r = document.documentElement;
  const cur = parseFloat(getComputedStyle(r).getPropertyValue('--code-size')) || 19;
  const n = Math.max(12, Math.min(40, cur + d * 2));
  r.style.setProperty('--code-size', n + 'px');
  r.style.setProperty('--line-h', Math.round(n * 1.37) + 'px');
}

/* ---------- меню ---------- */
const MENUS = [
  ['Фа́йлъ', [['Но́вый фа́йлъ…', 'newFile', 'Alt+Ins'], ['Нова́ѧ па́пка…', 'newDir'], ['Ѿве́рзти проектъ…', 'openProject'], '-',
    ['Сохрани́ти', 'save', 'Ctrl+S'], ['Сохрани́ти всѧ̑', 'saveAll', 'Ctrl+Shift+S'], '-', ['Затвори́ти вкла́дкꙋ', 'closeTab', 'Ctrl+F4']]],
  ['Реда́кторъ', [['Вста́вити «»', 'quotes'], ['Закомменти́ровати', 'comment', 'Ctrl+/'], '-', ['Сла́вѧнскаѧ клавїатꙋ́ра', 'toggleKbd']]],
  ['По́искъ', [['Найтѝ…', 'find', 'Ctrl+F'], ['Найтѝ да́лѣе', 'findNext', 'F3']]],
  ['Взо́ръ', [['І҆коноста́съ', 'toggleIcons'], ['Шрифтъ кꙋ́пнѣе', 'zoomIn', 'Ctrl+='], ['Шрифтъ мѣ́льче', 'zoomOut', 'Ctrl+-'], ['Гражда́нскїй / церко́вный шрифтъ', 'toggleFont']]],
  ['Ко́дъ', [['Показа́ти C-ко́дъ', 'showC'], ['Провѣ́рити', 'check']]],
  ['Пꙋ́скъ', [['Пꙋсти́ти', 'run', 'Shift+F10'], ['Собра́ти .exe', 'build', 'Ctrl+F9'], ['Пꙋсти́ти въ консо́ли', 'console', 'Ctrl+Shift+F10']]],
  ['Нала́дка', [['Исповѣ́дати прегрѣше́нїѧ', 'check'], ['Помоли́тисѧ предъ на́ладкою', 'pray']]],
  ['Окно̀', [['Проектъ', 'toggleProject', 'Alt+1'], ['Исхо́дъ', 'toggleBottom', 'Alt+4']]],
  ['По́мощь', [['Сѵнта́ксисъ ꙗ҆зы́ка', 'docs', 'F1'], ['Покрови́тели', 'saints'], ['Ѡ҆ програ́ммѣ', 'about']]],
];
function buildMenus() {
  const bar = $('#menubar'), dd = $('#dropdown');
  let open = null;
  const close = () => { dd.hidden = true; open?.classList.remove('open'); open = null; };
  MENUS.forEach(([title, items]) => {
    const mi = document.createElement('span');
    mi.className = 'mi'; mi.textContent = title;
    const show = () => {
      close(); open = mi; mi.classList.add('open');
      dd.innerHTML = items.map(it => it === '-' ? '<div class="sep"></div>' :
        `<div class="di" data-act="${it[1]}"><span>${it[0]}</span><span class="k">${it[2] || ''}</span></div>`).join('');
      const r = mi.getBoundingClientRect();
      dd.style.left = r.left + 'px'; dd.style.top = r.bottom + 'px'; dd.hidden = false;
    };
    mi.onclick = e => { e.stopPropagation(); open === mi ? close() : show(); };
    mi.onmouseenter = () => { if (open && open !== mi) show(); };
    bar.appendChild(mi);
  });
  document.addEventListener('click', e => {
    const a = e.target.closest('[data-act]');
    if (a) { close(); ACTIONS[a.dataset.act]?.(); return; }
    if (!e.target.closest('#dropdown')) close();
    if (e.target.id === 'modal') ACTIONS.closeModal();
  });
}

/* ---------- славянская клавиатура ---------- */
function buildKbd() {
  const keys = ['ѣ', 'і', 'ї', 'ѵ', 'ѳ', 'ѿ', 'ѡ', 'ѻ', 'ꙋ', 'ѧ', 'ꙗ', 'є', 'ѕ', 'ѯ', 'ѱ', 'ъ', '҂',
    ['\u0483', 'ти́тло'], ['\u0301', 'ѻ҆́ксїа'], ['\u0300', 'варі́а'], ['\u0311', 'камо́ра'], ['\u0486', 'звательце'], '«', '»', '☦'];
  const k = $('#slavkbd');
  k.innerHTML = '<span class="lbl">Бꙋ́квы:</span>';
  keys.forEach(x => {
    const [ch, tip] = Array.isArray(x) ? x : [x, x];
    const b = document.createElement('button');
    b.textContent = ch.length === 1 && /\p{M}/u.test(ch) ? '◌' + ch : ch;
    b.title = tip;
    b.onmousedown = e => e.preventDefault();
    b.onclick = () => S.active && !S.active.readonly && insertText(S.active.ta, ch);
    k.appendChild(b);
  });
}

/* ---------- иконостасъ ---------- */
async function buildIcons() {
  try { S.photos = new Set((await api('/api/icons')).have); } catch { S.photos = new Set(); }
  $('#iconostasis').innerHTML = SAINTS.map(s => iconTile(s)).join('');
  $('#iconostasis').onclick = e => {
    const el = e.target.closest('[data-saint]');
    if (el) modal(saintCard(SAINTS.find(s => s.id === el.dataset.saint)));
  };
}

/* ---------- раздѣлители ---------- */
function splitters() {
  const drag = (el, fn) => el.addEventListener('mousedown', e => {
    e.preventDefault();
    const mv = ev => fn(ev), up = () => { removeEventListener('mousemove', mv); removeEventListener('mouseup', up); };
    addEventListener('mousemove', mv); addEventListener('mouseup', up);
  });
  drag($('#split-left'), e => $('#project').style.width = Math.max(150, e.clientX - 24) + 'px');
  drag($('#split-bottom'), e => $('#bottom').style.height = Math.max(60, innerHeight - e.clientY - 24) + 'px');
}

/* ---------- клавиши ---------- */
document.addEventListener('keydown', e => {
  const k = e.key, c = e.ctrlKey, s = e.shiftKey;
  let act = null;
  if (c && !s && (k === 's' || k === 'ы')) act = 'save';
  else if (c && s && (k === 'S' || k === 'Ы')) act = 'saveAll';
  else if (c && !s && k === 'F9') act = 'build';
  else if (s && !c && k === 'F10') act = 'run';
  else if (c && s && k === 'F10') act = 'console';
  else if (c && (k === 'f' || k === 'а')) act = 'find';
  else if (k === 'F3') act = 'findNext';
  else if (k === 'F1') act = 'docs';
  else if (c && k === 'F4') act = 'closeTab';
  else if (c && (k === '=' || k === '+')) act = 'zoomIn';
  else if (c && k === '-') act = 'zoomOut';
  else if (e.altKey && k === '1') act = 'toggleProject';
  else if (e.altKey && k === '4') act = 'toggleBottom';
  else if (k === 'Escape' && !$('#modal').hidden) act = 'closeModal';
  if (act) { e.preventDefault(); ACTIONS[act](); }
});
addEventListener('beforeunload', e => { if (S.tabs.some(t => t.dirty)) { e.preventDefault(); e.returnValue = ''; } });

/* ---------- начало ---------- */
(async function init() {
  buildMenus(); buildKbd(); splitters(); buildIcons();
  try {
    LANG = await api('/api/lang');
    TYPES = new Set(LANG.types); BUILTINS = new Set(LANG.builtins);
    $('#st-lang').textContent = 'А҆́зъ ' + LANG.version;
  } catch (e) { status('Се́рверъ не ѿвѣща́етъ: ' + e.message, true); }
  await loadProject();
  const first = ['ядро/вѣды.азъ'].find(p => JSON.stringify(S.project.tree).includes(p.split('/').pop()));
  if (first) openFile(first).catch(() => {});
  document.fonts?.ready.then(() => S.tabs.forEach(render));
})();
