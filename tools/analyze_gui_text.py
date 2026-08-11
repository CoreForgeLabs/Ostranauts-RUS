# Анализ покрытия GUI-текста: что идёт через GetString (покрыто модом strings.json)
# vs что захардкожено в C# коде (пробел — не покрывается ни модом, ни грамматическим патчем).
import json, io, os, re, collections

DEC = r"F:\DEV2\ostra_i18n\decompiled"
SRC = r"F:\DEV2\ostra_i18n\lang_src\strings.en.json"

# значения, которые ТОЧНО в текстовых файлах (покрыты модом)
src_vals = set()
src_keys = set()
try:
    j = json.loads(io.open(SRC, encoding="utf-8").read())
    src_keys = set(j.keys())
    src_vals = set(v.strip() for v in j.values())
except Exception as e:
    print("src load fail:", e)

# паттерны установки текста в коде
re_text_assign = re.compile(r'\.text\s*=\s*"((?:[^"\\]|\\.)*)"')
re_settext = re.compile(r'\.SetText\(\s*"((?:[^"\\]|\\.)*)"')
re_getstring = re.compile(r'GetString\(\s*"([A-Za-z0-9_]+)"')
# английское слово/фраза, отображаемое (буквы, разумная длина, без токенов/разметки)
def looks_like_ui(s):
    if not s or len(s) < 2 or len(s) > 60: return False
    if not re.search(r'[A-Za-z]', s): return False
    if re.search(r'[\[\]{}<>]|^GUI_|^[A-Z0-9_]+$|\.png|\.jpg|\.cs$|http', s): return False
    if s.strip() != s and len(s.strip()) < 2: return False
    return True

getstring_keys = collections.Counter()
hardcoded = collections.Counter()
hardcoded_files = collections.defaultdict(set)

for root, dirs, files in os.walk(DEC):
    for fn in files:
        if not fn.endswith(".cs"): continue
        path = os.path.join(root, fn)
        try:
            txt = io.open(path, encoding="utf-8", errors="replace").read()
        except Exception:
            continue
        for m in re_getstring.finditer(txt):
            getstring_keys[m.group(1)] += 1
        for rx in (re_text_assign, re_settext):
            for m in rx.finditer(txt):
                lit = m.group(1)
                if looks_like_ui(lit):
                    hardcoded[lit] += 1
                    hardcoded_files[lit].add(fn)

print("=== GetString(...) вызовов (данные из strings.json, ПОКРЫТО модом) ===")
print("уникальных ключей:", len(getstring_keys), "| всего вызовов:", sum(getstring_keys.values()))
covered = sum(1 for k in getstring_keys if k in src_keys)
print("из них ключи есть в strings.json:", covered)
print()
print("=== захардкоженный UI-текст в коде (.text=/SetText) — ПРОБЕЛ ===")
print("уникальных литералов:", len(hardcoded))
# разбивка: есть ли литерал в strings.json (как значение)
in_src = [l for l in hardcoded if l.strip() in src_vals]
not_src = [l for l in hardcoded if l.strip() not in src_vals]
print("из них уже есть в strings.json (как значение):", len(in_src))
print("НЕТ в strings.json (реальный пробел):", len(not_src))
print()
# выгружаем пробелы для пайплайна перевода (ключ=значение=English source)
out = {lit: lit for lit in not_src}
io.open(r"F:\DEV2\ostra_i18n\lang_src\gui_hardcoded.en.json", "w", encoding="utf-8").write(json.dumps(out, ensure_ascii=False, indent=2))
print("GAP literals dumped -> lang_src/gui_hardcoded.en.json:", len(out))
# GetString ключи, которых НЕТ в strings.json (вернут UNKNOWN_STRING — тоже пробел)
missing_keys = {k: getstring_keys[k] for k in getstring_keys if k not in src_keys}
io.open(r"F:\DEV2\ostra_i18n\lang_src\gui_missing_keys.en.json", "w", encoding="utf-8").write(json.dumps({k: k for k in missing_keys}, ensure_ascii=False, indent=2))
print("GetString keys missing from strings.json:", len(missing_keys), "->", list(missing_keys)[:12])
print()
print("--- топ захардкоженного, чего НЕТ в strings.json ---")
for lit in sorted(not_src, key=lambda l: -hardcoded[l])[:45]:
    files = ", ".join(sorted(hardcoded_files[lit])[:2])
    print("  %-42s x%-3d [%s]" % (repr(lit[:40]), hardcoded[lit], files))
