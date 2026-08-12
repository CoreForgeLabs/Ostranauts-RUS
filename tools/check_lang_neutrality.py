#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_lang_neutrality.py — автопроверка глобального ограничения C2 плана
(docs/superpowers/plans/2026-08-13-i18n-architecture-v2.md, Task 5.6):

    "Никакой код не смеет содержать русскую строку, русское имя падежа или
    ветку if (lang == "ru"). Всё — данные языкового пакета."

Идея: код (plugin/**/*.cs, core/**/*.cs) должен быть языково-нейтральным.
Любая русская строка, русское название чего-либо или ветвление по коду языка
"ru"/"Russian" в самом коде — нарушение; такие вещи обязаны жить в
langs/<code>/*.json.

Что делает скрипт:
  1. Обходит plugin/**/*.cs и core/**/*.cs (относительно корня репозитория,
     который вычисляется как родитель каталога tools/, где лежит сам скрипт —
     то есть корень того worktree/чекаута, из которого скрипт запущен).
     Из обхода намеренно исключены (см. EXCLUDED_DIR_NAMES и комментарии
     ниже, у каждого исключения — своя явная причина, ни одно не добавлено
     "чтобы находки исчезли"):
       - core/OstraI18n.Core.Tests/  — тестовый проект, не входит в
         поставляемую DLL, никогда не выполняется в игре (Task 5.6,
         controller decision после первого прогона гейта).
       - **/obj/, **/bin/            — сгенерированные build-артефакты
         (AssemblyInfo.cs и т.п.), не исходный код, и так в .gitignore.
  2. В каждом файле вырезает //-комментарии и /* ... */-блочные комментарии
     лёгким посимвольным конечным автоматом, который различает состояния
     "код", "обычная строка "..."", "verbatim/interpolated-строка @"..."/
     $"..."/$@"...", "символьный литерал '...'", "//-комментарий" и
     "/*...*/-комментарий" — специально чтобы "//" или "/*" ВНУТРИ строкового
     литерала не считались началом комментария (наивный regex на это ловится).
     Комментарии заменяются пробелами (кроме переводов строк — они сохраняются,
     чтобы номера строк в отчёте не съезжали); всё остальное, включая
     содержимое строковых литералов, остаётся как есть.
  3. В том, что осталось (код + строки, без комментариев) ищет:
     a) Любой кириллический символ [А-Яа-яЁё] — в любом месте, включая
        содержимое строковых литералов (зашитая русская строка в коде — это
        ровно то, что запрещает C2).
     b) Точные строковые литералы "Russian" и "ru" (то есть подряд идущие
        символы " R u s s i a n " / " r u " в оставшемся тексте) — за
        исключением ОДНОЙ легитимной строки: дефолтного значения в вызове
        Config.Bind в plugin/OstraI18n/Plugin.cs (значение конфига по
        умолчанию — не код на языке, а параметр). Исключение применяется
        построчно, по СТРИППНУТОЙ (без комментариев) версии строки: если
        она содержит "Config.Bind", найденный на ЭТОЙ строке литерал
        "Russian"/"ru" не репортится. Специально НЕ исключается весь файл —
        иначе реальное нарушение, добавленное позже в этом же файле, стало
        бы невидимым. (Проверка именно по стриппнутой строке, а не по
        исходной — важно: см. "Диагностический лог" ниже и позитивный тест
        в отчёте задачи, который поймал баг именно на этом месте.)
  4. Отдельно от находок считает и печатает ДИАГНОСТИЧЕСКИЕ ИСКЛЮЧЕНИЯ —
     см. секцию "Диагностический лог" ниже. Это не находки, а явно
     подсвеченные, поимённо перечисленные и посчитанные пропуски.
  5. Печатает каждую находку в формате file:line: text.
  6. Возвращает код выхода 1, если находок (после исключений) хотя бы одна,
     0 если чисто — это и есть "гейт: 0 находок" из плана.

Про Ё/ё: включены в класс кириллицы сознательно, а не по умолчанию из плана
(план пишет [А-Яа-я]). Ё — легитимная отдельная русская буква; строка,
составленная так, что содержит только "ё"/"Ё" и не содержит других
кириллических букв в позиции, где сработал бы [А-Яа-я], была бы ложным
негативом, если Ё не включить. Расширение класса строго консервативнее
(находит не меньше, чем требует план), поэтому не противоречит требованию.

Internal-tooling-text exception (Task 5.6, второй И третий раунд —
controller decisions):
  Единый принцип (дословно из решения контроллера, раунд 3): текст,
  который НИКОГДА не доходит до игрока как переводимый игровой контент и
  НЕ является ветвлением языковой логики — то есть текст, обращённый к
  разработчику/мейнтейнеру/мод-конфигу, а не к игроку — не является тем,
  что C2 призвано исключать. Раунд 2 начал с одной категории (лог-вызовы),
  раунд 3 обобщил её на весь этот класс. Четыре конкретные категории,
  каждая реализована своим, явно задокументированным механизмом (см. ниже
  функции): НЕ единый "если где-то на экране есть кириллица — пропускаем".

  1. Лог-вызовы: строковые литералы — АРГУМЕНТ вызова Plugin.Log.LogInfo/
     LogWarning/LogError, голого Log.LogInfo/LogWarning/LogError (внутри
     класса Plugin, где Log — тот же статический ManualLogSource) или
     log.LogInfo/LogWarning (в VersionGuard, где log — параметр того же
     типа ManualLogSource).
  2. Диагностические коллекции: строковые литералы — аргумент
     Errors.Add(...) (PackLoader.Errors / аналогичные накопители, которые
     позже кто-то залогирует — тот же принцип, что и (1), просто на один
     уровень косвенности дальше от самого вызова Log.LogX).
  (1) и (2) реализованы ОДИНАКОВО, через INTERNAL_TOOLING_CALL_RE +
  find_call_argument_spans(): скрипт находит вызов по регэкспу, затем
  ищет символ ')', реально закрывающий именно ЭТОТ вызов (с учётом
  вложенных скобок и того, что скобки/кавычки ВНУТРИ строковых литералов
  не считаются), и считает диапазон [открывающая '(' ; закрывающая ')']
  "зоной исключения". Находка (кириллица/lang-literal) считается
  исключением, только если ВСЕ её вхождения на этой строке лежат внутри
  такой зоны — если на той же строке есть кириллица ВНЕ такого вызова
  (несвязанное нарушение), строка по-прежнему репортится как находка, а
  не тихо проглатывается целиком.

  3. QA/debug-only отчётность: методы, которые ЦЕЛИКОМ существуют только
     для написания диагностических файлов (не игровых строк) — сейчас
     единственный пример: LocalizedText.CheckOverflow (пишет TSV-строку
     overflow_report.tsv с полями "ширина"/"высота" для QA, а не то, что
     видит игрок). В этом случае русский текст не является аргументом
     ОДНОГО вызова — он сначала собирается в локальную переменную (`var
     line = ... + "ширина " + ...`), и только потом эта переменная
     передаётся в File.AppendAllText. Как следствие, exempt-механизм (1)/
     (2) (call-argument span) физически не может его поймать — литерал
     находится не в аргументах вызова, а в отдельном присваивании до
     него. Поэтому для этой категории используется ДРУГОЙ, более широкий,
     но по-прежнему явный и узкий механизм: METHOD_BODY_WHITELIST — явный
     список (относительный путь к файлу, имя метода), проверенный вручную
     и специально не автоматический ("любой метод с кириллицей" НЕ считается
     — только явно перечисленные). Для каждой записи скрипт находит границы
     ТЕЛА этого метода (по фигурным скобкам, тем же посимвольным подходом,
     что и для скобок вызова) и исключает всё, что внутри. НАМЕРЕННО не
     распространяется на весь файл/класс — только на конкретный
     перечисленный метод.
  4. Config.Bind description-текст (мод-конфиг UI, BepInEx config-меню,
     не игровой контент): кириллица на строке, содержащей "Config.Bind" —
     тот же построчный механизм, что уже использовался в раунде 1 для
     литералов "Russian"/"ru" на такой строке (см. RU_LITERAL_RE-секцию
     ниже), теперь применяется и к кириллице. Построчно, а не по всему
     файлу/вызову: в этой кодовой базе каждый вызов Config.Bind занимает
     ровно одну строку, так что построчная и argument-span-версии сейчас
     дают идентичный результат; если это когда-нибудь перестанет быть так
     (многострочный Config.Bind, или ДРУГОЕ, несвязанное нарушение на той
     же строке) — граница станет менее точной, это осознанное упрощение,
     а не недосмотр.

Запуск: python check_lang_neutrality.py
"""
import io
import os
import re
import sys

CYRILLIC_RE = re.compile(u"[А-Яа-яЁё]")  # А-Яа-яЁё
RU_LITERAL_RE = re.compile(r'"(Russian|ru)"')

# Internal-tooling-text call sites (categories 1+2 from the docstring above):
# developer-facing log calls, plus diagnostic-collection sinks like
# PackLoader.Errors. Receiver must be a fresh identifier ("MyLog.LogInfo" or
# "MyErrors.Add" must NOT match) — the negative lookbehind enforces that.
INTERNAL_TOOLING_CALL_RE = re.compile(
    r'(?<![A-Za-z0-9_.])(?:'
    r'(?:Plugin\.)?[Ll]og\.Log(?:Info|Warning|Error)'  # dev-facing log calls
    r'|Errors\.Add'                                     # diagnostic-collection sink
    r')\s*\('
)

# Category 3 from the docstring above: methods that exist ONLY to write
# QA/debug-only diagnostic output (never game-facing translated text), where
# the Cyrillic literal isn't a direct call argument (it's built up in a local
# variable first) so the call-argument-span mechanism above can't reach it.
# Deliberately an explicit, manually-reviewed whitelist of (relative file
# path using "/", method name) pairs -- NOT "any method containing Cyrillic",
# which would defeat the checker. Add an entry here only when a genuinely new
# QA-only diagnostic method needs it, with the same scrutiny as any other C2
# exception.
METHOD_BODY_WHITELIST = {
    ("plugin/OstraI18n/LocalizedText.cs", "CheckOverflow"),
}

WALK_GLOBS = ("plugin", "core")

# Directory (base)names skipped entirely while walking. Each has its own
# documented reason above/below — not a blanket "reduce noise" mechanism.
EXCLUDED_DIR_NAMES = {"obj", "bin"}
# Relative-path (POSIX-style, repo-root-relative) prefixes skipped entirely.
EXCLUDED_PATH_PREFIXES = ("core/OstraI18n.Core.Tests/",)


def repo_root():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def find_cs_files(root):
    files = []
    for top in WALK_GLOBS:
        base = os.path.join(root, top)
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in EXCLUDED_DIR_NAMES]
            for fn in filenames:
                if not fn.endswith(".cs"):
                    continue
                full = os.path.join(dirpath, fn)
                rel_posix = os.path.relpath(full, root).replace(os.sep, "/")
                if any(rel_posix.startswith(p) for p in EXCLUDED_PATH_PREFIXES):
                    continue
                files.append(full)
    files.sort()
    return files


def strip_comments(text):
    """Заменяет содержимое //- и /*...*/-комментариев пробелами, оставляя
    переводы строк на месте (для точного номера строки) и не трогая код и
    строковые/символьные литералы. Возвращает строку той же длины/раскладки
    по строкам, что и text."""
    n = len(text)
    out = list(text)
    i = 0
    state = "code"  # code | string | verbatim_string | char | line_comment | block_comment
    while i < n:
        c = text[i]
        if state == "code":
            if c == "/" and i + 1 < n and text[i + 1] == "/":
                out[i] = " "
                state = "line_comment"
                i += 1
                continue
            if c == "/" and i + 1 < n and text[i + 1] == "*":
                out[i] = " "
                state = "block_comment"
                i += 1
                continue
            if c == '"':
                # verbatim/interpolated prefixes (@, $, $@, @$) sit directly
                # before the opening quote in the untouched source
                j = i - 1
                is_verbatim = False
                while j >= 0 and text[j] in "@$":
                    if text[j] == "@":
                        is_verbatim = True
                    j -= 1
                state = "verbatim_string" if is_verbatim else "string"
                i += 1
                continue
            if c == "'":
                state = "char"
                i += 1
                continue
            i += 1
            continue
        if state == "line_comment":
            if c == "\n":
                state = "code"
                i += 1
                continue
            out[i] = " "
            i += 1
            continue
        if state == "block_comment":
            if c == "*" and i + 1 < n and text[i + 1] == "/":
                out[i] = " "
                out[i + 1] = " "
                state = "code"
                i += 2
                continue
            if c != "\n":
                out[i] = " "
            i += 1
            continue
        if state == "string":
            if c == "\\" and i + 1 < n:
                i += 2
                continue
            if c == '"':
                state = "code"
            i += 1
            continue
        if state == "verbatim_string":
            if c == '"':
                if i + 1 < n and text[i + 1] == '"':
                    i += 2
                    continue
                state = "code"
                i += 1
                continue
            i += 1
            continue
        if state == "char":
            if c == "\\" and i + 1 < n:
                i += 2
                continue
            if c == "'":
                state = "code"
            i += 1
            continue
    return "".join(out)


def _find_matching_close_paren(text, open_idx):
    """text[open_idx] must be '('. Returns the index of the ')' that closes
    it, tracking nested parens and skipping over parens that appear inside
    string/char literals (so a ')' inside a logged string doesn't end the
    call early). Falls back to end-of-text if unterminated (shouldn't happen
    on valid, already-comment-stripped C#)."""
    n = len(text)
    depth = 0
    i = open_idx
    state = "code"  # code | string | verbatim_string | char
    while i < n:
        c = text[i]
        if state == "code":
            if c == "(":
                depth += 1
            elif c == ")":
                depth -= 1
                if depth == 0:
                    return i
            elif c == '"':
                j = i - 1
                is_verbatim = False
                while j >= 0 and text[j] in "@$":
                    if text[j] == "@":
                        is_verbatim = True
                    j -= 1
                state = "verbatim_string" if is_verbatim else "string"
            elif c == "'":
                state = "char"
            i += 1
            continue
        if state == "string":
            if c == "\\" and i + 1 < n:
                i += 2
                continue
            if c == '"':
                state = "code"
            i += 1
            continue
        if state == "verbatim_string":
            if c == '"':
                if i + 1 < n and text[i + 1] == '"':
                    i += 2
                    continue
                state = "code"
            i += 1
            continue
        if state == "char":
            if c == "\\" and i + 1 < n:
                i += 2
                continue
            if c == "'":
                state = "code"
            i += 1
            continue
    return n - 1


def _find_matching_close_brace(text, open_idx):
    """Same idea as _find_matching_close_paren but for '{'/'}' -- used to
    find a method body's extent for METHOD_BODY_WHITELIST."""
    n = len(text)
    depth = 0
    i = open_idx
    state = "code"
    while i < n:
        c = text[i]
        if state == "code":
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
                if depth == 0:
                    return i
            elif c == '"':
                j = i - 1
                is_verbatim = False
                while j >= 0 and text[j] in "@$":
                    if text[j] == "@":
                        is_verbatim = True
                    j -= 1
                state = "verbatim_string" if is_verbatim else "string"
            elif c == "'":
                state = "char"
            i += 1
            continue
        if state == "string":
            if c == "\\" and i + 1 < n:
                i += 2
                continue
            if c == '"':
                state = "code"
            i += 1
            continue
        if state == "verbatim_string":
            if c == '"':
                if i + 1 < n and text[i + 1] == '"':
                    i += 2
                    continue
                state = "code"
            i += 1
            continue
        if state == "char":
            if c == "\\" and i + 1 < n:
                i += 2
                continue
            if c == "'":
                state = "code"
            i += 1
            continue
    return n - 1


def find_call_argument_spans(stripped_text):
    """Returns a list of (open_paren_idx, close_paren_idx) character-offset
    spans covering the argument list of every INTERNAL_TOOLING_CALL_RE match
    (Plugin.Log.Log*/Log.Log*/log.Log*/Errors.Add) found in the
    (comment-stripped) file text."""
    spans = []
    for m in INTERNAL_TOOLING_CALL_RE.finditer(stripped_text):
        open_idx = m.end() - 1
        assert stripped_text[open_idx] == "("
        close_idx = _find_matching_close_paren(stripped_text, open_idx)
        spans.append((open_idx, close_idx))
    return spans


def find_whitelisted_method_body_spans(rel_posix_path, stripped_text):
    """Returns a list of (open_brace_idx, close_brace_idx) spans for every
    method body in METHOD_BODY_WHITELIST that matches this file."""
    spans = []
    for file_suffix, method_name in METHOD_BODY_WHITELIST:
        if rel_posix_path != file_suffix:
            continue
        for m in re.finditer(r'(?<![A-Za-z0-9_])' + re.escape(method_name) + r'\s*\(', stripped_text):
            paren_open = m.end() - 1
            paren_close = _find_matching_close_paren(stripped_text, paren_open)
            brace_open = stripped_text.find("{", paren_close + 1)
            if brace_open == -1:
                continue  # declaration only (interface/abstract) -- no body to exempt
            brace_close = _find_matching_close_brace(stripped_text, brace_open)
            spans.append((brace_open, brace_close))
    return spans


def _in_any_span(offset, spans):
    return any(lo <= offset <= hi for lo, hi in spans)


def check_file(path, rel_posix_path):
    """Возвращает (findings, exceptions) — оба списки (line_no, text)."""
    findings = []
    exceptions = []
    raw = io.open(path, encoding="utf-8-sig").read()
    stripped = strip_comments(raw)
    tooling_spans = find_call_argument_spans(stripped) + find_whitelisted_method_body_spans(rel_posix_path, stripped)

    stripped_lines = stripped.split("\n")
    line_offset = 0
    for idx, line in enumerate(stripped_lines):
        line_no = idx + 1
        is_config_bind_line = "Config.Bind" in line  # category 4: mod-config UI text, see docstring

        cyr_matches = list(CYRILLIC_RE.finditer(line))
        if cyr_matches:
            offsets = [line_offset + m.start() for m in cyr_matches]
            snippet = line.strip()
            if is_config_bind_line or all(_in_any_span(o, tooling_spans) for o in offsets):
                exceptions.append((line_no, "cyrillic (internal-tooling text): " + snippet))
            else:
                # At least one Cyrillic char on this line sits outside any
                # exempted span -> real finding, not swallowed just because
                # the line ALSO happens to contain an exempted call.
                findings.append((line_no, "cyrillic: " + snippet))

        for m in RU_LITERAL_RE.finditer(line):
            if is_config_bind_line:
                exceptions.append((line_no, "lang-literal (Config.Bind default value): " + line.strip()))
                continue  # legitimate config default value, C2's explicit exclusion
            offset = line_offset + m.start()
            if _in_any_span(offset, tooling_spans):
                exceptions.append((line_no, "lang-literal (internal-tooling text): " + line.strip()))
            else:
                findings.append((line_no, "lang-literal: " + line.strip()))

        line_offset += len(line) + 1

    return findings, exceptions


def main():
    root = repo_root()
    files = find_cs_files(root)
    total_findings = 0
    total_exceptions = 0
    for path in files:
        rel = os.path.relpath(path, root)
        rel_posix = rel.replace(os.sep, "/")
        findings, exceptions = check_file(path, rel_posix)
        for line_no, text in findings:
            print("%s:%d: %s" % (rel, line_no, text))
            total_findings += 1
        for line_no, text in exceptions:
            print("%s:%d: EXEMPT(internal-tooling) %s" % (rel, line_no, text))
            total_exceptions += 1

    if total_findings == 0:
        print("OK: 0 findings across %d files (plugin/**/*.cs, core/**/*.cs, "
              "core/OstraI18n.Core.Tests/ and build artifacts excluded); "
              "%d internal-tooling-text exception(s) applied" % (len(files), total_exceptions))
        return 0
    else:
        print("FAIL: %d finding(s) across %d files (%d internal-tooling-text exception(s) applied)"
              % (total_findings, len(files), total_exceptions))
        return 1


if __name__ == "__main__":
    sys.exit(main())
