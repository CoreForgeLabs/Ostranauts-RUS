# -*- coding: utf-8 -*-
"""
Проверка покрытия падежной таблицы.

named_forms.json -- отдельный артефакт: конвейер перевода обновляет
condowners.json и о нём не знает. Любое имя, которое он добавил или изменил,
приходит без падежей, и в игре печатается именительный: "панель обогреватель".
Дыру видно только в игре, если её специально не искать -- поэтому ищем.

Возвращает ненулевой код, если покрытие неполное: годится для CI и для вызова
в конце translate_all.py.
"""
import json, io, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CO = os.path.join(ROOT, "langs", "ru", "data", "condowners.json")
NF = os.path.join(ROOT, "langs", "ru", "named_forms.json")


def load(p):
    with io.open(p, encoding="utf-8-sig") as f:
        return json.load(f)


def main():
    co, nf = load(CO), load(NF)
    missing = [k for k in co if k not in nf]
    print("condowners: %d, падежных записей: %d, без падежей: %d"
          % (len(co), len(nf), len(missing)))
    if missing:
        print("\nБез падежных форм (в игре будут в именительном):")
        for k in missing[:40]:
            print("   %-32s %s" % (k, co[k].get("strNameFriendly", "")))
        if len(missing) > 40:
            print("   ... и ещё %d" % (len(missing) - 40))
        return 1
    print("покрытие полное")
    return 0


if __name__ == "__main__":
    sys.exit(main())
