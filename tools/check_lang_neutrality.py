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

Диагностический лог (Task 5.6, второй раунд — controller decision):
  Строковые литералы, которые являются АРГУМЕНТОМ вызова Plugin.Log.LogInfo/
  LogWarning/LogError, голого Log.LogInfo/LogWarning/LogError (внутри класса
  Plugin, где Log — тот же статический ManualLogSource) или log.LogInfo/
  LogWarning (в VersionGuard, где log — параметр того же типа
  ManualLogSource) — НЕ репортятся как находки C2, а считаются отдельно как
  "diagnostic-log exceptions". Обоснование (дословно из решения контроллера):
  это диагностика для разработчика, которая никогда не доходит до игрока и
  не влияет на портируемость мода на другой язык — немецкий пакет будет
  работать корректно, даже если собственные dev-логи этого проекта остаются
  русскими для его русскоязычных мейнтейнеров. Это про GAME-FACING текст и
  ВЕТВЛЕНИЕ по языку, а не "ни один русский символ не может встретиться в
  вызове логгера".
  Исключение реализовано ТОЧЕЧНО, а не по строке/методу/файлу: скрипт находит
  вызов лог-функции по регэкспу (см. LOG_CALL_RE), затем ищет символ ')',
  реально закрывающий именно ЭТОТ вызов (с учётом вложенных скобок и того,
  что скобки/кавычки ВНУТРИ строковых литералов не считаются), и считает
  диапазон [открывающая '(' ; закрывающая ')'] "зоной исключения". Находка
  (кириллица/lang-literal) считается диагностическим исключением, только
  если ВСЕ её вхождения на этой строке лежат внутри такой зоны — если на той
  же строке есть кириллица ВНЕ вызова логгера (несвязанное нарушение), строка
  по-прежнему репортится как находка, а не тихо проглатывается целиком.

Запуск: python check_lang_neutrality.py
"""
import io
import os
import re
import sys

CYRILLIC_RE = re.compile(u"[А-Яа-яЁё]")  # А-Яа-яЁё
RU_LITERAL_RE = re.compile(r'"(Russian|ru)"')

# Receiver must be exactly "Log" or "log" (optionally "Plugin.Log"), not a
# suffix of some other identifier ("MyLog.LogInfo" must NOT match) — the
# negative lookbehind enforces a fresh identifier boundary before it.
LOG_CALL_RE = re.compile(r'(?<![A-Za-z0-9_.])(?:Plugin\.)?[Ll]og\.Log(?:Info|Warning|Error)\s*\(')

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


def find_diagnostic_log_spans(stripped_text):
    """Returns a list of (open_paren_idx, close_paren_idx) character-offset
    spans covering the argument list of every Plugin.Log.Log*/Log.Log*/
    log.Log* call found in the (comment-stripped) file text."""
    spans = []
    for m in LOG_CALL_RE.finditer(stripped_text):
        open_idx = m.end() - 1
        assert stripped_text[open_idx] == "("
        close_idx = _find_matching_close_paren(stripped_text, open_idx)
        spans.append((open_idx, close_idx))
    return spans


def _in_any_span(offset, spans):
    return any(lo <= offset <= hi for lo, hi in spans)


def check_file(path):
    """Возвращает (findings, exceptions) — оба списки (line_no, text)."""
    findings = []
    exceptions = []
    raw = io.open(path, encoding="utf-8-sig").read()
    stripped = strip_comments(raw)
    log_spans = find_diagnostic_log_spans(stripped)

    stripped_lines = stripped.split("\n")
    line_offset = 0
    for idx, line in enumerate(stripped_lines):
        line_no = idx + 1

        cyr_matches = list(CYRILLIC_RE.finditer(line))
        if cyr_matches:
            offsets = [line_offset + m.start() for m in cyr_matches]
            snippet = line.strip()
            if all(_in_any_span(o, log_spans) for o in offsets):
                exceptions.append((line_no, "cyrillic (diagnostic-log message): " + snippet))
            else:
                # At least one Cyrillic char on this line sits outside any
                # logger call's argument list -> real finding, not swallowed
                # just because the line ALSO happens to contain a log call.
                findings.append((line_no, "cyrillic: " + snippet))

        for m in RU_LITERAL_RE.finditer(line):
            if "Config.Bind" in line:
                continue  # legitimate config default value, C2's explicit exclusion
            offset = line_offset + m.start()
            if _in_any_span(offset, log_spans):
                exceptions.append((line_no, "lang-literal (diagnostic-log message): " + line.strip()))
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
        findings, exceptions = check_file(path)
        for line_no, text in findings:
            print("%s:%d: %s" % (rel, line_no, text))
            total_findings += 1
        for line_no, text in exceptions:
            print("%s:%d: EXEMPT(diagnostic-log) %s" % (rel, line_no, text))
            total_exceptions += 1

    if total_findings == 0:
        print("OK: 0 findings across %d files (plugin/**/*.cs, core/**/*.cs, "
              "core/OstraI18n.Core.Tests/ and build artifacts excluded); "
              "%d diagnostic-log exception(s) applied" % (len(files), total_exceptions))
        return 0
    else:
        print("FAIL: %d finding(s) across %d files (%d diagnostic-log exception(s) applied)"
              % (total_findings, len(files), total_exceptions))
        return 1


if __name__ == "__main__":
    sys.exit(main())
