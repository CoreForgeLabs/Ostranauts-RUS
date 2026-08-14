#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
OstraI18n - Automatic Release Packaging Script
CFLabs (CoreForgeLabs)

Automates:
1. Compilation of OstraI18n.Core (Release)
2. Compilation of OstraI18n (Release)
3. Assembly of complete BepInEx/plugins/OstraI18n structure
4. Generation of installation guide and README
5. Creation of clean zip archive and unpacked distribution in 'Релиз' folder
"""

import os
import sys
import shutil
import zipfile
import re
import subprocess

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
RELEASE_ROOT = os.path.join(SCRIPT_DIR, "Релиз")

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

def build_release(version=None):
    if not version:
        version = get_plugin_version()
    
    display_ver = "2.0" if version in ("2.0.0", "2.0") else version
    print("=" * 65)
    print(f"  OstraI18n Release Builder - Version v{display_ver}")
    print(f"  Author: CFLabs (CoreForgeLabs)")
    print("=" * 65)

    # 1. Build projects
    print("\n[1/5] Building OstraI18n.Core (Release)...")
    run_cmd("dotnet build core/OstraI18n.Core/OstraI18n.Core.csproj -c Release")

    print("\n[2/5] Building OstraI18n (Release)...")
    run_cmd("dotnet build plugin/OstraI18n/OstraI18n.csproj -c Release")

    # 2. Prepare directories
    print("\n[3/5] Assembling release folder structure...")
    os.makedirs(RELEASE_ROOT, exist_ok=True)
    
    bundle_name = f"OstraI18n_v{display_ver}"
    stage_dir = os.path.join(RELEASE_ROOT, bundle_name)
    if os.path.exists(stage_dir):
        shutil.rmtree(stage_dir)
    
    plugin_dest = os.path.join(stage_dir, "BepInEx", "plugins", "OstraI18n")
    os.makedirs(plugin_dest, exist_ok=True)

    # 3. Copy complete BepInEx 6 Framework (Doorstop, winhttp.dll, core/)
    bepinex_src = os.path.join(SCRIPT_DIR, "bepinex6_be", "extracted")
    if os.path.exists(bepinex_src):
        for item in os.listdir(bepinex_src):
            s_path = os.path.join(bepinex_src, item)
            d_path = os.path.join(stage_dir, item)
            if os.path.isdir(s_path):
                copy_filtered_tree(s_path, d_path)
            else:
                shutil.copy2(s_path, d_path)
        print("  + Copied complete BepInEx 6 framework (winhttp.dll, doorstop_config.ini, BepInEx/core/)")

    # 4. Copy Mod DLLs and all runtime dependencies
    core_bin = os.path.join(SCRIPT_DIR, "core", "OstraI18n.Core", "bin", "Release", "netstandard2.1")
    plugin_dll = os.path.join(SCRIPT_DIR, "plugin", "OstraI18n", "bin", "Release", "netstandard2.1", "OstraI18n.dll")

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
    print(f"  + Copied {len(copied_dlls)} DLLs to plugin folder ({', '.join(copied_dlls)})")

    # 5. Copy catalog
    catalog_src = os.path.join(SCRIPT_DIR, "catalog")
    catalog_dest = os.path.join(plugin_dest, "catalog")
    if os.path.exists(catalog_src):
        copy_filtered_tree(catalog_src, catalog_dest)
        print(f"  + Copied catalog/ ({len(os.listdir(catalog_dest))} files)")

    # 6. Copy langs
    langs_src = os.path.join(SCRIPT_DIR, "langs")
    langs_dest = os.path.join(plugin_dest, "langs")
    if os.path.exists(langs_src):
        copy_filtered_tree(langs_src, langs_dest)
        print(f"  + Copied langs/ (languages.json, ru, en)")

    # 7. Create instructions & docs (for players)
    print("\n[4/5] Writing installation instructions & documentation...")
    
    install_guide_text = f"""======================================================================
  OstraI18n - Полная русификация для Ostranauts (Версия v{display_ver})
  Автор модификации: CFLabs (CoreForgeLabs)
  Поддержка на Boosty: https://boosty.to/coreforgelabs
======================================================================

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
    with open(os.path.join(stage_dir, "ИНСТРУКЦИЯ_ПО_УСТАНОВКЕ.txt"), "w", encoding="utf-8") as f:
        f.write(install_guide_text)

    with open(os.path.join(stage_dir, "README.txt"), "w", encoding="utf-8") as f:
        f.write(install_guide_text)

    # 7. Create ZIP archive
    print("\n[5/5] Creating release ZIP archive...")
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
    
    print("\n" + "=" * 65)
    print(f"  СБОРКА РЕЛИЗА УСПЕШНО ЗАВЕРШЕНА!")
    print(f"  Папка релиза: {stage_dir}")
    print(f"  Архив релиза: {zip_path} ({zip_size_mb:.2f} MB)")
    print("=" * 65)

if __name__ == "__main__":
    ver = sys.argv[1] if len(sys.argv) > 1 else None
    build_release(ver)
