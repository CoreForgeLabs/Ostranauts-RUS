#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
OstraI18n - Steam Workshop Packaging & Preparation Script
CFLabs (CoreForgeLabs)
"""

import os
import sys
import shutil
import json
import re
from PIL import Image

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
GAME_DIR = os.environ.get("OSTRANAUTS_GAME_DIR", r"F:\Games\Steam\steamapps\common\Ostranauts")
WORKSHOP_DIR = os.path.join(SCRIPT_DIR, "workshop", "OstraI18n")
def _plugin_version():
    """Версию берём из Plugin.cs, как это делает build_release.py: жёстко
    прописанный путь к сборке v2.0 давно устарел, и копирование молча
    пропускалось -- в пакет уезжал старый BepInEx или вовсе никакого."""
    cs = os.path.join(SCRIPT_DIR, "plugin", "OstraI18n", "Plugin.cs")
    try:
        with open(cs, encoding="utf-8") as f:
            m = re.search(r'Version\s*=\s*"([^"]+)"', f.read(), re.IGNORECASE)
        if m:
            v = m.group(1)
            return "2.2" if v.startswith("2.2") else ("2.0" if v.startswith("2.0") else v)
    except Exception:
        pass
    return "2.2"


RELEASE_BEPINEX = os.path.join(SCRIPT_DIR, "Релиз",
                               f"OstraI18n_v{_plugin_version()}", "BepInEx_6", "BepInEx")

def main():
    print("=" * 60)
    print("  Подготовка пакета для Steam Workshop (Ostranauts v2.0)")
    print("=" * 60)

    os.makedirs(WORKSHOP_DIR, exist_ok=True)

    # 1. Generate 512x512 transparent preview.png from astronaut_ru.png
    astro_path = os.path.join(SCRIPT_DIR, "langs", "ru", "images", "astronaut_ru.png")
    preview_path = os.path.join(WORKSHOP_DIR, "preview.png")
    if os.path.exists(astro_path):
        with Image.open(astro_path) as im:
            w, h = im.size
            scale = min(500.0 / w, 500.0 / h)
            new_w = int(w * scale)
            new_h = int(h * scale)
            im_resized = im.resize((new_w, new_h), Image.Resampling.LANCZOS)
            canvas = Image.new('RGBA', (512, 512), (0, 0, 0, 0))
            pos_x = (512 - new_w) // 2
            pos_y = (512 - new_h) // 2
            canvas.paste(im_resized, (pos_x, pos_y), im_resized)
            canvas.save(preview_path, format="PNG", optimize=True)
            print(f"[1/5] Создан чистый прозрачный preview.png (512x512) -> {preview_path}")

    # 2. Generate mod_info.json
    mod_info = [
        {
            "strName": "OstraI18n - Русификатор Ostranauts v2.0",
            "strAuthor": "CFLabs (CoreForgeLabs)",
            "strModURL": "https://github.com/CoreForgeLabs/Ostranauts_i18n",
            "strGameVersion": "1.0.0.9",
            "strModVersion": "2.0.0",
            "strNotes": "Полная русификация Ostranauts от CFLabs (v2.0).\n\nПоддержка и новые проекты на Boosty: https://boosty.to/coreforgelabs",
            "aTags": ["Localization", "Russian", "Translation", "UI", "Language"],
            "strWorkshopID": ""
        }
    ]
    mod_info_path = os.path.join(WORKSHOP_DIR, "mod_info.json")
    with open(mod_info_path, "w", encoding="utf-8") as f:
        json.dump(mod_info, f, ensure_ascii=False, indent=2)
    print(f"[2/5] Создан mod_info.json -> {mod_info_path}")

    # 3. Create game data and copy BepInEx files
    dst_data = os.path.join(WORKSHOP_DIR, "data")
    if os.path.exists(dst_data):
        shutil.rmtree(dst_data)
    os.makedirs(dst_data, exist_ok=True)
    with open(os.path.join(dst_data, "info.json"), "w", encoding="utf-8") as f:
        json.dump([{"strName": "OstraI18n_Data", "strNotes": "OstraI18n Localization Module"}], f, ensure_ascii=False, indent=2)
    print(f"[3/6] Создана безопасная папка data/ -> {dst_data}")

    dst_bepinex = os.path.join(WORKSHOP_DIR, "BepInEx")
    if os.path.exists(dst_bepinex):
        shutil.rmtree(dst_bepinex)
    if not os.path.exists(RELEASE_BEPINEX):
        print(f"ОШИБКА: не найден {RELEASE_BEPINEX}")
        print("Сначала соберите релиз: python build_release.py")
        sys.exit(1)
    if os.path.exists(RELEASE_BEPINEX):
        shutil.copytree(RELEASE_BEPINEX, dst_bepinex)
        print(f"[4/6] Скопированы файлы BepInEx/plugins/OstraI18n в {dst_bepinex}")

    # 4. Generate Steam Workshop BBCode Description
    bbcode_desc = """[h1]OstraI18n — Полная русификация Ostranauts (Версия 2.0)[/h1]

[b]Автор модификации:[/b] CFLabs (CoreForgeLabs)
[b]Поддержка проекта на Boosty:[/b] [url=https://boosty.to/coreforgelabs]https://boosty.to/coreforgelabs[/url]
[b]Исходный код на GitHub:[/b] [url=https://github.com/CoreForgeLabs/Ostranauts_i18n]https://github.com/CoreForgeLabs/Ostranauts_i18n[/url]

[hr][/hr]

[h2]🚀 Особенности русификатора:[/h2]
[list]
[*] [b]Полная локализация интерфейса[/b]: меню, MFD-экраны, система связи (Comms), термоядерный реактор, навигация, торговля, биржа кораблей и станции.
[*] [b]Грамматический движок склонений[/b]: живой русский язык с правильными падежами имён, местоимений и глагольных форм от 1-го/2-го/3-го лица.
[*] [b]Модульные шрифты высокого разрешения[/b]: кристально чёткий кириллический текст (SDF TextMeshPro).
[*] [b]Переключение на лету[/b]: интерактивный космонавт в Главном меню позволяет мгновенно менять язык (RU ⇄ EN).
[*] [b]Бортовой манифест экипажа[/b]: благодарности всем, кто поддержал развитие модификации!
[/list]

[hr][/hr]

[h2]📦 Установка:[/h2]
[olist]
[*] Убедитесь, что у вас установлен [b]BepInEx 6[/b].
[*] Скопируйте папку [b]BepInEx[/b] в корневую директорию игры Ostranauts.
[*] Запустите игру и наслаждайтесь погружением в космические будни!
[/olist]

[hr][/hr]

[h2]❤️ Благодарности экипажу (Boosty):[/h2]
[list]
[*] [b]Шейх:[/b] Сергей Коршунов
[*] [b]Адмиралы:[/b] Миша Аверин, Towland
[*] [b]Капитаны:[/b] Gundyar, Сергей Примаков, Zurics Game
[*] [b]Юнги:[/b] GreyViS, Pavel Bezik, LunarGoat, jard, languin, Анна Плагиатор
[/list]

[i]Разработкой и поддержкой модификации занимается один человек. Если вам нравится русификатор — поддержите проект на [url=https://boosty.to/coreforgelabs]Boosty[/url]![/i]
"""
    desc_path = os.path.join(SCRIPT_DIR, "workshop", "WORKSHOP_DESCRIPTION.bbcode")
    with open(desc_path, "w", encoding="utf-8") as f:
        f.write(bbcode_desc)
    print(f"[4/5] Сгенерировано описание для мастерской Steam -> {desc_path}")

    # 5. Copy to game Mods folder and update loading_order.json
    game_mods_dest = os.path.join(GAME_DIR, "Ostranauts_Data", "Mods", "OstraI18n")
    os.makedirs(os.path.dirname(game_mods_dest), exist_ok=True)
    if os.path.exists(game_mods_dest):
        shutil.rmtree(game_mods_dest)
    shutil.copytree(WORKSHOP_DIR, game_mods_dest)
    print(f"[5/6] Скопировано в папку игры Mods/ -> {game_mods_dest}")

    # 6. Enable mod in game loading_order.json in Edit mode for Steam Workshop upload
    lo_path = os.path.join(GAME_DIR, "Ostranauts_Data", "Mods", "loading_order.json")
    lo_data = [
        {
            "strName": "Mod Loading Order",
            "aLoadOrder": ["core", "OstraI18n|edit"],
            "CORE_MOD_NAME": "core"
        }
    ]
    with open(lo_path, "w", encoding="utf-8") as f:
        json.dump(lo_data, f, ensure_ascii=False, indent=2)
    print(f"[6/6] Обновлён loading_order.json с флагом |edit -> {lo_path}")

    print("\n" + "=" * 60)
    print("  ГОТОВО! Пакет для Мастерской Steam полностью сформирован.")
    print("=" * 60)

if __name__ == "__main__":
    main()
