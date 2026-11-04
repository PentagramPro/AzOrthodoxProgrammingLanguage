#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Азъ — православный языкъ программированія.
Трансляторъ: исходникъ .азъ  →  C  →  .exe (чрезъ MSVC cl / gcc / clang / tcc).

    az собери  файлъ.азъ [-o имя.exe]   — собрать исполнимый файлъ
    az пусти   файлъ.азъ [доводы...]    — собрать и пустить
    az си      файлъ.азъ                — показать порожденный C-кодъ
"""
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unicodedata
from pathlib import Path

HERE = Path(__file__).resolve().parent
VERSION = "1.0"


class AzError(Exception):
    def __init__(self, msg, line=0, col=0):
        super().__init__(msg)
        self.msg, self.line, self.col = msg, line, col


# ════════════════════════════ Нормализація словъ ════════════════════════════
LETTER_MAP = {
    'ѣ': 'е', 'є': 'е', 'ѥ': 'е', 'ё': 'е', 'і': 'и', 'ї': 'и', 'ѵ': 'и', 'ѷ': 'и', 'ꙇ': 'и', 'й': 'и',
    'ѳ': 'ф', 'ѿ': 'от', 'ѡ': 'о', 'ѻ': 'о', 'ꙍ': 'о', 'ѽ': 'о', 'ꚙ': 'о', 'ꙩ': 'о', 'ꙫ': 'о', 'ꙭ': 'о',
    'ꙋ': 'у', 'ѹ': 'у', 'ѫ': 'у', 'ѭ': 'ю', 'ѕ': 'з', 'ꙃ': 'з', 'ꙁ': 'з', 'ꙗ': 'я', 'ѧ': 'я', 'ѩ': 'я',
    'ѯ': 'кс', 'ѱ': 'пс', 'ꙑ': 'ы', 'ꙟ': 'я',
}


def norm(word: str) -> str:
    """Приводитъ слово къ простому виду: безъ ударѣній, титлъ и древнихъ буквъ."""
    w = unicodedata.normalize('NFD', word).casefold()
    w = ''.join(ch for ch in w if not unicodedata.category(ch).startswith('M') and ch != '\u0482')
    has_cyr = any('\u0400' <= ch <= '\u052f' or '\ua640' <= ch <= '\ua69f' for ch in w)
    out = []
    for ch in w:
        if has_cyr and ch == 'i':
            ch = 'и'
        out.append(LETTER_MAP.get(ch, ch))
    w = ''.join(out).replace('оу', 'у')
    if len(w) > 1 and w.endswith('ъ'):
        w = w[:-1]
    return w


# ═══════════════════════════ Кириллическая цыфирь ═══════════════════════════
TITLO = '\u0483'
THOUSANDS = '\u0482'
NUMERALS = {
    'а': 1, 'в': 2, 'г': 3, 'д': 4, 'є': 5, 'е': 5, 'ѕ': 6, 'з': 7, 'ꙁ': 7, 'и': 8, 'ѳ': 9,
    'і': 10, 'i': 10, 'к': 20, 'л': 30, 'м': 40, 'н': 50, 'ѯ': 60, 'ѻ': 70, 'о': 70, 'п': 80, 'ч': 90, 'ҁ': 90,
    'р': 100, 'с': 200, 'т': 300, 'у': 400, 'ѵ': 400, 'ꙋ': 400, 'ф': 500, 'х': 600, 'ѱ': 700, 'ѿ': 800,
    'ѡ': 800, 'ц': 900,
}


def cyr_numeral(word: str):
    if TITLO not in word:
        return None
    total, mult, seen = 0, 1, False
    for ch in unicodedata.normalize('NFC', word).lower():
        if ch == THOUSANDS:
            mult = 1000
            continue
        if unicodedata.category(ch).startswith('M'):
            continue
        if ch not in NUMERALS:
            return None
        total += NUMERALS[ch] * mult
        mult, seen = 1, True
    return total if seen else None


# ═════════════════════════════ Ключевыя словеса ═════════════════════════════
KW = {}
for kind, words in {
    'def': 'функция деяние дело',
    'return': 'возврати возвратити верни',
    'if': 'аще',
    'else': 'иначе инако',
    'while': 'покуда доколе',
    'for': 'для всяко всяк',
    'in': 'во',
    'break': 'прерви довольно',
    'continue': 'продолжи паки',
    'print': 'глаголи рцы рци',
    'is': 'есть',
    'assign': 'бысть буди',
    'decl_many': 'суть',
    'decl_one': 'же',
    'not': 'не',
    'and': 'и',
    'or': 'або или либо',
    'gt': 'вяще вящше больше более болше',
    'lt': 'меньше менее мнее менше',
    'true': 'истина правда аминь',
    'false': 'ложь кривда',
    'none': 'безвестно безвестна безвестный безвестен безвестное ничтоже',
    'index': 'от',
    'anathema': 'анафема',
}.items():
    for w in words.split():
        KW[norm(w)] = kind

TYPES = {}
for t, words in {
    'int': 'целое целыя целый целые целая целых цело',
    'flt': 'дробное дробныя дробный дробные дробь',
    'str': 'слово словеса строка строки глагол',
    'dict': 'словник словники словарь',
    'list': 'свиток свитки список списки',
    'bool': 'истинность',
}.items():
    for w in words.split():
        TYPES[norm(w)] = t

# имя → (C-функція, мин. доводовъ, макс. доводовъ)
BUILTINS = {}
for names, cfn, lo, hi in [
    ('длина мера сочти сочтати', 'az_b_len', 1, 1),
    ('приложи', 'az_b_push', 2, 2),
    ('изъми изыми', 'az_b_pop', 1, 1),
    ('ключи', 'az_b_keys', 1, 1),
    ('имать содержит', 'az_b_has', 2, 2),
    ('чреда', 'az_b_range', 1, 2),
    ('число', 'az_b_int', 1, 1),
    ('дробь', 'az_b_flt', 1, 1),
    ('слово', 'az_b_str', 1, 1),
    ('вид', 'az_b_type', 1, 1),
    ('корень', 'az_b_sqrt', 1, 1),
    ('мерило', 'az_b_abs', 1, 1),
    ('жребий', 'az_b_random', 2, 2),
    ('почий', 'az_b_sleep', 1, 1),
    ('время', 'az_b_time', 0, 0),
    ('совокупи', 'az_b_join', 1, 2),
    ('раздели', 'az_b_split', 1, 2),
    ('внемли', 'az_b_input', 0, 1),
    ('кириллицею цифирью', 'az_b_cyr', 1, 1),
]:
    for n in names.split():
        BUILTINS[norm(n)] = (cfn, lo, hi)


# ═══════════════════════════════ Лексеръ ═══════════════════════════════
class Tok:
    __slots__ = ('kind', 'val', 'raw', 'line', 'col')

    def __init__(self, kind, val, raw, line, col):
        self.kind, self.val, self.raw, self.line, self.col = kind, val, raw, line, col

    def __repr__(self):
        return f'{self.kind}:{self.val!r}@{self.line}'


OPS = ['//', '==', '!=', '<=', '>=', '+=', '-=', '<', '>', '=', '+', '-', '*', '/', '%',
       '(', ')', '[', ']', '{', '}', ',', ':', '.']


def is_word_char(ch):
    return ch.isalnum() or ch == '_' or ch == THOUSANDS or unicodedata.category(ch).startswith('M')


def lex(src: str):
    src = src.lstrip('\ufeff').replace('\r\n', '\n').replace('\r', '\n').replace('\t', '    ')
    toks, indents, depth = [], [0], 0
    lines = src.split('\n')
    for ln, line in enumerate(lines, 1):
        i, n = 0, len(line)
        if depth == 0:
            stripped = line.strip()
            if not stripped or stripped.startswith('#'):
                continue
            ind = len(line) - len(line.lstrip(' '))
            if ind > indents[-1]:
                indents.append(ind)
                toks.append(Tok('INDENT', None, '', ln, 1))
            while ind < indents[-1]:
                indents.pop()
                toks.append(Tok('DEDENT', None, '', ln, 1))
            if ind != indents[-1]:
                raise AzError('отступъ не согласуется ни съ единымъ прежнимъ', ln, ind + 1)
            i = ind
        while i < n:
            ch = line[i]
            col = i + 1
            if ch == ' ':
                i += 1
                continue
            if ch == '#':
                break
            if ch in '«"':
                close = '»' if ch == '«' else '"'
                j, buf = i + 1, []
                while j < n and line[j] != close:
                    if line[j] == '\\' and j + 1 < n:
                        e = line[j + 1]
                        buf.append({'n': '\n', 't': '\t', '\\': '\\', '"': '"', '»': '»', '«': '«'}.get(e, '\\' + e))
                        j += 2
                        continue
                    buf.append(line[j])
                    j += 1
                if j >= n:
                    raise AzError('слово (строка) не закрыто', ln, col)
                toks.append(Tok('STR', ''.join(buf), line[i:j + 1], ln, col))
                i = j + 1
                continue
            if ch.isdigit() and ch.isascii():
                m = re.match(r'\d+(\.\d+)?', line[i:])
                s = m.group(0)
                toks.append(Tok('NUM', float(s) if '.' in s else int(s), s, ln, col))
                i += len(s)
                continue
            if is_word_char(ch):
                j = i
                while j < n and is_word_char(line[j]):
                    j += 1
                raw = line[i:j]
                num = cyr_numeral(raw)
                if num is not None:
                    toks.append(Tok('NUM', num, raw, ln, col))
                else:
                    w = norm(raw)
                    if w in KW:
                        toks.append(Tok('KW', KW[w], raw, ln, col))
                    else:
                        toks.append(Tok('NAME', w, raw, ln, col))
                i = j
                continue
            for op in OPS:
                if line.startswith(op, i):
                    if op in '([{':
                        depth += 1
                    elif op in ')]}':
                        depth = max(0, depth - 1)
                    toks.append(Tok('OP', op, op, ln, col))
                    i += len(op)
                    break
            else:
                raise AzError(f'невѣдомый знакъ «{ch}»', ln, col)
        if depth == 0 and toks and toks[-1].kind not in ('NEWLINE', 'INDENT', 'DEDENT'):
            toks.append(Tok('NEWLINE', None, '', ln, n + 1))
    last = len(lines)
    if not toks or toks[-1].kind != 'NEWLINE':
        toks.append(Tok('NEWLINE', None, '', last, 1))
    while len(indents) > 1:
        indents.pop()
        toks.append(Tok('DEDENT', None, '', last, 1))
    toks.append(Tok('EOF', None, '', last + 1, 1))
    return toks


# ═══════════════════════════════ Парсеръ ═══════════════════════════════
class Node:
    def __init__(self, kind, line, **kw):
        self.kind, self.line = kind, line
        self.__dict__.update(kw)


class Parser:
    def __init__(self, toks):
        self.t, self.p = toks, 0

    # --- служебное ---
    @property
    def cur(self):
        return self.t[self.p]

    def peek(self, k=1):
        return self.t[min(self.p + k, len(self.t) - 1)]

    def at(self, kind, val=None, tok=None):
        tok = tok or self.cur
        return tok.kind == kind and (val is None or tok.val == val)

    def eat(self, kind, val=None, what=None):
        if not self.at(kind, val):
            want = what or (f'«{val}»' if val else kind)
            got = self.cur.raw or {'NEWLINE': 'конецъ строки', 'EOF': 'конецъ файла',
                                   'INDENT': 'отступъ', 'DEDENT': 'конецъ отступа'}.get(self.cur.kind, self.cur.kind)
            raise AzError(f'ожидалось {want}, а обрѣтено «{got}»', self.cur.line, self.cur.col)
        tok = self.cur
        self.p += 1
        return tok

    def accept(self, kind, val=None):
        if self.at(kind, val):
            return self.eat(kind, val)
        return None

    # --- программа ---
    def program(self):
        body = []
        while not self.at('EOF'):
            body.append(self.statement())
        return body

    def block(self):
        self.eat('OP', ':', '«:»')
        if not self.at('NEWLINE'):
            st = self.simple_statement()
            self.eat('NEWLINE', what='конецъ строки')
            return [st]
        self.eat('NEWLINE')
        if not self.at('INDENT'):
            raise AzError('ожидался отступъ (тѣло блока)', self.cur.line, self.cur.col)
        self.eat('INDENT')
        body = []
        while not self.at('DEDENT') and not self.at('EOF'):
            body.append(self.statement())
        self.accept('DEDENT')
        return body

    def statement(self):
        c = self.cur
        if self.at('KW', 'def'):
            return self.funcdef()
        if self.at('KW', 'if'):
            return self.ifstmt()
        if self.at('KW', 'while'):
            self.eat('KW')
            cond = self.expr()
            return Node('while', c.line, cond=cond, body=self.block())
        if self.at('KW', 'for'):
            self.eat('KW')
            var = self.eat('NAME', what='имя')
            self.eat('KW', 'in', '«во»')
            it = self.expr()
            return Node('for', c.line, var=var.val, it=it, body=self.block())
        if self.at('KW', 'else'):
            raise AzError('«иначе» безъ «аще»', c.line, c.col)
        st = self.simple_statement()
        self.eat('NEWLINE', what='конецъ строки')
        return st

    def funcdef(self):
        c = self.eat('KW', 'def')
        name = self.eat('NAME', what='имя функціи')
        self.eat('OP', '(', '«(»')
        params = []
        while not self.at('OP', ')'):
            pn = self.eat('NAME', what='имя довода')
            default = None
            if self.accept('KW', 'is') or self.accept('OP', '='):
                default = self.expr()
                if default.kind not in ('num', 'str', 'bool', 'none') and not (
                        default.kind == 'neg' and default.a.kind == 'num'):
                    raise AzError('по умолчанію допустимы токмо простыя значенія', pn.line, pn.col)
            params.append((pn.val, default))
            if not self.accept('OP', ','):
                break
        self.eat('OP', ')', '«)»')
        return Node('def', c.line, name=name.val, raw=name.raw, params=params, body=self.block())

    def ifstmt(self):
        c = self.eat('KW', 'if')
        branches = [(self.expr(), self.block())]
        orelse = None
        while self.at('KW', 'else'):
            self.eat('KW')
            if self.accept('KW', 'if'):
                branches.append((self.expr(), self.block()))
            else:
                orelse = self.block()
                break
        return Node('if', c.line, branches=branches, orelse=orelse)

    def simple_statement(self):
        c = self.cur
        if self.at('KW', 'return'):
            self.eat('KW')
            val = None if self.at('NEWLINE') else self.expr()
            return Node('return', c.line, val=val)
        if self.at('KW', 'break'):
            self.eat('KW')
            return Node('break', c.line)
        if self.at('KW', 'continue'):
            self.eat('KW')
            return Node('continue', c.line)
        if self.at('KW', 'print'):
            self.eat('KW')
            args = []
            if not self.at('NEWLINE'):
                args.append(self.expr())
                while self.accept('OP', ','):
                    args.append(self.expr())
            return Node('print', c.line, args=args)
        if self.at('KW', 'anathema'):
            self.eat('KW')
            return Node('anathema', c.line, val=self.expr())
        # объявленіе:  а, б, в суть цѣлыя   /   славословъ же словникъ
        if self.at('NAME'):
            k, names = self.p, []
            while self.t[k].kind == 'NAME':
                names.append(self.t[k])
                if self.t[k + 1].kind == 'OP' and self.t[k + 1].val == ',':
                    k += 2
                else:
                    k += 1
                    break
            if self.t[k].kind == 'KW' and self.t[k].val in ('decl_many', 'decl_one'):
                self.p = k + 1
                tt = self.eat('NAME', what='имя вида (цѣлое, слово, словникъ…)')
                if tt.val not in TYPES:
                    raise AzError(f'невѣдомый видъ «{tt.raw}»', tt.line, tt.col)
                return Node('decl', c.line, names=[n.val for n in names], typ=TYPES[tt.val])
        # присвоеніе:  цѣль есть выраженіе
        save = self.p
        target = None
        try:
            target = self.postfix()
        except AzError:
            pass
        if target is not None and target.kind in ('name', 'index') and (
                self.at('KW', 'is') or self.at('KW', 'assign') or self.at('OP', '=')
                or self.at('OP', '+=') or self.at('OP', '-=')):
            op = self.eat(self.cur.kind).val
            val = self.expr()
            if op in ('+=', '-='):
                val = Node('bin', c.line, op='+' if op == '+=' else '-', a=target, b=val)
            return Node('assign', c.line, target=target, val=val)
        self.p = save
        return Node('expr', c.line, val=self.expr())

    # --- выраженія ---
    def expr(self):
        a = self.and_expr()
        while self.at('KW', 'or'):
            t = self.eat('KW')
            a = Node('or', t.line, a=a, b=self.and_expr())
        return a

    def and_expr(self):
        a = self.not_expr()
        while self.at('KW', 'and'):
            t = self.eat('KW')
            a = Node('and', t.line, a=a, b=self.not_expr())
        return a

    def not_expr(self):
        if self.at('KW', 'not'):
            t = self.eat('KW')
            return Node('not', t.line, a=self.not_expr())
        return self.comparison()

    def comparison(self):
        a = self.arith()
        t = self.cur
        # словесное сравненіе:  [не] [есть] [не] [вяще|менѣе]
        nots, has_is, cmp = 0, False, None
        k = self.p
        while True:
            tk = self.t[k]
            if tk.kind == 'KW' and tk.val == 'not':
                nots += 1
            elif tk.kind == 'KW' and tk.val == 'is' and not has_is:
                has_is = True
            elif tk.kind == 'KW' and tk.val in ('gt', 'lt'):
                cmp = tk.val
                k += 1
                break
            else:
                break
            k += 1
        if cmp or has_is:
            self.p = k
            b = self.arith()
            if cmp:
                op = {('gt', 0): '>', ('gt', 1): '<=', ('lt', 0): '<', ('lt', 1): '>='}[(cmp, nots % 2)]
            else:
                op = '!=' if nots % 2 else '=='
            return Node('bin', t.line, op=op, a=a, b=b)
        if nots:
            raise AzError('«не» не на мѣстѣ', t.line, t.col)
        if self.at('OP') and self.cur.val in ('==', '!=', '<', '>', '<=', '>='):
            op = self.eat('OP').val
            return Node('bin', t.line, op=op, a=a, b=self.arith())
        return a

    def arith(self):
        a = self.term()
        while self.at('OP') and self.cur.val in ('+', '-'):
            t = self.eat('OP')
            a = Node('bin', t.line, op=t.val, a=a, b=self.term())
        return a

    def term(self):
        a = self.unary()
        while self.at('OP') and self.cur.val in ('*', '/', '//', '%'):
            t = self.eat('OP')
            a = Node('bin', t.line, op=t.val, a=a, b=self.unary())
        return a

    def unary(self):
        if self.at('OP', '-'):
            t = self.eat('OP')
            return Node('neg', t.line, a=self.unary())
        if self.at('OP', '+'):
            self.eat('OP')
            return self.unary()
        return self.postfix()

    def args(self):
        self.eat('OP', '(')
        args = []
        while not self.at('OP', ')'):
            args.append(self.expr())
            if not self.accept('OP', ','):
                break
        self.eat('OP', ')', '«)»')
        return args

    def postfix(self):
        a = self.primary()
        while True:
            t = self.cur
            if self.at('OP', '(') and a.kind == 'name':
                a = Node('call', t.line, name=a.name, raw=a.raw, args=self.args(), col=a.col)
            elif self.at('OP', '['):
                self.eat('OP')
                k = self.expr()
                self.eat('OP', ']', '«]»')
                a = Node('index', t.line, a=a, k=k)
            elif self.at('KW', 'index'):  # ѿ
                self.eat('KW')
                k = self.primary()
                if self.at('OP', '(') and k.kind == 'name':
                    k = Node('call', k.line, name=k.name, raw=k.raw, args=self.args(), col=k.col)
                a = Node('index', t.line, a=a, k=k)
            elif self.at('OP', '.'):
                self.eat('OP')
                m = self.eat('NAME', what='имя дѣйства')
                a = Node('call', t.line, name=m.val, raw=m.raw, args=[a] + self.args(), col=m.col)
            else:
                return a

    def primary(self):
        t = self.cur
        if self.at('NUM'):
            self.eat('NUM')
            return Node('num', t.line, v=t.val)
        if self.at('STR'):
            self.eat('STR')
            return Node('str', t.line, v=t.val)
        if self.at('KW', 'true') or self.at('KW', 'false'):
            self.eat('KW')
            return Node('bool', t.line, v=t.val == 'true')
        if self.at('KW', 'none'):
            self.eat('KW')
            return Node('none', t.line)
        if self.at('NAME'):
            self.eat('NAME')
            return Node('name', t.line, name=t.val, raw=t.raw, col=t.col)
        if self.at('OP', '('):
            self.eat('OP')
            e = self.expr()
            self.eat('OP', ')', '«)»')
            return e
        if self.at('OP', '['):
            self.eat('OP')
            items = []
            while not self.at('OP', ']'):
                items.append(self.expr())
                if not self.accept('OP', ','):
                    break
            self.eat('OP', ']', '«]»')
            return Node('list', t.line, items=items)
        if self.at('OP', '{'):
            self.eat('OP')
            items = []
            while not self.at('OP', '}'):
                k = self.expr()
                self.eat('OP', ':', '«:»')
                items.append((k, self.expr()))
                if not self.accept('OP', ','):
                    break
            self.eat('OP', '}', '«}»')
            return Node('dict', t.line, items=items)
        got = t.raw or {'NEWLINE': 'конецъ строки', 'EOF': 'конецъ файла'}.get(t.kind, t.kind)
        raise AzError(f'ожидалось выраженіе, а обрѣтено «{got}»', t.line, t.col)


# ═══════════════════════════════ Кодогенъ (C) ═══════════════════════════════
def mangle(name):
    out = []
    for ch in name:
        if ch.isascii() and ch.isalnum():
            out.append(ch)
        elif ch == '_':
            out.append('__')
        else:
            out.append(f'_{ord(ch):x}')
    return ''.join(out)


def c_string(s):
    out = []
    for b in s.encode('utf-8'):
        c = chr(b)
        if c == '"' or c == '\\':
            out.append('\\' + c)
        elif 32 <= b < 127 and c != '?':
            out.append(c)
        else:
            out.append(f'\\{b:03o}')
    return '"' + ''.join(out) + '"'


BINOPS = {'+': 'az_add', '-': 'az_sub', '*': 'az_mul', '/': 'az_div', '//': 'az_idiv', '%': 'az_mod',
          '==': 'az_op_eq', '!=': 'az_op_ne', '<': 'az_op_lt', '>': 'az_op_gt', '<=': 'az_op_le',
          '>=': 'az_op_ge'}


def assigned_names(body, acc):
    for st in body:
        k = st.kind
        if k == 'assign' and st.target.kind == 'name':
            acc.add(st.target.name)
        elif k == 'decl':
            acc.update(st.names)
        elif k == 'for':
            acc.add(st.var)
            assigned_names(st.body, acc)
        elif k == 'while':
            assigned_names(st.body, acc)
        elif k == 'if':
            for _, b in st.branches:
                assigned_names(b, acc)
            if st.orelse:
                assigned_names(st.orelse, acc)
    return acc


class Gen:
    def __init__(self):
        self.funcs = {}
        self.globals = set()
        self.tmp = 0
        self.out = []
        self.scope = None  # (params, locals) или None для главнаго тѣла
        self.loop = 0
        self.in_func = False

    def new_tmp(self):
        self.tmp += 1
        return self.tmp

    def var(self, name, node):
        raw = getattr(node, 'raw', name)
        if self.scope is not None:
            params, locs = self.scope
            if name in params or name in locs:
                return 'v_' + mangle(name)
        if name in self.globals:
            return 'g_' + mangle(name)
        if name in self.funcs or name in BUILTINS:
            raise AzError(f'«{raw}» есть функція — призови её со скобками', node.line, getattr(node, 'col', 0))
        raise AzError(f'имя «{raw}» не вѣдомо (не объявлено и не присвоено)', node.line, getattr(node, 'col', 0))

    def e(self, n):
        k = n.kind
        if k == 'num':
            return f'az_int({n.v}LL)' if isinstance(n.v, int) else f'az_flt({n.v!r})'
        if k == 'str':
            return f'az_strn({c_string(n.v)}, {len(n.v.encode("utf-8"))})'
        if k == 'bool':
            return f'az_bool({int(n.v)})'
        if k == 'none':
            return 'az_none()'
        if k == 'name':
            return self.var(n.name, n)
        if k == 'neg':
            return f'az_neg({self.e(n.a)})'
        if k == 'not':
            return f'az_op_not({self.e(n.a)})'
        if k == 'and':
            return f'az_bool(az_truthy({self.e(n.a)}) && az_truthy({self.e(n.b)}))'
        if k == 'or':
            return f'az_bool(az_truthy({self.e(n.a)}) || az_truthy({self.e(n.b)}))'
        if k == 'bin':
            return f'{BINOPS[n.op]}({self.e(n.a)}, {self.e(n.b)})'
        if k == 'index':
            return f'az_index({self.e(n.a)}, {self.e(n.k)})'
        if k == 'list':
            if not n.items:
                return 'az_list_new()'
            return f'az_list_lit({len(n.items)}, ' + ', '.join(self.e(x) for x in n.items) + ')'
        if k == 'dict':
            t = self.new_tmp()
            parts = [f'az_dict_set(_d{t}.u.d, {self.e(a)}, {self.e(b)})' for a, b in n.items]
            # выраженіе-запятая: создаём словникъ во временной перемѣнной
            self.dict_tmps.append(t)
            return f'(_d{t} = az_dict_new(), ' + ''.join(p + ', ' for p in parts) + f'_d{t})'
        if k == 'call':
            return self.call(n)
        raise AzError(f'внутренняя ошибка: {k}', n.line)

    def call(self, n):
        if n.name in self.funcs:
            fd = self.funcs[n.name]
            params = fd.params
            if len(n.args) > len(params):
                raise AzError(f'функція «{fd.raw}» пріемлетъ не болѣе {len(params)} доводовъ', n.line, n.col)
            args = [self.e(a) for a in n.args]
            for pname, default in params[len(n.args):]:
                if default is None:
                    raise AzError(f'функціи «{fd.raw}» недостаетъ довода «{pname}»', n.line, n.col)
                args.append(self.e(default))
            return f'f_{mangle(n.name)}(' + ', '.join(args) + ')'
        if n.name in BUILTINS:
            cfn, lo, hi = BUILTINS[n.name]
            if not lo <= len(n.args) <= hi:
                raise AzError(f'«{n.raw}» пріемлетъ ѿ {lo} до {hi} доводовъ, а дано {len(n.args)}', n.line, n.col)
            args = [self.e(a) for a in n.args] + ['az_none()'] * (hi - len(n.args))
            return f'{cfn}(' + ', '.join(args) + ')'
        raise AzError(f'функція «{n.raw}» не вѣдома', n.line, n.col)

    # --- предложенія ---
    def emit(self, s, ind):
        self.out.append('    ' * ind + s)

    def stmts(self, body, ind):
        for st in body:
            self.stmt(st, ind)

    def stmt(self, st, ind):
        k = st.kind
        self.emit(f'az_line = {st.line};', ind)
        if k == 'expr':
            self.emit(f'(void){self.e(st.val)};', ind)
        elif k == 'print':
            for a in st.args:
                self.emit(f'az_print_item({self.e(a)});', ind)
            self.emit('az_print_end();', ind)
        elif k == 'assign':
            if st.target.kind == 'name':
                self.emit(f'{self.var(st.target.name, st.target)} = {self.e(st.val)};', ind)
            else:
                self.emit(f'az_setindex({self.e(st.target.a)}, {self.e(st.target.k)}, {self.e(st.val)});', ind)
        elif k == 'decl':
            init = {'int': 'az_int(0)', 'flt': 'az_flt(0.0)', 'str': 'az_str("")', 'dict': 'az_dict_new()',
                    'list': 'az_list_new()', 'bool': 'az_bool(0)'}[st.typ]
            for nm in st.names:
                self.emit(f'{self.var(nm, st)} = {init};', ind)
        elif k == 'if':
            for i, (cond, body) in enumerate(st.branches):
                kw = 'if' if i == 0 else '} else if'
                self.emit(f'{kw} (az_truthy({self.e(cond)})) {{', ind)
                self.stmts(body, ind + 1)
            if st.orelse:
                self.emit('} else {', ind)
                self.stmts(st.orelse, ind + 1)
            self.emit('}', ind)
        elif k == 'while':
            self.emit(f'while (az_truthy({self.e(st.cond)})) {{', ind)
            self.loop += 1
            self.stmts(st.body, ind + 1)
            self.loop -= 1
            self.emit(f'az_line = {st.line};', ind + 1)
            self.emit('}', ind)
        elif k == 'for':
            t = self.new_tmp()
            target = self.var(st.var, st)
            self.emit('{', ind)
            self.emit(f'Val _it{t} = az_iter({self.e(st.it)}); size_t _i{t};', ind + 1)
            self.emit(f'for (_i{t} = 0; _i{t} < _it{t}.u.l->n; _i{t}++) {{', ind + 1)
            self.emit(f'{target} = _it{t}.u.l->v[_i{t}];', ind + 2)
            self.loop += 1
            self.stmts(st.body, ind + 2)
            self.loop -= 1
            self.emit('}', ind + 1)
            self.emit('}', ind)
        elif k in ('break', 'continue'):
            if not self.loop:
                raise AzError(f'«{"прерви" if k == "break" else "продолжи"}» внѣ цикла', st.line)
            self.emit(f'{k};', ind)
        elif k == 'return':
            v = self.e(st.val) if st.val else 'az_none()'
            if self.in_func:
                self.emit(f'return {v};', ind)
            else:
                self.emit(f'az_exit_with({v});', ind)
        elif k == 'anathema':
            self.emit(f'az_anathema({self.e(st.val)});', ind)
        elif k == 'def':
            raise AzError('функцію подобаетъ опредѣлять токмо въ главномъ тѣлѣ', st.line)
        else:
            raise AzError(f'внутренняя ошибка: {k}', st.line)

    def generate(self, prog, src_name):
        for st in prog:
            if st.kind == 'def':
                if st.name in self.funcs:
                    raise AzError(f'функція «{st.raw}» уже опредѣлена', st.line)
                if st.name in BUILTINS:
                    pass  # своя функція заслоняетъ встроенную
                self.funcs[st.name] = st
        main_body = [st for st in prog if st.kind != 'def']
        self.globals = assigned_names(main_body, set())

        runtime = (HERE / 'runtime.c').read_text(encoding='utf-8')
        head = [f'/* Порождено трансляторомъ «Азъ» {VERSION} изъ {src_name}. Не правити рукою. */', runtime, '']
        for g in sorted(self.globals):
            head.append(f'static Val g_{mangle(g)};')
        protos, bodies = [], []
        for name, fd in self.funcs.items():
            sig = f'static Val f_{mangle(name)}(' + (', '.join(f'Val v_{mangle(p)}' for p, _ in fd.params) or 'void') + ')'
            protos.append(sig + ';')
            params = {p for p, _ in fd.params}
            locs = assigned_names(fd.body, set()) - params - self.globals
            self.scope, self.in_func, self.out, self.dict_tmps = (params, locs), True, [], []
            self.stmts(fd.body, 1)
            body = self.out
            pre = [f'    Val v_{mangle(l)} = az_none();' for l in sorted(locs)]
            pre += [f'    Val _d{t};' for t in self.dict_tmps]
            bodies += [sig + ' {'] + pre + body + ['    return az_none();', '}', '']
        self.scope, self.in_func, self.out, self.dict_tmps = None, False, [], []
        self.stmts(main_body, 1)
        main = ['static void az_main_body(void) {'] + [f'    Val _d{t};' for t in self.dict_tmps] + self.out + ['}', '',
                'int main(void) {', '    az_init();', '    az_main_body();', '    fflush(stdout);',
                '    return 0;', '}', '']
        return '\n'.join(head + [''] + protos + [''] + bodies + main)


def compile_to_c(src, src_name='исходникъ'):
    prog = Parser(lex(src)).program()
    return Gen().generate(prog, src_name)


# ═══════════════════════════ Поискъ C-компилятора ═══════════════════════════
def _cache_dir():
    d = Path(os.environ.get('LOCALAPPDATA', tempfile.gettempdir())) / 'Az-lang'
    d.mkdir(parents=True, exist_ok=True)
    return d


def msvc_env():
    """Возвращаетъ окруженіе съ настроеннымъ MSVC (cl.exe) или None."""
    cache = _cache_dir() / 'msvc_env.json'
    if cache.exists():
        try:
            env = json.loads(cache.read_text(encoding='utf-8'))
            if shutil.which('cl', path=env.get('PATH', '')):
                return env
        except Exception:
            pass
    pf86 = os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')
    vswhere = Path(pf86) / 'Microsoft Visual Studio' / 'Installer' / 'vswhere.exe'
    if not vswhere.exists():
        return None
    r = subprocess.run([str(vswhere), '-latest', '-products', '*', '-requires',
                        'Microsoft.VisualStudio.Component.VC.Tools.x86.x64', '-property', 'installationPath'],
                       capture_output=True, text=True)
    inst = r.stdout.strip().splitlines()
    if not inst:
        return None
    vcvars = Path(inst[0]) / 'VC' / 'Auxiliary' / 'Build' / 'vcvars64.bat'
    if not vcvars.exists():
        return None
    r = subprocess.run(f'cmd /u /s /c ""{vcvars}" >nul 2>&1 && set"', capture_output=True, shell=False)
    text = r.stdout.decode('utf-16-le', errors='replace')
    env = {}
    for line in text.splitlines():
        if '=' in line and not line.startswith('='):
            k, v = line.split('=', 1)
            env[k] = v
    if not shutil.which('cl', path=env.get('PATH', '')):
        return None
    cache.write_text(json.dumps(env, ensure_ascii=False), encoding='utf-8')
    return env


def find_cc():
    forced = os.environ.get('AZ_CC')
    if forced:
        return forced, None
    if sys.platform == 'win32':
        if shutil.which('cl'):
            return 'cl', None
        env = msvc_env()
        if env:
            return shutil.which('cl', path=env.get('PATH', '')), env
    for cc in ('gcc', 'clang', 'tcc', 'cc'):
        if shutil.which(cc):
            return cc, None
    raise AzError('не обрѣтенъ ни единъ C-компиляторъ (MSVC, gcc, clang, tcc). '
                  'Поставьте Visual Studio Build Tools или MinGW.')


def build_exe(c_code, out_exe: Path):
    cc, env = find_cc()
    key = hashlib.sha1(c_code.encode('utf-8')).hexdigest()[:12]
    wd = Path(tempfile.gettempdir()) / 'az_build' / key
    wd.mkdir(parents=True, exist_ok=True)
    c_file = wd / 'prog.c'
    c_file.write_text(c_code, encoding='utf-8')
    exe = wd / 'prog.exe'
    name = Path(cc).stem.lower()
    if name in ('cl', 'clang-cl'):
        cmd = [cc, '/nologo', '/O2', '/utf-8', '/W0', '/MT', 'prog.c', '/Fe:prog.exe', '/link', '/SUBSYSTEM:CONSOLE']
    else:
        cmd = [cc, '-O2', '-w', 'prog.c', '-o', 'prog.exe', '-lm']
    r = subprocess.run(cmd, cwd=wd, env=env, capture_output=True)
    log = (r.stdout + r.stderr).decode('cp866' if name == 'cl' else 'utf-8', errors='replace')
    if r.returncode != 0 or not exe.exists():
        raise AzError('C-компиляторъ возроптал:\n' + log)
    out_exe.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(exe, out_exe)
    return cc, log


# ═══════════════════════════════ Командная строка ═══════════════════════════════
CMD_ALIASES = {
    'build': 'build', 'собери': 'build', 'собрати': 'build', 'сбор': 'build',
    'run': 'run', 'пусти': 'run', 'пуск': 'run', 'пустити': 'run',
    'c': 'c', 'си': 'c', 'emit-c': 'c',
    'check': 'check', 'проверь': 'check', 'испытай': 'check',
}


def report(e: AzError, path):
    loc = f'{path}:{e.line}:{e.col}' if e.line else str(path)
    print(f'{loc}: прегрѣшеніе: {e.msg}', file=sys.stderr)


def main(argv):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8')
        except Exception:
            pass
    if not argv or argv[0] in ('-h', '--help', 'помощь', '?'):
        print(__doc__)
        return 0
    if argv[0] in ('-v', '--version'):
        print(f'Азъ {VERSION}')
        return 0
    cmd = CMD_ALIASES.get(argv[0].lower())
    if cmd is None:
        cmd, rest = 'build', argv
    else:
        rest = argv[1:]
    if not rest:
        print('Укажите исходный файлъ .азъ', file=sys.stderr)
        return 2
    src_path = Path(rest[0])
    out = None
    extra = rest[1:]
    if '-o' in extra:
        i = extra.index('-o')
        out = Path(extra[i + 1])
        extra = extra[:i] + extra[i + 2:]
    try:
        src = src_path.read_text(encoding='utf-8')
    except OSError as ex:
        print(f'Не можно прочести {src_path}: {ex}', file=sys.stderr)
        return 2
    try:
        c_code = compile_to_c(src, src_path.name)
        if cmd == 'c':
            sys.stdout.write(c_code)
            return 0
        if cmd == 'check':
            print('Прегрѣшеній не обрѣтено. Аминь.')
            return 0
        if cmd == 'build':
            out = out or src_path.with_suffix('.exe')
            cc, _ = build_exe(c_code, out)
            print(f'Собрано: {out}  (C-компиляторъ: {Path(cc).name}). Слава Богу!')
            return 0
        if cmd == 'run':
            out = out or Path(tempfile.gettempdir()) / 'az_run' / (src_path.stem + '.exe')
            build_exe(c_code, out)
            sys.stdout.flush()
            return subprocess.call([str(out)] + extra)
    except AzError as e:
        report(e, src_path)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
