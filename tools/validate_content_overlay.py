#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
validate_content_overlay.py — офлайн-проверка langs/ru/data/*.json перед деплоем.
Блокирует (exit 1): расхождение токенов/плейсхолдеров/разметки, сироты (strName
отсутствует в текущих данных игры). Предупреждает без блокировки: перевод совпадает
с оригиналом дословно (вероятно забыт).

Запуск: python validate_content_overlay.py
"""
import io
import json
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from import_old_translation import load_category, validate, CUR_DATA, TRANSLATABLE  # noqa: E402

ROOT = r"F:\DEV2\ostra_i18n"
RU_DATA = os.path.join(ROOT, "langs", "ru", "data")


def main():
    if not os.path.isdir(RU_DATA):
        print("нет langs/ru/data — нечего проверять")
        return 0

    errors = 0
    warnings = 0
    for fn in sorted(os.listdir(RU_DATA)):
        if not fn.endswith(".json"):
            continue
        category = fn[:-5]
        overlay = json.loads(io.open(os.path.join(RU_DATA, fn), encoding="utf-8").read())
        cur = load_category(CUR_DATA, category)
        print("=== %s: %d записей в оверлее ===" % (category, len(overlay)))

        for str_name, fields in overlay.items():
            if str_name not in cur:
                print("  ОШИБКА: сирота '%s' — отсутствует в текущих данных игры" % str_name)
                errors += 1
                continue
            cur_obj = cur[str_name]
            for field, ru_val in fields.items():
                if field not in TRANSLATABLE:
                    print("  ОШИБКА: '%s'.%s — поле не в списке переводимых" % (str_name, field))
                    errors += 1
                    continue
                en_val = cur_obj.get(field)
                if en_val is None:
                    print("  ОШИБКА: '%s'.%s — поля нет в текущих данных игры" % (str_name, field))
                    errors += 1
                    continue
                reason = validate(ru_val, en_val, field)
                if reason:
                    print("  ОШИБКА: '%s'.%s — %s" % (str_name, field, reason))
                    errors += 1
                    continue
                if ru_val.strip() == en_val.strip() and len(en_val.strip()) > 3:
                    warnings += 1

    print("итого: ошибок %d, предупреждений %d" % (errors, warnings))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
