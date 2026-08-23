#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
OstraI18n - Automatic Release Packaging Script
CFLabs (CoreForgeLabs)

Automates:
1. Compilation of OstraI18n.Core + OstraI18n for BepInEx 5 (Release_v5)
2. Compilation of OstraI18n.Core + OstraI18n for BepInEx 6 (Release_v6)
3. Assembly of two ready-to-extract bundles (BepInEx 5 / BepInEx 6)
4. Generation of installation guide and README
5. Creation of clean zip archives in the 'Релиз' folder
"""

import os
import sys
import shutil
import zipfile
import re
import subprocess

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RELEASE_ROOT = os.path.join(SCRIPT_DIR, "Релиз")

# BepInEx flavour -> (msbuild configuration, extracted framework folder)
VARIANTS = {
    "6": ("Release_v6", os.path.join(SCRIPT_DIR, "bepinex6_be", "extracted")),
    "5": ("Release_v5", os.path.join(SCRIPT_DIR, "bepinex5", "extracted")),
}

def get_plugin_version():
    plugin_cs = os.path.join(SCRIPT_DIR, "plugin", "OstraI18n", "Plugin.cs")
    if os.path.exists(plugin_cs):
        with open(plugin_cs, "r", encoding="utf-8") as f:
            content = f.read()
            m = re.search(r'Version\s*=\s*"([^"]+)"', content, re.IGNORECASE)
            if m:
                return m.group(1)
    return "2.0.0"

def run_cmd(cmd, cwd=None):
    print(f">> Running: {cmd}")
    res = subprocess.run(cmd, shell=True, cwd=cwd or SCRIPT_DIR, capture_output=True, text=True)
    if res.returncode != 0:
        print(f"ERROR: Command failed with exit code {res.returncode}")
        print("STDOUT:", res.stdout)
        print("STDERR:", res.stderr)
        sys.exit(1)
    return res.stdout

def copy_filtered_tree(src, dst, ignore_exts=None, ignore_dirs=None):
    ignore_exts = ignore_exts or {".pdb", ".pyc", ".tmp", ".bak", ".log"}
    ignore_dirs = ignore_dirs or {"obj", "bin", ".vs", ".git", ".idea", "__pycache__"}

    os.makedirs(dst, exist_ok=True)
    for root, dirs, files in os.walk(src):
        dirs[:] = [d for d in dirs if d not in ignore_dirs]
        rel = os.path.relpath(root, src)
        target_dir = os.path.join(dst, rel) if rel != "." else dst
        os.makedirs(target_dir, exist_ok=True)

        for f in files:
            ext = os.path.splitext(f)[1].lower()
            if ext in ignore_exts or f.startswith("."):
                continue
            src_f = os.path.join(root, f)
            dst_f = os.path.join(target_dir, f)
            shutil.copy2(src_f, dst_f)

def make_install_guide(display_ver):
    return f"""======================================================================
  OstraI18n - Полная русификация для Ostranauts (Версия v{display_ver})
  Автор модификации: CFLabs (CoreForgeLabs)
  Поддержка на Boosty: https://boosty.to/coreforgelabs
======================================================================

КАКОЙ АРХИВ ВЫБРАТЬ:

- OstraI18n_v{display_ver}_BepInEx6.zip - рекомендуемый вариант (BepInEx 6).
- OstraI18n_v{display_ver}_BepInEx5.zip - если BepInEx 6 у вас не запускается
  или уже установлен BepInEx 5.

Ставьте ТОЛЬКО ОДИН из архивов - две версии BepInEx одновременно не работают.
Если раньше стоял другой вариант, удалите старую папку BepInEx перед установкой.

ИНСТРУКЦИЯ ПО УСТАНОВКЕ (В ОДИН ШАГ):

1. Распакуйте ВСЁ содержимое этого архива (папку BepInEx, файлы winhttp.dll
   и doorstop_config.ini) в корневую директорию игры Ostranauts:
   (например: Steam\\steamapps\\common\\Ostranauts\\)
   так, чтобы winhttp.dll оказался в одной папке с Ostranauts.exe.
2. Запустите игру.
3. В Главном меню появится интерактивный космонавт с флагом переключения языка
   и информационная панель со ссылкой на Boosty.
4. Приятной игры, капитан!

----------------------------------------------------------------------
О МОДИФИКАЦИИ:
- Полная адаптация и перевод интерфейса, терминологии и игровых механик.
- Естественная русская грамматика со склонениями имён, глаголов и местоимений.
- Модульные кириллические шрифты в высоком разрешении (SDF).
- Русифицированные экраны MFD, связи, реактора, создания персонажа и предметов.
- Возможность переключения языка на лету прямо из главного меню.
- Поддержка BepInEx 5 и BepInEx 6.

----------------------------------------------------------------------
БОРТОВОЙ МАНИФЕСТ - ТЕ, БЛАГОДАРЯ КОМУ МОД ВООБЩЕ СУЩЕСТВУЕТ:
• ШЕЙХ:
  Сергей Коршунов

• АДМИРАЛЫ:
  Миша Аверин, Towland

• КАПИТАНЫ:
  Gundyar, Сергей Примаков, Zurics Game

• ЮНГИ:
  GreyViS, Pavel Bezik, LunarGoat, jard, languin, Анна Плагиатор

Сердечная благодарность каждому члену экипажа за поддержку и веру в проект!

----------------------------------------------------------------------
ПОДДЕРЖКА АВТОРА:
Разработкой и поддержкой модификации занимается один человек.
Если вам нравится перевод - поддержите проект на Boosty:
👉 https://boosty.to/coreforgelabs

Здесь вы можете голосовать за новые проекты, сообщать о багах и получать ранние обновления.
Спасибо за вашу поддержку!
======================================================================
"""

def stage_variant(bep, display_ver, guide_text):
    """Assembles one ready-to-extract bundle for a single BepInEx flavour."""
    config, bepinex_src = VARIANTS[bep]
    bundle_name = f"OstraI18n_v{display_ver}_BepInEx{bep}"
    stage_dir = os.path.join(RELEASE_ROOT, bundle_name)
    if os.path.exists(stage_dir):
        shutil.rmtree(stage_dir)

    plugin_dest = os.path.join(stage_dir, "BepInEx", "plugins", "OstraI18n")
    os.makedirs(plugin_dest, exist_ok=True)

    # 1. BepInEx framework (Doorstop, winhttp.dll, core/)
    if not os.path.exists(bepinex_src):
        print(f"ERROR: BepInEx {bep} framework not found: {bepinex_src}")
        sys.exit(1)
    for item in os.listdir(bepinex_src):
        s_path = os.path.join(bepinex_src, item)
        d_path = os.path.join(stage_dir, item)
        if os.path.isdir(s_path):
            copy_filtered_tree(s_path, d_path)
        else:
            shutil.copy2(s_path, d_path)
    for junk in ("changelog.txt", "README.md", "README.txt"):
        junk_p = os.path.join(stage_dir, junk)
        if os.path.exists(junk_p):
            os.remove(junk_p)
    print(f"  + BepInEx {bep} framework (winhttp.dll, doorstop_config.ini, BepInEx/core/)")

    # 2. Mod DLLs and runtime dependencies - taken from THIS configuration's output
    core_bin = os.path.join(SCRIPT_DIR, "core", "OstraI18n.Core", "bin", config, "netstandard2.1")
    plugin_dll = os.path.join(SCRIPT_DIR, "plugin", "OstraI18n", "bin", config, "netstandard2.1", "OstraI18n.dll")
    if not os.path.exists(plugin_dll):
        print(f"ERROR: {plugin_dll} not found!")
        sys.exit(1)

    copied_dlls = []
    if os.path.exists(core_bin):
        for f in os.listdir(core_bin):
            if f.endswith(".dll"):
                shutil.copy2(os.path.join(core_bin, f), plugin_dest)
                copied_dlls.append(f)
    shutil.copy2(plugin_dll, plugin_dest)
    copied_dlls.append("OstraI18n.dll")
    print(f"  + {len(copied_dlls)} DLLs from {config} ({', '.join(copied_dlls)})")

    # 3. Catalog
    catalog_src = os.path.join(SCRIPT_DIR, "catalog")
    catalog_dest = os.path.join(plugin_dest, "catalog")
    if os.path.exists(catalog_src):
        copy_filtered_tree(catalog_src, catalog_dest)
        print(f"  + catalog/ ({len(os.listdir(catalog_dest))} files)")

    # 4. Langs
    langs_src = os.path.join(SCRIPT_DIR, "langs")
    langs_dest = os.path.join(plugin_dest, "langs")
    if os.path.exists(langs_src):
        copy_filtered_tree(langs_src, langs_dest)
        print("  + langs/ (languages.json, ru, en)")

    # 5. Docs
    with open(os.path.join(stage_dir, "ИНСТРУКЦИЯ_ПО_УСТАНОВКЕ.txt"), "w", encoding="utf-8") as f:
        f.write(guide_text)
    with open(os.path.join(stage_dir, "README.txt"), "w", encoding="utf-8") as f:
        f.write(guide_text)

    # 6. ZIP
    zip_path = os.path.join(RELEASE_ROOT, f"{bundle_name}.zip")
    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zipf:
        for root, dirs, files in os.walk(stage_dir):
            for file in files:
                full_p = os.path.join(root, file)
                rel_p = os.path.relpath(full_p, stage_dir)
                zipf.write(full_p, arcname=rel_p)

    zip_size_mb = os.path.getsize(zip_path) / (1024 * 1024)
    print(f"  = {zip_path} ({zip_size_mb:.2f} MB)")
    return stage_dir, zip_path, zip_size_mb

def build_release(version=None, only=None):
    if not version:
        version = get_plugin_version()

    display_ver = "2.2" if version in ("2.2.0", "2.2") else ("2.0" if version in ("2.0.0", "2.0") else version)
    targets = [only] if only in VARIANTS else ["6", "5"]

    print("=" * 65)
    print(f"  OstraI18n Release Builder - Version v{display_ver}")
    print(f"  Author: CFLabs (CoreForgeLabs)")
    print(f"  BepInEx targets: {', '.join(targets)}")
    print("=" * 65)

    # 1. Build both flavours (Core is a ProjectReference, so it follows the configuration)
    for i, bep in enumerate(targets, start=1):
        config = VARIANTS[bep][0]
        print(f"\n[{i}/{len(targets) + 1}] Building OstraI18n ({config}) for BepInEx {bep}...")
        run_cmd(f"dotnet build plugin/OstraI18n/OstraI18n.csproj -c {config}")

    # 2. Проверка качества шаблонов: релиз не должен молча увозить регрессию.
    #    Сканер сверяет каждую русскую строку с её английским оригиналом.
    print("")
    print("[qa] Проверка грамматики шаблонов...")
    qa_script = os.path.join(SCRIPT_DIR, "tools", "qa_scan_grammar.py")
    if os.path.exists(qa_script):
        res = subprocess.run([sys.executable, qa_script], cwd=SCRIPT_DIR,
                             capture_output=True, text=True, encoding="utf-8")
        for ln in [x for x in (res.stdout or "").splitlines() if x.strip()][-8:]:
            print("   " + ln)
    else:
        print("   сканер не найден, пропускаю")
    cov = os.path.join(SCRIPT_DIR, "tools", "qa_check_coverage.py")
    if os.path.exists(cov):
        res = subprocess.run([sys.executable, cov], cwd=SCRIPT_DIR,
                             capture_output=True, text=True, encoding="utf-8")
        for ln in [x for x in (res.stdout or "").splitlines() if x.strip()][:3]:
            print("   " + ln)

    # 2. Assemble + zip
    print(f"\n[{len(targets) + 1}/{len(targets) + 1}] Assembling release bundles in 'Релиз'...")
    os.makedirs(RELEASE_ROOT, exist_ok=True)
    guide_text = make_install_guide(display_ver)
    results = []
    for bep in targets:
        print(f"\n-- BepInEx {bep} --")
        results.append((bep,) + stage_variant(bep, display_ver, guide_text))

    print("\n" + "=" * 65)
    print("  СБОРКА РЕЛИЗА УСПЕШНО ЗАВЕРШЕНА!")
    print(f"  Папка релиза: {RELEASE_ROOT}")
    for bep, stage_dir, zip_path, size in results:
        print(f"  BepInEx {bep}: {os.path.basename(zip_path)} ({size:.2f} MB)")
    print("=" * 65)

if __name__ == "__main__":
    ver = None
    only = None
    for arg in sys.argv[1:]:
        a = arg.lstrip("-").lower()
        if a in ("5", "v5", "bepinex5"):
            only = "5"
        elif a in ("6", "v6", "bepinex6"):
            only = "6"
        else:
            ver = arg
    build_release(ver, only)
