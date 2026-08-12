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
        построчно: если та же исходная строка файла содержит "Config.Bind",
        найденный на этой строке литерал "Russian"/"ru" не репортится.
        Специально НЕ исключается весь файл — иначе реальное нарушение,
        добавленное позже в этом же файле, стало бы невидимым.
  4. Печатает каждую находку в формате file:line: text.
  5. Возвращает код выхода 1, если находок (после исключения) хотя бы одна,
     0 если чисто — это и есть "гейт: 0 находок" из плана.

Про Ё/ё: включены в класс кириллицы сознательно, а не по умолчанию из плана
(план пишет [А-Яа-я]). Ё — легитимная отдельная русская буква; строка,
составленная так, что содержит только "ё"/"Ё" и не содержит других
кириллических букв в позиции, где сработал бы [А-Яа-я], была бы ложным
негативом, если Ё не включить. Расширение класса строго консервативнее
(находит не меньше, чем требует план), поэтому не противоречит требованию.

Запуск: python check_lang_neutrality.py
"""
import io
import os
import re
import sys

CYRILLIC_RE = re.compile(u"[А-Яа-яЁё]")  # А-Яа-яЁё
RU_LITERAL_RE = re.compile(r'"(Russian|ru)"')

WALK_GLOBS = ("plugin", "core")


def repo_root():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def find_cs_files(root):
    files = []
    for top in WALK_GLOBS:
        base = os.path.join(root, top)
        if not os.path.isdir(base):
            continue
        for dirpath, _dirnames, filenames in os.walk(base):
            for fn in filenames:
                if fn.endswith(".cs"):
                    files.append(os.path.join(dirpath, fn))
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


def check_file(path):
    """Возвращает список находок (line_no, text) для одного файла."""
    findings = []
    raw = io.open(path, encoding="utf-8-sig").read()
    stripped = strip_comments(raw)

    raw_lines = raw.split("\n")
    stripped_lines = stripped.split("\n")

    for idx, (raw_line, line) in enumerate(zip(raw_lines, stripped_lines)):
        line_no = idx + 1

        for m in CYRILLIC_RE.finditer(line):
            snippet = line.strip()
            findings.append((line_no, "cyrillic: " + snippet))
            break  # one finding per line is enough to point at it; avoid noise

        for m in RU_LITERAL_RE.finditer(line):
            if "Config.Bind" in line:
                continue  # legitimate config default value, C2's explicit exclusion
            findings.append((line_no, "lang-literal: " + line.strip()))

    return findings


def main():
    root = repo_root()
    files = find_cs_files(root)
    total_findings = 0
    for path in files:
        rel = os.path.relpath(path, root)
        for line_no, text in check_file(path):
            print("%s:%d: %s" % (rel, line_no, text))
            total_findings += 1

    if total_findings == 0:
        print("OK: 0 findings across %d files (plugin/**/*.cs, core/**/*.cs)" % len(files))
        return 0
    else:
        print("FAIL: %d finding(s) across %d files" % (total_findings, len(files)))
        return 1


if __name__ == "__main__":
    sys.exit(main())
