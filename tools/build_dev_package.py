#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_dev_package.py — собирает dist/ostranauts-i18n/ из уже существующих
частей проекта (плагин, каталоги, языковые паки, инструменты) по структуре
из спеки. Ничего не генерирует заново — только копирует и пишет README/MIGRATION.

Запуск: python build_dev_package.py
"""
import os
import shutil

ROOT = r"F:\DEV2\ostra_i18n"
OUT = os.path.join(ROOT, "dist", "ostranauts-i18n")

README = """# OstraI18n — референсная реализация key-based i18n для Ostranauts

Что это: рабочий мод (BepInEx) + референсные C#-исходники + каталоги извлечённого
текста + русский перевод, полученные без единого сопоставления по английскому тексту
(см. SPEC.md, раздел "Архитектура").

Проверить за 5 минут:
1. Скопировать содержимое `langs/` в `<Game>/BepInEx/plugins/OstraI18n/langs/`
   (или, при внедрении в исходники игры — в `StreamingAssets`, см. MIGRATION.md шаг 5).
2. Запустить игру с установленным модом (или после внедрения `src/` — без мода).
3. Русский текст должен появиться в UI, в контекстных меню объектов и в описаниях
   состояний персонажа.

Дальше: MIGRATION.md — пошаговое внедрение в исходники игры вместо мода.

Инструменты извлечения (`FormatExtract`, каталогизация литералов/префабов) в этот
пакет не включены — они завязаны на пути машины автора и требуют декомпилированной
сборки игры как аргумент командной строки. Каталоги (`catalog/`) и отчёт
(`patches/formats.md`) уже сгенерированы этими инструментами и включены как результат.
"""

MIGRATION = """# Внедрение в исходники игры

| Шаг | Объём | Автоматизация |
|---|---|---|
| 1. Положить `src/` в проект | — | — |
| 2. Прогнать `editor/ApplyBindings.cs` | ~1250 объектов (каталог `catalog/prefabs.json`) | полная |
| 3. Заменить литералы на `GetString` по `catalog/literals.json` | список готов | механическая замена, требует пересборки |
| 4. Применить формат-строки из `patches/formats.md` | см. файл | diff готов, нужна проверка человеком |
| 5. Положить `langs/` в `StreamingAssets` | — | — |
| 6. Удалить BepInEx-плагин | — | плагин больше не нужен |

Шаг 6 — критерий успеха: если после шагов 1-5 плагин всё ещё требуется для перевода,
внедрение выполнено не полностью.
"""

COPY_MAP = [
    # (относительный источник, относительное назначение внутри dist/ostranauts-i18n)
    ("plugin/OstraI18n/I18n.cs", "src/I18n.cs"),
    ("plugin/OstraI18n/LocalizedText.cs", "src/LocalizedText.cs"),
    ("plugin/OstraI18n/PrefabBinder.cs", "src/PrefabBinder.cs"),
    ("plugin/OstraI18n/ContentOverlay.cs", "src/ContentOverlay.cs"),
    ("core/OstraI18n.Core/LanguagePack.cs", "src/LanguagePack.cs"),
    ("core/OstraI18n.Core/PackLoader.cs", "src/PackLoader.cs"),
    ("core/OstraI18n.Core/PluralRule.cs", "src/PluralRule.cs"),
    ("core/OstraI18n.Core/MethodKey.cs", "src/MethodKey.cs"),
    ("core/OstraI18n.Core/PathKey.cs", "src/PathKey.cs"),
    ("docs/superpowers/specs/2026-08-11-ostra-i18n-keybased-design.md", "SPEC.md"),
    ("catalog/literals.json", "catalog/literals.json"),
    ("catalog/prefabs.json", "catalog/prefabs.json"),
    ("patches/formats.md", "patches/formats.md"),
]


def main():
    if os.path.exists(OUT):
        shutil.rmtree(OUT)
    os.makedirs(OUT)

    missing = []
    for src_rel, dst_rel in COPY_MAP:
        src = os.path.join(ROOT, src_rel)
        dst = os.path.join(OUT, dst_rel)
        if not os.path.exists(src):
            missing.append(src_rel)
            continue
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)

    shutil.copytree(os.path.join(ROOT, "langs"), os.path.join(OUT, "langs"))

    with open(os.path.join(OUT, "README.md"), "w", encoding="utf-8") as f:
        f.write(README)
    with open(os.path.join(OUT, "MIGRATION.md"), "w", encoding="utf-8") as f:
        f.write(MIGRATION)

    if missing:
        print("ПРЕДУПРЕЖДЕНИЕ: не найдены и пропущены:")
        for m in missing:
            print("  -", m)
    print("собрано:", OUT)


if __name__ == "__main__":
    main()
