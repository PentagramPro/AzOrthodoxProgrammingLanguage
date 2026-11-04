/* ============================================================
 *  Рантаймъ языка «Азъ».  Сей файлъ вклеивается въ каждую
 *  сгенерированную программу.  Значенія динамическія, память
 *  не освобождается (аки манна — на всѣхъ хватитъ).
 * ============================================================ */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <math.h>
#include <time.h>
#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#endif

typedef enum { AZ_NONE_T = 0, AZ_BOOL_T, AZ_INT_T, AZ_FLT_T, AZ_STR_T, AZ_LIST_T, AZ_DICT_T } AzTag;
typedef struct AzStr { size_t len; char *s; } AzStr;
typedef struct AzList AzList;
typedef struct AzDict AzDict;
typedef struct Val { AzTag t; union { long long i; double f; AzStr *s; AzList *l; AzDict *d; } u; } Val;
struct AzList { size_t n, cap; Val *v; };
struct AzDict { size_t n, cap; Val *k; Val *v; };

static int az_line = 0;

static void az_die(const char *fmt, ...) {
    va_list ap;
    fflush(stdout);
    fprintf(stderr, "\nАнаѳема! Строка %d: ", az_line);
    va_start(ap, fmt); vfprintf(stderr, fmt, ap); va_end(ap);
    fputc('\n', stderr);
    exit(1);
}

static void *az_alloc(size_t n) {
    void *p = calloc(1, n ? n : 1);
    if (!p) { fputs("Гладъ памяти!\n", stderr); exit(3); }
    return p;
}
static void *az_realloc(void *p, size_t n) {
    p = realloc(p, n ? n : 1);
    if (!p) { fputs("Гладъ памяти!\n", stderr); exit(3); }
    return p;
}

/* ---------- построеніе значеній ---------- */
static Val az_none(void) { Val v; v.t = AZ_NONE_T; v.u.i = 0; return v; }
static Val az_bool(int b) { Val v; v.t = AZ_BOOL_T; v.u.i = b ? 1 : 0; return v; }
static Val az_int(long long i) { Val v; v.t = AZ_INT_T; v.u.i = i; return v; }
static Val az_flt(double f) { Val v; v.t = AZ_FLT_T; v.u.f = f; return v; }
static Val az_strn(const char *s, size_t n) {
    Val v; AzStr *x = (AzStr *)az_alloc(sizeof *x);
    x->s = (char *)az_alloc(n + 1); memcpy(x->s, s, n); x->s[n] = 0; x->len = n;
    v.t = AZ_STR_T; v.u.s = x; return v;
}
static Val az_str(const char *s) { return az_strn(s, strlen(s)); }
static Val az_list_new(void) { Val v; v.t = AZ_LIST_T; v.u.l = (AzList *)az_alloc(sizeof(AzList)); return v; }
static Val az_dict_new(void) { Val v; v.t = AZ_DICT_T; v.u.d = (AzDict *)az_alloc(sizeof(AzDict)); return v; }

static void az_list_push(AzList *l, Val x) {
    if (l->n == l->cap) { l->cap = l->cap ? l->cap * 2 : 8; l->v = (Val *)az_realloc(l->v, l->cap * sizeof(Val)); }
    l->v[l->n++] = x;
}
static Val az_list_lit(int n, ...) {
    Val r = az_list_new(); va_list ap; int i;
    va_start(ap, n); for (i = 0; i < n; i++) az_list_push(r.u.l, va_arg(ap, Val)); va_end(ap);
    return r;
}

static const char *az_typename(Val v) {
    switch (v.t) {
    case AZ_NONE_T: return "безвѣстно";
    case AZ_BOOL_T: return "истинность";
    case AZ_INT_T: return "цѣлое";
    case AZ_FLT_T: return "дробное";
    case AZ_STR_T: return "слово";
    case AZ_LIST_T: return "свитокъ";
    case AZ_DICT_T: return "словникъ";
    }
    return "?";
}

/* ---------- UTF-8: «буква» = знакъ вмѣстѣ съ надстрочными (ударенія, титла) ---------- */
static int az_is_mark(unsigned cp) {
    return (cp >= 0x300 && cp <= 0x36F) || (cp >= 0x483 && cp <= 0x489) || (cp >= 0x1DC0 && cp <= 0x1DFF) ||
           (cp >= 0x2DE0 && cp <= 0x2DFF) || (cp >= 0xA66F && cp <= 0xA67D) || (cp >= 0xFE20 && cp <= 0xFE2F) ||
           cp == 0xA69E || cp == 0xA69F;
}
static unsigned az_u8_cp(const unsigned char *s, size_t n, size_t i) {
    unsigned c = s[i];
    if (c < 0x80 || i + 1 >= n) return c;
    if ((c & 0xE0) == 0xC0) return ((c & 0x1F) << 6) | (s[i + 1] & 0x3F);
    if ((c & 0xF0) == 0xE0 && i + 2 < n) return ((c & 0x0F) << 12) | ((s[i + 1] & 0x3F) << 6) | (s[i + 2] & 0x3F);
    if (i + 3 < n) return ((c & 0x07) << 18) | ((s[i + 1] & 0x3F) << 12) | ((s[i + 2] & 0x3F) << 6) | (s[i + 3] & 0x3F);
    return c;
}
static int az_u8_start(const char *s, size_t n, size_t i) {
    if (((unsigned char)s[i] & 0xC0) == 0x80) return 0;
    return i == 0 || !az_is_mark(az_u8_cp((const unsigned char *)s, n, i));
}
static size_t az_u8_count(const char *s, size_t n) {
    size_t i, c = 0;
    for (i = 0; i < n; i++) if (az_u8_start(s, n, i)) c++;
    return c;
}
/* байтовое смѣщеніе idx-ой (ѿ нуля) буквы */
static size_t az_u8_off(const char *s, size_t n, size_t idx) {
    size_t i = 0, c = 0;
    while (i < n) {
        if (az_u8_start(s, n, i)) { if (c == idx) return i; c++; }
        i++;
    }
    return n;
}

/* ---------- строитель строкъ ---------- */
typedef struct { char *s; size_t n, cap; } AzBuf;
static void az_buf_put(AzBuf *b, const char *s, size_t n) {
    if (b->n + n + 1 > b->cap) { b->cap = (b->n + n + 1) * 2; b->s = (char *)az_realloc(b->s, b->cap); }
    memcpy(b->s + b->n, s, n); b->n += n; b->s[b->n] = 0;
}
static void az_buf_puts(AzBuf *b, const char *s) { az_buf_put(b, s, strlen(s)); }

static int az_dict_find(AzDict *d, Val k);
static void az_repr(AzBuf *b, Val v, int quote) {
    char tmp[64]; size_t i;
    switch (v.t) {
    case AZ_NONE_T: az_buf_puts(b, "безвѣстно"); break;
    case AZ_BOOL_T: az_buf_puts(b, v.u.i ? "истина" : "ложь"); break;
    case AZ_INT_T: sprintf(tmp, "%lld", v.u.i); az_buf_puts(b, tmp); break;
    case AZ_FLT_T: sprintf(tmp, "%.15g", v.u.f); az_buf_puts(b, tmp); break;
    case AZ_STR_T:
        if (quote) az_buf_puts(b, "«");
        az_buf_put(b, v.u.s->s, v.u.s->len);
        if (quote) az_buf_puts(b, "»");
        break;
    case AZ_LIST_T:
        az_buf_puts(b, "[");
        for (i = 0; i < v.u.l->n; i++) { if (i) az_buf_puts(b, ", "); az_repr(b, v.u.l->v[i], 1); }
        az_buf_puts(b, "]");
        break;
    case AZ_DICT_T:
        az_buf_puts(b, "{");
        for (i = 0; i < v.u.d->n; i++) {
            if (i) az_buf_puts(b, ", ");
            az_repr(b, v.u.d->k[i], 1); az_buf_puts(b, ": "); az_repr(b, v.u.d->v[i], 1);
        }
        az_buf_puts(b, "}");
        break;
    }
}
static Val az_tostr(Val v) {
    AzBuf b = {0, 0, 0};
    if (v.t == AZ_STR_T) return v;
    az_repr(&b, v, 0);
    return az_strn(b.s ? b.s : "", b.n);
}

/* ---------- истинность и равенство ---------- */
static int az_truthy(Val v) {
    switch (v.t) {
    case AZ_NONE_T: return 0;
    case AZ_BOOL_T: case AZ_INT_T: return v.u.i != 0;
    case AZ_FLT_T: return v.u.f != 0.0;
    case AZ_STR_T: return v.u.s->len > 0;
    case AZ_LIST_T: return v.u.l->n > 0;
    case AZ_DICT_T: return v.u.d->n > 0;
    }
    return 0;
}
static int az_isnum(Val v) { return v.t == AZ_INT_T || v.t == AZ_FLT_T || v.t == AZ_BOOL_T; }
static double az_num(Val v) {
    if (v.t == AZ_FLT_T) return v.u.f;
    if (v.t == AZ_INT_T || v.t == AZ_BOOL_T) return (double)v.u.i;
    az_die("ожидалось число, а дано %s", az_typename(v));
    return 0;
}
static int az_eq(Val a, Val b) {
    size_t i;
    if (az_isnum(a) && az_isnum(b)) {
        if (a.t != AZ_FLT_T && b.t != AZ_FLT_T) return a.u.i == b.u.i;
        return az_num(a) == az_num(b);
    }
    if (a.t != b.t) return 0;
    switch (a.t) {
    case AZ_NONE_T: return 1;
    case AZ_STR_T: return a.u.s->len == b.u.s->len && memcmp(a.u.s->s, b.u.s->s, a.u.s->len) == 0;
    case AZ_LIST_T:
        if (a.u.l->n != b.u.l->n) return 0;
        for (i = 0; i < a.u.l->n; i++) if (!az_eq(a.u.l->v[i], b.u.l->v[i])) return 0;
        return 1;
    case AZ_DICT_T: return a.u.d == b.u.d;
    default: return 0;
    }
}
static Val az_op_eq(Val a, Val b) { return az_bool(az_eq(a, b)); }
static Val az_op_ne(Val a, Val b) { return az_bool(!az_eq(a, b)); }
static int az_less(Val a, Val b) {
    if (az_isnum(a) && az_isnum(b)) {
        if (a.t != AZ_FLT_T && b.t != AZ_FLT_T) return a.u.i < b.u.i;
        return az_num(a) < az_num(b);
    }
    if (a.t == AZ_STR_T && b.t == AZ_STR_T) {
        size_t n = a.u.s->len < b.u.s->len ? a.u.s->len : b.u.s->len;
        int c = memcmp(a.u.s->s, b.u.s->s, n);
        return c < 0 || (c == 0 && a.u.s->len < b.u.s->len);
    }
    az_die("не можно сравнити %s съ %s", az_typename(a), az_typename(b));
    return 0;
}
static Val az_op_lt(Val a, Val b) { return az_bool(az_less(a, b)); }
static Val az_op_gt(Val a, Val b) { return az_bool(az_less(b, a)); }
static Val az_op_le(Val a, Val b) { return az_bool(!az_less(b, a)); }
static Val az_op_ge(Val a, Val b) { return az_bool(!az_less(a, b)); }
static Val az_op_not(Val a) { return az_bool(!az_truthy(a)); }

/* ---------- аріѳметика ---------- */
static Val az_add(Val a, Val b) {
    if (a.t == AZ_INT_T && b.t == AZ_INT_T) return az_int(a.u.i + b.u.i);
    if (a.t == AZ_STR_T || b.t == AZ_STR_T) {
        AzBuf buf = {0, 0, 0}; Val sa = az_tostr(a), sb = az_tostr(b);
        az_buf_put(&buf, sa.u.s->s, sa.u.s->len); az_buf_put(&buf, sb.u.s->s, sb.u.s->len);
        return az_strn(buf.s, buf.n);
    }
    if (a.t == AZ_LIST_T && b.t == AZ_LIST_T) {
        Val r = az_list_new(); size_t i;
        for (i = 0; i < a.u.l->n; i++) az_list_push(r.u.l, a.u.l->v[i]);
        for (i = 0; i < b.u.l->n; i++) az_list_push(r.u.l, b.u.l->v[i]);
        return r;
    }
    if (az_isnum(a) && az_isnum(b)) {
        if (a.t != AZ_FLT_T && b.t != AZ_FLT_T) return az_int(a.u.i + b.u.i);
        return az_flt(az_num(a) + az_num(b));
    }
    az_die("не можно сложити %s и %s", az_typename(a), az_typename(b));
    return az_none();
}
static Val az_sub(Val a, Val b) {
    if (a.t != AZ_FLT_T && b.t != AZ_FLT_T && az_isnum(a) && az_isnum(b)) return az_int(a.u.i - b.u.i);
    return az_flt(az_num(a) - az_num(b));
}
static Val az_mul(Val a, Val b) {
    if (a.t == AZ_STR_T && (b.t == AZ_INT_T)) {
        AzBuf buf = {0, 0, 0}; long long i;
        for (i = 0; i < b.u.i; i++) az_buf_put(&buf, a.u.s->s, a.u.s->len);
        return az_strn(buf.s ? buf.s : "", buf.n);
    }
    if (a.t != AZ_FLT_T && b.t != AZ_FLT_T && az_isnum(a) && az_isnum(b)) return az_int(a.u.i * b.u.i);
    return az_flt(az_num(a) * az_num(b));
}
static Val az_div(Val a, Val b) {
    double y = az_num(b);
    if (y == 0) az_die("дѣленіе на нуль, егоже несть въ кириллической цыфири");
    if (a.t != AZ_FLT_T && b.t != AZ_FLT_T && a.u.i % b.u.i == 0) return az_int(a.u.i / b.u.i);
    return az_flt(az_num(a) / y);
}
static Val az_idiv(Val a, Val b) {
    if (az_num(b) == 0) az_die("дѣленіе на нуль, егоже несть въ кириллической цыфири");
    if (a.t != AZ_FLT_T && b.t != AZ_FLT_T) {
        long long q = a.u.i / b.u.i;
        if ((a.u.i % b.u.i != 0) && ((a.u.i < 0) != (b.u.i < 0))) q--;
        return az_int(q);
    }
    return az_flt(floor(az_num(a) / az_num(b)));
}
static Val az_mod(Val a, Val b) {
    if (az_num(b) == 0) az_die("дѣленіе на нуль, егоже несть въ кириллической цыфири");
    if (a.t != AZ_FLT_T && b.t != AZ_FLT_T) {
        long long m = a.u.i % b.u.i;
        if (m != 0 && ((m < 0) != (b.u.i < 0))) m += b.u.i;
        return az_int(m);
    }
    return az_flt(fmod(az_num(a), az_num(b)));
}
static Val az_neg(Val a) {
    if (a.t == AZ_INT_T || a.t == AZ_BOOL_T) return az_int(-a.u.i);
    return az_flt(-az_num(a));
}

/* ---------- словникъ ---------- */
static int az_dict_find(AzDict *d, Val k) {
    size_t i;
    for (i = 0; i < d->n; i++) if (az_eq(d->k[i], k)) return (int)i;
    return -1;
}
static void az_dict_set(AzDict *d, Val k, Val v) {
    int i = az_dict_find(d, k);
    if (i >= 0) { d->v[i] = v; return; }
    if (d->n == d->cap) {
        d->cap = d->cap ? d->cap * 2 : 8;
        d->k = (Val *)az_realloc(d->k, d->cap * sizeof(Val));
        d->v = (Val *)az_realloc(d->v, d->cap * sizeof(Val));
    }
    d->k[d->n] = k; d->v[d->n] = v; d->n++;
}

/* ---------- указаніе (ѿ) ---------- */
static long long az_need_int(Val k, const char *what) {
    if (k.t == AZ_INT_T || k.t == AZ_BOOL_T) return k.u.i;
    if (k.t == AZ_FLT_T && k.u.f == floor(k.u.f)) return (long long)k.u.f;
    az_die("%s долженъ быти цѣлымъ, а дано %s", what, az_typename(k));
    return 0;
}
static Val az_index(Val a, Val k) {
    long long i;
    if (a.t == AZ_DICT_T) { int j = az_dict_find(a.u.d, k); return j < 0 ? az_none() : a.u.d->v[j]; }
    if (a.t == AZ_LIST_T) {
        i = az_need_int(k, "указатель");
        if (i < 1 || (size_t)i > a.u.l->n)
            az_die("указатель %lld внѣ свитка (длина %zu). Помни: счётъ ѿ единицы, ибо нуля несть", i, a.u.l->n);
        return a.u.l->v[i - 1];
    }
    if (a.t == AZ_STR_T) {
        size_t n = az_u8_count(a.u.s->s, a.u.s->len), s, e;
        i = az_need_int(k, "указатель");
        if (i < 1 || (size_t)i > n) az_die("указатель %lld внѣ слова (длина %zu)", i, n);
        s = az_u8_off(a.u.s->s, a.u.s->len, (size_t)(i - 1));
        e = az_u8_off(a.u.s->s, a.u.s->len, (size_t)i);
        return az_strn(a.u.s->s + s, e - s);
    }
    az_die("не можно взяти ѿ %s", az_typename(a));
    return az_none();
}
static void az_setindex(Val a, Val k, Val v) {
    long long i;
    if (a.t == AZ_DICT_T) { az_dict_set(a.u.d, k, v); return; }
    if (a.t == AZ_LIST_T) {
        i = az_need_int(k, "указатель");
        if ((size_t)i == a.u.l->n + 1) { az_list_push(a.u.l, v); return; }
        if (i < 1 || (size_t)i > a.u.l->n)
            az_die("указатель %lld внѣ свитка (длина %zu). Помни: счётъ ѿ единицы, ибо нуля несть", i, a.u.l->n);
        a.u.l->v[i - 1] = v; return;
    }
    az_die("не можно положити въ %s", az_typename(a));
}

/* ---------- обходъ (для … во …) ---------- */
static Val az_iter(Val a) {
    Val r; size_t i;
    switch (a.t) {
    case AZ_LIST_T: return a;
    case AZ_DICT_T:
        r = az_list_new();
        for (i = 0; i < a.u.d->n; i++) az_list_push(r.u.l, a.u.d->k[i]);
        return r;
    case AZ_STR_T: {
        size_t n = az_u8_count(a.u.s->s, a.u.s->len);
        r = az_list_new();
        for (i = 0; i < n; i++) az_list_push(r.u.l, az_index(a, az_int((long long)i + 1)));
        return r;
    }
    case AZ_INT_T: {
        long long j; r = az_list_new();
        for (j = 1; j <= a.u.i; j++) az_list_push(r.u.l, az_int(j));
        return r;
    }
    default: az_die("не можно обходити %s", az_typename(a));
    }
    return az_none();
}

/* ---------- глаголи ---------- */
static int az_print_first = 1;
static void az_print_item(Val v) {
    Val s = az_tostr(v);
    if (!az_print_first) fputc(' ', stdout);
    fwrite(s.u.s->s, 1, s.u.s->len, stdout);
    az_print_first = 0;
}
static void az_print_end(void) { fputc('\n', stdout); fflush(stdout); az_print_first = 1; }

static void az_anathema(Val msg) {
    Val s = az_tostr(msg);
    fflush(stdout);
    fprintf(stderr, "\nАнаѳема! Строка %d: %s\n", az_line, s.u.s->s);
    exit(1);
}
static void az_exit_with(Val v) {
    fflush(stdout);
    exit(v.t == AZ_INT_T ? (int)v.u.i : (az_truthy(v) || v.t == AZ_NONE_T ? 0 : 1));
}

/* ---------- кириллическая цыфирь ---------- */
static Val az_b_cyr(Val n) {
    static const char *ones[] = {"", "а", "в", "г", "д", "є", "ѕ", "з", "и", "ѳ"};
    static const char *tens[] = {"", "і", "к", "л", "м", "н", "ѯ", "ѻ", "п", "ч"};
    static const char *hund[] = {"", "р", "с", "т", "ѵ", "ф", "х", "ѱ", "ѿ", "ц"};
    const char *parts[16]; int np = 0, i, last; long long x = az_need_int(n, "число");
    AzBuf b = {0, 0, 0};
    if (x <= 0) return az_tostr(n); /* нуля и отрицанія въ цыфири несть */
    if (x >= 1000) {
        long long th = x / 1000; x %= 1000;
        if (th >= 10) { Val t = az_b_cyr(az_int(th)); az_buf_puts(&b, "҂"); az_buf_puts(&b, t.u.s->s); }
        else { az_buf_puts(&b, "҂"); az_buf_puts(&b, ones[th]); }
    }
    if (x / 100) parts[np++] = hund[x / 100];
    x %= 100;
    if (x > 10 && x < 20) { parts[np++] = ones[x % 10]; parts[np++] = "і"; }
    else { if (x / 10) parts[np++] = tens[x / 10]; if (x % 10) parts[np++] = ones[x % 10]; }
    last = np >= 2 ? np - 2 : np - 1; /* титло надъ предпослѣднею буквою */
    for (i = 0; i < np; i++) { az_buf_puts(&b, parts[i]); if (i == last) az_buf_puts(&b, "҃"); }
    if (np == 0) az_buf_puts(&b, "҃");
    return az_strn(b.s, b.n);
}

/* ---------- встроенныя дѣла ---------- */
static Val az_b_len(Val x) {
    if (x.t == AZ_STR_T) return az_int((long long)az_u8_count(x.u.s->s, x.u.s->len));
    if (x.t == AZ_LIST_T) return az_int((long long)x.u.l->n);
    if (x.t == AZ_DICT_T) return az_int((long long)x.u.d->n);
    az_die("у %s нѣсть длины", az_typename(x));
    return az_none();
}
static Val az_b_push(Val l, Val x) {
    if (l.t != AZ_LIST_T) az_die("приложити можно токмо ко свитку, а дано %s", az_typename(l));
    az_list_push(l.u.l, x); return az_none();
}
static Val az_b_pop(Val l) {
    if (l.t != AZ_LIST_T) az_die("изъяти можно токмо изъ свитка");
    if (l.u.l->n == 0) az_die("свитокъ пустъ");
    return l.u.l->v[--l.u.l->n];
}
static Val az_b_keys(Val d) {
    if (d.t != AZ_DICT_T) az_die("ключи бываютъ токмо у словника");
    return az_iter(d);
}
static Val az_b_has(Val c, Val x) {
    size_t i;
    if (c.t == AZ_DICT_T) return az_bool(az_dict_find(c.u.d, x) >= 0);
    if (c.t == AZ_LIST_T) { for (i = 0; i < c.u.l->n; i++) if (az_eq(c.u.l->v[i], x)) return az_bool(1); return az_bool(0); }
    if (c.t == AZ_STR_T && x.t == AZ_STR_T) {
        if (x.u.s->len == 0) return az_bool(1);
        for (i = 0; i + x.u.s->len <= c.u.s->len; i++) if (memcmp(c.u.s->s + i, x.u.s->s, x.u.s->len) == 0) return az_bool(1);
        return az_bool(0);
    }
    az_die("не можно искати въ %s", az_typename(c));
    return az_none();
}
static Val az_b_range(Val a, Val b) {
    long long s = 1, e, j; Val r = az_list_new();
    if (b.t == AZ_NONE_T) e = az_need_int(a, "конецъ");
    else { s = az_need_int(a, "началo"); e = az_need_int(b, "конецъ"); }
    if (s <= e) for (j = s; j <= e; j++) az_list_push(r.u.l, az_int(j));
    else for (j = s; j >= e; j--) az_list_push(r.u.l, az_int(j));
    return r;
}
static Val az_b_int(Val x) {
    if (x.t == AZ_INT_T) return x;
    if (x.t == AZ_BOOL_T) return az_int(x.u.i);
    if (x.t == AZ_FLT_T) return az_int((long long)x.u.f);
    if (x.t == AZ_STR_T) {
        char *end; long long v = strtoll(x.u.s->s, &end, 10);
        while (*end == ' ' || *end == '\t' || *end == '\r' || *end == '\n') end++;
        if (end == x.u.s->s || *end) az_die("«%s» не есть число", x.u.s->s);
        return az_int(v);
    }
    az_die("не можно %s обратити въ число", az_typename(x));
    return az_none();
}
static Val az_b_flt(Val x) {
    if (x.t == AZ_STR_T) {
        char *end; double v = strtod(x.u.s->s, &end);
        if (end == x.u.s->s) az_die("«%s» не есть число", x.u.s->s);
        return az_flt(v);
    }
    return az_flt(az_num(x));
}
static Val az_b_str(Val x) { return az_tostr(x); }
static Val az_b_type(Val x) { return az_str(az_typename(x)); }
static Val az_b_sqrt(Val x) { return az_flt(sqrt(az_num(x))); }
static Val az_b_abs(Val x) { return x.t == AZ_INT_T ? az_int(x.u.i < 0 ? -x.u.i : x.u.i) : az_flt(fabs(az_num(x))); }
static Val az_b_random(Val a, Val b) {
    long long lo = az_need_int(a, "начало"), hi = az_need_int(b, "конецъ"), r;
    if (hi < lo) { long long t = lo; lo = hi; hi = t; }
    r = ((long long)rand() << 15) ^ rand();
    return az_int(lo + (r % (hi - lo + 1)));
}
static Val az_b_sleep(Val ms) {
    fflush(stdout);
#ifdef _WIN32
    Sleep((DWORD)az_need_int(ms, "время"));
#else
    usleep((useconds_t)az_need_int(ms, "время") * 1000);
#endif
    return az_none();
}
static Val az_b_time(void) { return az_int((long long)clock() * 1000 / CLOCKS_PER_SEC); }
static Val az_b_join(Val l, Val sep) {
    AzBuf b = {0, 0, 0}; size_t i; Val s;
    if (l.t != AZ_LIST_T) az_die("совокупити можно токмо свитокъ");
    for (i = 0; i < l.u.l->n; i++) {
        if (i && sep.t != AZ_NONE_T) { s = az_tostr(sep); az_buf_put(&b, s.u.s->s, s.u.s->len); }
        s = az_tostr(l.u.l->v[i]); az_buf_put(&b, s.u.s->s, s.u.s->len);
    }
    return az_strn(b.s ? b.s : "", b.n);
}
static Val az_b_split(Val str, Val sep) {
    Val r = az_list_new(); const char *s, *p, *e;
    if (str.t != AZ_STR_T) az_die("раздѣлити можно токмо слово");
    s = str.u.s->s; e = s + str.u.s->len;
    if (sep.t == AZ_NONE_T || (sep.t == AZ_STR_T && sep.u.s->len == 0)) {
        while (s < e) {
            while (s < e && (*s == ' ' || *s == '\t' || *s == '\n' || *s == '\r')) s++;
            p = s; while (p < e && !(*p == ' ' || *p == '\t' || *p == '\n' || *p == '\r')) p++;
            if (p > s) az_list_push(r.u.l, az_strn(s, (size_t)(p - s)));
            s = p;
        }
        return r;
    }
    if (sep.t != AZ_STR_T) az_die("раздѣлитель долженъ быти словомъ");
    for (;;) {
        p = s;
        while (p + sep.u.s->len <= e && memcmp(p, sep.u.s->s, sep.u.s->len) != 0) p++;
        if (p + sep.u.s->len > e) { az_list_push(r.u.l, az_strn(s, (size_t)(e - s))); break; }
        az_list_push(r.u.l, az_strn(s, (size_t)(p - s)));
        s = p + sep.u.s->len;
    }
    return r;
}
static Val az_b_input(Val prompt) {
    AzBuf b = {0, 0, 0};
    if (prompt.t != AZ_NONE_T) { Val s = az_tostr(prompt); fwrite(s.u.s->s, 1, s.u.s->len, stdout); }
    fflush(stdout);
#ifdef _WIN32
    {
        HANDLE h = GetStdHandle(STD_INPUT_HANDLE); DWORD mode;
        if (GetConsoleMode(h, &mode)) {
            wchar_t wb[4096]; DWORD got = 0; char ub[16384]; int n;
            if (!ReadConsoleW(h, wb, 4095, &got, NULL)) return az_str("");
            while (got > 0 && (wb[got - 1] == L'\n' || wb[got - 1] == L'\r')) got--;
            n = WideCharToMultiByte(CP_UTF8, 0, wb, (int)got, ub, sizeof ub, NULL, NULL);
            return az_strn(ub, (size_t)(n > 0 ? n : 0));
        }
    }
#endif
    {
        int c;
        while ((c = fgetc(stdin)) != EOF && c != '\n') { char ch = (char)c; az_buf_put(&b, &ch, 1); }
        while (b.n > 0 && b.s[b.n - 1] == '\r') b.s[--b.n] = 0;
        return az_strn(b.s ? b.s : "", b.n);
    }
}

static void az_init(void) {
#ifdef _WIN32
    SetConsoleOutputCP(65001);
    SetConsoleCP(65001);
#endif
    srand((unsigned)time(NULL));
}
/* ============== конецъ рантайма ============== */
