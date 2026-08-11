#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
translate_ui.py — переводит непереведённые UI-строки Фазы 1/2 (langs/en/ui/generated.json,
langs/en/ui/prefabs.json) через Qwen, с контекстом происхождения каждой строки
(methodKey из catalog/literals.json или путь в иерархии из catalog/prefabs.json),
параллельными батчами (Qwen многопоточный — используем chat_batch/ThreadPoolExecutor).

Пишет результат инкрементально после каждого батча — безопасно прервать и перезапустить,
уже переведённые ключи не будут переведены заново.

Запуск:  python translate_ui.py [lang]     (по умолчанию ru)
"""
import concurrent.futures
import io
import json
import os
import sys
import time

sys.path.insert(0, r"C:\Users\Low\Desktop\DEV\KWEN")
from llm_client import chat_json, check_api  # noqa: E402

ROOT = r"F:\DEV2\ostra_i18n"
LANG = sys.argv[1] if len(sys.argv) > 1 else "ru"
LANGNAME = {"ru": "русский", "de": "немецкий", "fr": "французский", "es": "испанский",
            "pt": "португальский", "zh": "китайский (упрощённый)", "ja": "японский",
            "ko": "корейский", "pl": "польский", "uk": "украинский"}.get(LANG, LANG)

BATCH = 20
MAX_WORKERS = 12
LOG = os.path.join(ROOT, "lang_src", "translate_ui_%s.log" % LANG)

SYS_PROMPT = (
    "Ты локализатор интерфейса игры Ostranauts — суровый hard sci-fi симулятор "
    "разборки списанных космических кораблей и выживания на орбитальных станциях "
    "у пояса астероидов (сеттинг в духе Cowboy Bebop / The Expanse, тон циничный, "
    "потрёпанный, без пафоса). Переведи каждую строку интерфейса на %s.\n\n"
    "Правила:\n"
    "- Каждый элемент содержит \"ctx\" — техническое происхождение строки в игре "
    "(имя C#-метода вида Класс::Метод, ЛИБО путь в иерархии UI вида Родитель/Ребёнок/лист). "
    "Используй его ТОЛЬКО чтобы понять роль строки на экране (заголовок диалога, подпись кнопки, "
    "тултип, лейбл статуса и т.п.) — в перевод его не включай.\n"
    "- Заголовки/кнопки/лейблы переводи кратко, как принято в интерфейсах — не длиннее оригинала "
    "более чем на 20-30%%, экранное пространство ограничено.\n"
    "- Сохраняй БЕЗ ИЗМЕНЕНИЙ на исходных местах: плейсхолдеры вида {0}, %%s, %%d; теги разметки "
    "вида <b>...</b>, <i>...</i>, \\n, \\t; токены [us]/[them]/[verb]/[cap]; ведущие/замыкающие пробелы "
    "и знаки препинания (если строка обрывается многоточием или без точки в конце — сохраняй).\n"
    "- Если строка — это ключ/токен без пробелов (типа ALL_CAPS_WITH_UNDERSCORES) или служебный "
    "мусор без смысла — верни как есть, не переводи.\n"
    "- Термины: 'ship' -> 'корабль', 'crew' -> 'экипаж', 'salvage/scavenge' -> 'разборка/лом', "
    "'captain' -> 'капитан', 'career' -> 'карьера'. Единообразно во всех строках.\n\n"
    "Ответ — СТРОГО JSON-объект {\"id\": \"перевод\", ...} с ТЕМИ ЖЕ id, что во входном массиве. "
    "Ничего кроме JSON-объекта в ответе быть не должно."
) % LANGNAME


def log(msg):
    line = "[%s] %s" % (time.strftime("%H:%M:%S"), msg)
    print(line, flush=True)
    with io.open(LOG, "a", encoding="utf-8") as f:
        f.write(line + "\n")


def load_json(path):
    with io.open(path, encoding="utf-8") as f:
        return json.load(f)


def save_json(path, data):
    tmp = path + ".tmp"
    with io.open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    os.replace(tmp, path)


def pack_dict(d):
    """Единый dict строкового пака -> {"strName":..., "strLanguage":..., "dict": d}."""
    return [{"strName": "Game Strings", "strLanguage": LANGNAME.capitalize(), "dict": d}]


def build_context_maps():
    """methodKey по ключу (Фаза 1) и root/path по ключу (Фаза 2), для подсказки Qwen."""
    method_ctx = {}
    lits = load_json(os.path.join(ROOT, "catalog", "literals.json"))
    for e in lits:
        if e.get("key"):
            method_ctx[e["key"]] = e.get("methodKey", "")

    path_ctx = {}
    prefabs = load_json(os.path.join(ROOT, "catalog", "prefabs.json"))
    for e in prefabs:
        if e.get("key"):
            path_ctx[e["key"]] = e.get("root", "") + "/" + "/".join(e.get("path", []))

    return method_ctx, path_ctx


def translate_pack(en_path, ru_path, ctx_map, label):
    en_data = load_json(en_path)
    en_dict = en_data[0]["dict"]

    ru_data = load_json(ru_path) if os.path.exists(ru_path) else pack_dict({})
    ru_dict = ru_data[0]["dict"]

    todo = [k for k in en_dict if k not in ru_dict or ru_dict[k] == en_dict[k]]
    log("%s: всего %d, уже переведено %d, к переводу %d" %
        (label, len(en_dict), len(en_dict) - len(todo), len(todo)))
    if not todo:
        return 0

    batches = [todo[i:i + BATCH] for i in range(0, len(todo), BATCH)]

    def run_batch(batch_keys):
        items = [{"id": k, "en": en_dict[k], "ctx": ctx_map.get(k, "")} for k in batch_keys]
        try:
            res = chat_json(SYS_PROMPT, items, model="qwen")
        except Exception as e:
            log("%s: batch ERROR: %s" % (label, e))
            return {}
        if not isinstance(res, dict):
            log("%s: batch вернул не-dict, пропуск (%s)" % (label, type(res)))
            return {}
        return res

    done = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=MAX_WORKERS) as ex:
        futures = {ex.submit(run_batch, b): b for b in batches}
        for fut in concurrent.futures.as_completed(futures):
            batch_keys = futures[fut]
            res = fut.result()
            got = 0
            for k in batch_keys:
                v = res.get(k)
                if v and str(v).strip():
                    ru_dict[k] = str(v)
                    got += 1
            done += got
            log("%s: батч из %d -> +%d (всего готово %d/%d)" %
                (label, len(batch_keys), got, done, len(todo)))
            save_json(ru_path, pack_dict(ru_dict))

    return done


def main():
    api = check_api(model="qwen")
    log("api: %s" % api)
    if not api.get("ok"):
        log("ERROR: Qwen-прокси недоступен")
        sys.exit(1)

    method_ctx, path_ctx = build_context_maps()

    total = 0
    total += translate_pack(
        os.path.join(ROOT, "langs", "en", "ui", "generated.json"),
        os.path.join(ROOT, "langs", LANG, "ui", "generated.json"),
        method_ctx, "generated (литералы кода)")
    total += translate_pack(
        os.path.join(ROOT, "langs", "en", "ui", "prefabs.json"),
        os.path.join(ROOT, "langs", LANG, "ui", "prefabs.json"),
        path_ctx, "prefabs (текст в сценах/префабах)")

    log("ГОТОВО: переведено %d строк суммарно" % total)


if __name__ == "__main__":
    main()
