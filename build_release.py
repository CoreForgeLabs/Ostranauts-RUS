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
import io

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

GUIDE_SRC = os.path.join(SCRIPT_DIR, "readme", "Инструкция.txt")


def make_install_guide(display_ver):
    """Текст инструкции берём из readme/Инструкция.txt -- он правится руками и
    является источником правды. Встроенный вариант ниже нужен только как
    запасной, если файла нет: релиз не должен уехать вообще без инструкции.

    Подстановка версии делается, только если в файле явно стоит {version} --
    иначе текст копируется дословно, без сюрпризов для того, кто его писал."""
    if os.path.exists(GUIDE_SRC):
        with io.open(GUIDE_SRC, encoding="utf-8-sig") as f:
            text = f.read()
        if "{version}" in text:
            text = text.replace("{version}", display_ver)
        print(f"  + инструкция взята из {os.path.relpath(GUIDE_SRC, SCRIPT_DIR)}")
        return text
    print("  ! readme/Инструкция.txt не найден, использую встроенный текст")
    return f"""======================================================================
  OstraI18n - Полная русификация для Ostranauts (Версия v{display_ver})
  Автор модификации: CFLabs (CoreForgeLabs)
  Поддержка на Boosty: https://boosty.to/coreforgelabs
======================================================================

В АРХИВЕ ДВЕ ПАПКИ - НУЖНА ТОЛЬКО ОДНА:

- BepInEx_6 - рекомендуемый вариант.
- BepInEx_5 - если по какой-то причине не подошёл первый.

Ставьте ТОЛЬКО ОДНУ из них: две версии BepInEx одновременно не работают.
Если раньше стоял другой вариант, удалите старую папку BepInEx перед установкой.

ИНСТРУКЦИЯ ПО УСТАНОВКЕ (В ОДИН ШАГ):

1. Откройте выбранную папку (BepInEx_6 или BepInEx_5) и скопируйте ВСЁ ЕЁ
   СОДЕРЖИМОЕ - папку BepInEx, файлы winhttp.dll и doorstop_config.ini -
   в корневую директорию игры Ostranauts:
   (например: Steam\\steamapps\\common\\Ostranauts\\)
   так, чтобы winhttp.dll оказался в одной папке с Ostranauts.exe.
   Саму папку BepInEx_6 / BepInEx_5 копировать НЕ нужно - только её содержимое.
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

def check_doorstop_pairing(bep, stage_dir):
    """Doorstop 4 вызывает Doorstop.Entrypoint.Start() в целевой сборке.
    BepInEx 5.4.22 такой точки входа не имеет -- он рассчитан на Doorstop 3,
    и связка "ядро 5.4.22 + файлы Doorstop 4" не стартует МОЛЧА: winhttp
    грузится, лог не появляется, мод как будто не установлен. Проверяем пару
    здесь, а не в игре."""
    cfg = os.path.join(stage_dir, "doorstop_config.ini")
    if not os.path.exists(cfg):
        print("  ! doorstop_config.ini отсутствует")
        return
    with io.open(cfg, encoding="utf-8", errors="ignore") as f:
        text = f.read()
    doorstop4 = "target_assembly" in text          # 4.x: snake_case
    m = re.search(r"target_?[Aa]ssembly\s*=\s*(.+)", text)
    target = m.group(1).strip().replace("\\", os.sep) if m else ""
    dll = os.path.join(stage_dir, target)
    if not os.path.exists(dll):
        print("  ! целевая сборка не найдена: %s" % target)
        return
    with open(dll, "rb") as f:
        blob = f.read()
    has_entry = b"Doorstop" in blob
    if doorstop4 and not has_entry:
        print("  ! НЕСОВМЕСТИМО: конфиг от Doorstop 4, а в %s нет точки входа"
              " Doorstop.Start -- мод не запустится и не скажет об этом."
              % os.path.basename(target))
        sys.exit(1)
    if not doorstop4 and has_entry:
        print("  ! конфиг от Doorstop 3, а сборка ждёт Doorstop 4")
        sys.exit(1)
    print("  + Doorstop %s и точка входа совпадают" % ("4" if doorstop4 else "3"))


def stage_variant(bep, display_ver, bundle_dir):
    """Собирает один вариант BepInEx в подпапку общего бандла.

    Обе версии едут в ОДНОМ архиве отдельными папками: пользователь копирует
    содержимое нужной, а не выбирает между двумя загрузками."""
    config, bepinex_src = VARIANTS[bep]
    stage_dir = os.path.join(bundle_dir, f"BepInEx_{bep}")
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
    check_doorstop_pairing(bep, stage_dir)

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

    print(f"  = {os.path.relpath(stage_dir, RELEASE_ROOT)}")
    return stage_dir


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
    print("")
    print("[qa] Проверка грамматики шаблонов...")
    qa_script = os.path.join(SCRIPT_DIR, "tools", "qa_scan_grammar.py")
    if os.path.exists(qa_script):
        res = subprocess.run([sys.executable, qa_script], cwd=SCRIPT_DIR,
                             capture_output=True, text=True, encoding="utf-8")
        for ln in [x for x in (res.stdout or "").splitlines() if x.strip()][-8:]:
            print("   " + ln)
    cov = os.path.join(SCRIPT_DIR, "tools", "qa_check_coverage.py")
    if os.path.exists(cov):
        res = subprocess.run([sys.executable, cov], cwd=SCRIPT_DIR,
                             capture_output=True, text=True, encoding="utf-8")
        for ln in [x for x in (res.stdout or "").splitlines() if x.strip()][:3]:
            print("   " + ln)

    # 3. Сборка одного архива с обеими версиями
    print("")
    print(f"[{len(targets) + 1}/{len(targets) + 1}] Сборка архива в 'Релиз'...")
    os.makedirs(RELEASE_ROOT, exist_ok=True)
    bundle_name = f"OstraI18n_v{display_ver}"
    bundle_dir = os.path.join(RELEASE_ROOT, bundle_name)
    if os.path.exists(bundle_dir):
        shutil.rmtree(bundle_dir)
    os.makedirs(bundle_dir)

    for bep in targets:
        print("")
        print(f"-- BepInEx {bep} --")
        stage_variant(bep, display_ver, bundle_dir)

    guide_text = make_install_guide(display_ver)
    with open(os.path.join(bundle_dir, "ИНСТРУКЦИЯ_ПО_УСТАНОВКЕ.txt"), "w",
              encoding="utf-8") as f:
        f.write(guide_text)

    zip_path = os.path.join(RELEASE_ROOT, f"{bundle_name}.zip")
    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zipf:
        for root, dirs, files in os.walk(bundle_dir):
            for file in files:
                full_p = os.path.join(root, file)
                zipf.write(full_p, arcname=os.path.relpath(full_p, bundle_dir))
    size_mb = os.path.getsize(zip_path) / (1024 * 1024)

    print("")
    print("=" * 65)
    print("  СБОРКА РЕЛИЗА УСПЕШНО ЗАВЕРШЕНА!")
    print(f"  Архив: {zip_path} ({size_mb:.2f} MB)")
    print(f"  Внутри: {', '.join('BepInEx_' + b for b in targets)}"
          " + ИНСТРУКЦИЯ_ПО_УСТАНОВКЕ.txt")
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
