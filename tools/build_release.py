#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_release.py — собирает i18n_release/: готовый к раздаче пакет перевода,
который игрок распаковывает в корень игры и запускает.

Структура релиза зеркалит корень игры, чтобы распаковка была "поверх":
  i18n_release/
    winhttp.dll, doorstop_config.ini, .doorstop_version   <- BepInEx doorstop
    BepInEx/core/...                                       <- BepInEx 6 be.785 лоадер
    BepInEx/plugins/OstraI18n/  (dll + grammar + verbs)    <- плагин движка
    Ostranauts_Data/Mods/lang_ru/ + loading_order.json     <- переведённый контент
    INSTALL_RU.txt                                         <- инструкция для игроков
"""
import os, shutil, io

ROOT = r"F:\DEV2\ostra_i18n"
GAME = r"F:\Games\Steam\steamapps\common\Ostranauts"
REL = os.path.join(ROOT, "i18n_release")
BEP = os.path.join(ROOT, "bepinex6_be", "extracted")

def cp_file(src, dst):
    if not os.path.exists(src):
        print("  MISSING:", src); return
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy2(src, dst)

def cp_tree(src, dst):
    if os.path.exists(src):
        shutil.copytree(src, dst, dirs_exist_ok=True)

print("=== собираю i18n_release ===")
# 1. BepInEx doorstop + core
cp_file(os.path.join(BEP, "winhttp.dll"), os.path.join(REL, "winhttp.dll"))
cp_file(os.path.join(BEP, "doorstop_config.ini"), os.path.join(REL, "doorstop_config.ini"))
cp_file(os.path.join(BEP, ".doorstop_version"), os.path.join(REL, ".doorstop_version"))
cp_tree(os.path.join(BEP, "BepInEx", "core"), os.path.join(REL, "BepInEx", "core"))
print("  + BepInEx loader")

# 2. плагин OstraI18n + langs/ (манифест языков + языковые папки со ВСЕМ)
plug = os.path.join(GAME, "BepInEx", "plugins", "OstraI18n")
dst = os.path.join(REL, "BepInEx", "plugins", "OstraI18n")
cp_file(os.path.join(plug, "OstraI18n.dll"), os.path.join(dst, "OstraI18n.dll"))
cp_tree(os.path.join(plug, "langs"), os.path.join(dst, "langs"))
print("  + OstraI18n plugin + langs/ (languages.json + lang_ru/)")

# 3. мод lang_ru + loading_order.json
mods = os.path.join(GAME, "Ostranauts_Data", "Mods")
cp_tree(os.path.join(mods, "lang_ru"), os.path.join(REL, "Ostranauts_Data", "Mods", "lang_ru"))
cp_file(os.path.join(mods, "loading_order.json"), os.path.join(REL, "Ostranauts_Data", "Mods", "loading_order.json"))
print("  + мод lang_ru + loading_order.json")

# 4. инструкция для игроков
readme = (
    "РУССКИЙ ПЕРЕВОД OSTRANAUTS (OstraI18n)\r\n"
    "=====================================\r\n\r\n"
    "УСТАНОВКА:\r\n"
    "1. Скопируй ВСЁ содержимое этой папки в корень игры — туда, где лежит Ostranauts.exe.\r\n"
    "   Обычно: ...\\Steam\\steamapps\\common\\Ostranauts\\r\n"
    "   Соглашайся на слияние/замену папок (ничего из игры не удаляется).\r\n"
    "2. Запусти игру. В главном меню: MODS -> убедись, что мод 'lang_ru' включён.\r\n"
    "3. Играй на русском.\r\n\r\n"
    "ЧТО ВНУТРИ:\r\n"
    "- BepInEx 6 (лоадер) + плагин OstraI18n: движок грамматики (местоимения/спряжения)\r\n"
    "  + кириллический fallback-шрифт.\r\n"
    "- Мод lang_ru: переведённые строки интерфейса.\r\n\r\n"
    "УДАЛЕНИЕ: удали добавленные файлы (winhttp.dll, doorstop_config.ini, .doorstop_version,\r\n"
    "BepInEx\\, Ostranauts_Data\\Mods\\lang_ru\\) и убери 'lang_ru' из Mods\\loading_order.json.\r\n"
)
io.open(os.path.join(REL, "INSTALL_RU.txt"), "w", encoding="utf-8").write(readme)
print("  + INSTALL_RU.txt")

print("=== итог i18n_release ===")
for root, dirs, files in os.walk(REL):
    rel = root.replace(REL, "")
    print("  %s/  (%d файлов)" % (rel, len(files)))
