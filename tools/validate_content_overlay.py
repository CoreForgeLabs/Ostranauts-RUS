#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
validate_content_overlay.py — офлайн-проверка langs/ru/data/*.json перед деплоем.
Блокирует (exit 1): расхождение плейсхолдеров/разметки, нерезолвящиеся токены
(Task 6.6 — см. token_check() ниже), сироты (strName отсутствует в текущих
данных игры). Предупреждает без блокировки: перевод совпадает с оригиналом
дословно (вероятно забыт), токен резолвится только в fallback на английский
(нет RU-парадигмы в verbs.json).

Запуск: python validate_content_overlay.py
"""
import io
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from import_old_translation import (  # noqa: E402
    load_category, load_simple_category, CUR_DATA, GAME, TRANSLATABLE, SIMPLE_SCHEMAS, output_category_for,
    PLACEHOLDER_RE, TAG_RE,
)

# category simple-формата (например "conditions_simple") -> категория, в файл которой
# она сливается ("conditions") — обратная сторона той же карты, что и в
# import_old_translation/translate_data: игра сама объединяет их в один словарь
# движка, поэтому при проверке "conditions" нужно ЗНАТЬ про strName из
# conditions_simple, иначе они все окажутся ложными сиротами.
MERGE_SOURCES = {}
for _simple_cat in SIMPLE_SCHEMAS:
    MERGE_SOURCES.setdefault(output_category_for(_simple_cat), []).append(_simple_cat)

# Task 6.6: было захардкожено на конкретную рабочую копию (F:\DEV2\ostra_i18n),
# из-за чего запуск из ЛЮБОГО git worktree (в т.ч. этого) молча валидировал
# файлы В ГЛАВНОЙ рабочей копии, а не в том дереве, где реально ведётся
# работа — реальный, ранее незамеченный баг (см. task-6.6-brief.md). Тот же
# приём, что и в tools/build_named_forms.py и других воркчри-безопасных
# инструментах Фазы 5/6: ROOT выводится из расположения этого файла, а не
# зашивается абсолютным путём.
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RU_DATA = os.path.join(ROOT, "langs", "ru", "data")

# Категории с "/" в исходном пути (например racing/tracks) сохраняются на диск как
# racing_tracks.json (см. import_old_translation.py: category.replace("/", "_")) —
# обратное сопоставление нужно, чтобы найти реальные текущие данные игры по правильному
# вложенному пути, а не по имени файла буквально.
FILENAME_TO_CATEGORY = {
    "market_CoCollections": "market/CoCollections",
    "market_Production": "market/Production",
    "racing_tracks": "racing/tracks",
    "racing_leagues": "racing/leagues",
    "attackmodes_coAttacks": "attackmodes/coAttacks",
    "attackmodes_shipAttacks": "attackmodes/shipAttacks",
}


# ============================================================================
# Task 6.6 — «разрешим, а не идентичен»: набор токенов RU-перевода может
# отличаться от английского оригинала (Task 6.4 ввела [them-gen] там, где в
# EN только [them]; Task 6.5 ввела [is.aux] там, где в EN только [is]), но
# КАЖДЫЙ токен, которого нет в оригинале (т.е. реально добавлен/изменён
# переводчиком), обязан резолвиться против pack.json + verbs.json + список
# алиасов — точно так же, как это делает сам движок в PrepareToken()
# (decompiled/DataHandler.cs:3122). Токены, которые совпадают буквально с
# токеном на том же месте в EN (перенесены как есть, не тронуты
# переводчиком), НЕ перепроверяются на резолвинг: если оригинал сам содержит
# нерезолвящийся токен (см. ниже про [they-obj] и [us-contractHave] —
# реальные баги ДАННЫХ ИГРЫ, не нашего перевода), это не наша ошибка и не
# должно наплодить новых ложных срабатываний на существующих 24861 полях.
# ============================================================================

# Любой текст между [ и ] — ровно как делает сам движок (DataHandler.cs:
# PrepareInflectedString берёт s.Substring(num+1, num2-num-1) без каких-либо
# ограничений на символы). Дальнейшая ASCII-фильтрация (см. _is_token_candidate)
# отсекает случаи, когда квадратные скобки использованы просто как пунктуация
# в самом RU-тексте (например заголовок-ремарка «[Отрывок из профиля...]» или
# «[не замечает]» в conditions.json), а не как игровой токен — токены движка
# всегда состоят из ASCII-идентификаторов (алиасы/категории/ключи глаголов),
# кириллицы или пробельного текста внутри них не бывает.
TOKEN_RE = re.compile(r"\[([^\[\]]+)\]")


def _is_token_candidate(content):
    """True если содержимое скобок ВООБЩЕ может быть игровым токеном (ASCII-
    идентификаторного вида), а не просто декоративными квадратными скобками
    вокруг обычного RU-текста (см. TOKEN_RE)."""
    return bool(content) and all(ord(ch) < 128 for ch in content)


# Функциональные литералы из PrepareToken() (decompiled/DataHandler.cs:3146-3211)
# — НЕ переводимый текст, служебные токены, которые движок обрабатывает
# по имени напрямую (не через алиасы/категории/verbs.json). Плюс [DOCKID] —
# отдельный механизм (Ostranauts.ShipGUIs.MFD/MFDComms.cs делает буквальный
# string.Replace("[DOCKID]", ...) вне PrepareInflectedString вообще), но с
# точки зрения переводчика это такой же "не трогать" токен.
FUNCTIONAL_LITERALS = {
    "regID", "shipfriendly", "captain", "data", "txt1", "itm", "x",
    "object", "purple", "prereq0", "firstname", "fullname", "shipname",
    "surname", "age", "homeworld", "custom", "DOCKID",
}


def _load_json_lenient(path):
    raw = io.open(path, encoding="utf-8-sig").read()
    raw = re.sub(r"//[^\n]*", "", raw)  # data/tokens/verbs.json содержит // комментарии
    return json.loads(raw, strict=False)


def _load_game_token_defs():
    """Читает decompiled/DataHandler.cs:UnpackTokens()'s источники напрямую из
    текущих данных игры (data/tokens/{aliases,verbs}.json) — тот же приём,
    что CUR_DATA уже использует для сирот/orphan-проверки, так что наш
    Python-валидатор проверяет то же самое множество алиасов/EN-ключей
    глаголов, что видит реальный движок в PrepareToken(), а не
    захардкоженный список, рассинхронизирующийся с патчами игры."""
    aliases = set()
    en_verb_keys = set()
    tokens_dir = os.path.join(CUR_DATA, "tokens")
    try:
        data = _load_json_lenient(os.path.join(tokens_dir, "aliases.json"))
        for entry in data:
            if isinstance(entry, dict) and entry.get("type") == "aliases":
                aliases.update(entry.get("tokens") or [])
    except Exception as e:
        print("ПРЕДУПРЕЖДЕНИЕ: не удалось прочитать data/tokens/aliases.json (%s) "
              "— используем запасной список алиасов" % e)
        aliases.update({"us", "them", "3rd", "racing_icon"})
    try:
        data = _load_json_lenient(os.path.join(tokens_dir, "verbs.json"))
        for entry in data:
            if isinstance(entry, dict) and entry.get("type") == "verbs":
                for pair in entry.get("tokens2") or []:
                    if pair:
                        en_verb_keys.add(pair[0])
    except Exception as e:
        print("ПРЕДУПРЕЖДЕНИЕ: не удалось прочитать data/tokens/verbs.json (%s) "
              "— проверка English-fallback ключей глаголов будет неполной" % e)
    return aliases, en_verb_keys


def _load_pack_categories():
    """pack.json's pronounCategories (Task 6.1/6.4) — это множество, которое
    Patches.UnpackTokensPostfix реально регистрирует в DataHandler.categories
    в рантайме (и оно уже является суперсетом ванильных grammar.json-категорий
    subj/pos/obj/reflexive/contractIs/contractHas/contractWill/contractWould,
    см. langs/ru/pack.json), плюс новая "gen" из Task 6.4."""
    pack_path = os.path.join(ROOT, "langs", "ru", "pack.json")
    pack = json.loads(io.open(pack_path, encoding="utf-8").read())
    return set((pack.get("pronounCategories") or {}).keys())


def _load_ru_verb_keys():
    verbs_path = os.path.join(ROOT, "langs", "ru", "verbs.json")
    verbs = json.loads(io.open(verbs_path, encoding="utf-8").read())
    return {k for k in verbs.keys() if not k.startswith("_")}


ALIASES, EN_VERB_KEYS = _load_game_token_defs()
CATEGORIES = _load_pack_categories()
RU_VERB_KEYS = _load_ru_verb_keys()


def _classify_part(part):
    """Возвращает один из: 'alias', 'category', 'verb_ru', 'verb_en_only',
    'functional', или None если часть токена (после split('-')) не резолвится
    ни во что известное движку."""
    if part in ALIASES:
        return "alias"
    if part in CATEGORIES:
        return "category"
    if part in FUNCTIONAL_LITERALS:
        return "functional"
    if part in RU_VERB_KEYS:
        return "verb_ru"
    if part in EN_VERB_KEYS:
        return "verb_en_only"
    return None


def token_check(ru_val, en_val):
    """Task 6.6 core: возвращает (errors, warnings) — списки строк с
    причинами. Токен, буквально совпадающий с токеном на EN-стороне,
    пропускается (см. модульный docstring выше про [they-obj] /
    [us-contractHave] — унаследованные баги ДАННЫХ ИГРЫ, не нашего перевода).
    Токен, которого нет в EN (переводчик добавил/изменил его — например Task
    6.4's [them-gen], Task 6.5's [is.aux]), обязан резолвиться:
      - хотя бы одна '-'-часть матчит category/functional/verb_ru, ИЛИ
      - хотя бы одна часть матчит verb_en_only (резолвится, но упадёт в
        английский fallback в рантайме — VerbPrefix в Patches.cs, WARNING,
        не ERROR), ИЛИ
      - токен из ОДНОЙ части и эта часть — alias (голый '[them]').
    Иначе — ERROR (ни alias, ни category, ни verb, ни functional-литерал не
    матчат ни одну часть токена — движок оставит битые квадратные скобки
    видимыми в UI, см. PrepareInflectedString's "token made without output").
    """
    errors = []
    warnings = []

    en_tokens_raw = set(m for m in TOKEN_RE.findall(en_val) if _is_token_candidate(m))

    for content in TOKEN_RE.findall(ru_val):
        if not _is_token_candidate(content):
            continue  # декоративные скобки вокруг обычного RU-текста, не токен
        if content in en_tokens_raw:
            continue  # не тронуто переводчиком — унаследованная проблема EN, не наша

        parts = content.split("-")
        kinds = [_classify_part(p) for p in parts]

        if len(parts) == 1 and kinds[0] == "alias":
            continue  # голый '[them]'/'[us]'/'[3rd]'/'[racing_icon]'
        if any(k in ("category", "functional", "verb_ru") for k in kinds):
            continue  # резолвится полноценно (с RU-парадигмой, если это глагол)
        if any(k == "verb_en_only" for k in kinds):
            warnings.append(
                "[%s] — ключ глагола есть в EN (%s), но нет RU-парадигмы в verbs.json: "
                "в игре останется английский текст (fallback), не ошибка"
                % (content, [p for p, k in zip(parts, kinds) if k == "verb_en_only"]))
            continue

        errors.append(
            "токен '[%s]' не резолвится ни против алиасов (%s), ни против pack.json "
            "pronounCategories, ни против verbs.json/списка EN-глаголов, и отсутствует "
            "в EN-оригинале — похоже на опечатку" % (content, sorted(ALIASES)))

    return errors, warnings


def validate_fields(ru_val, en_val):
    """Заменяет старую import_old_translation.validate() для целей этого
    скрипта: плейсхолдеры/разметка проверяются РОВНО как раньше (сравнение
    множеств RU vs EN, без изменений — вне периметра Task 6.6), а проверка
    токенов — новой token_check() логикой вместо старого «множества токенов
    должны совпадать буквально». Возвращает (errors, warnings) — списки строк."""
    errors = []
    warnings = []

    tok_errors, tok_warnings = token_check(ru_val, en_val)
    errors.extend(tok_errors)
    warnings.extend(tok_warnings)

    ru_ph = sorted(PLACEHOLDER_RE.findall(ru_val))
    en_ph = sorted(PLACEHOLDER_RE.findall(en_val))
    if ru_ph != en_ph:
        errors.append("плейсхолдеры разошлись: en=%s ru=%s" % (en_ph, ru_ph))

    ru_tags = sorted(TAG_RE.findall(ru_val))
    en_tags = sorted(TAG_RE.findall(en_val))
    if ru_tags != en_tags:
        errors.append("разметка разошлась: en=%s ru=%s" % (en_tags, ru_tags))

    if not ru_val.strip():
        errors.append("пустой перевод")

    return errors, warnings


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
        real_category = FILENAME_TO_CATEGORY.get(category, category)
        cur = load_category(CUR_DATA, real_category)
        for simple_cat in MERGE_SOURCES.get(real_category, []):
            cur.update(load_simple_category(CUR_DATA, simple_cat))
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
                field_errors, field_warnings = validate_fields(ru_val, en_val)
                for reason in field_errors:
                    print("  ОШИБКА: '%s'.%s — %s" % (str_name, field, reason))
                    errors += 1
                for reason in field_warnings:
                    print("  ПРЕДУПРЕЖДЕНИЕ: '%s'.%s — %s" % (str_name, field, reason))
                    warnings += 1
                if field_errors:
                    continue
                if ru_val.strip() == en_val.strip() and len(en_val.strip()) > 3:
                    warnings += 1

    print("итого: ошибок %d, предупреждений %d" % (errors, warnings))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
