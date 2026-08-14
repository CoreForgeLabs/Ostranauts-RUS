# OstraI18n — Полная русификация Ostranauts (v2.0)

<p align="center">
  <img src="workshop/OstraI18n/preview.png" alt="OstraI18n Logo" width="360"/>
</p>

<p align="center">
  <b>Комплексная модификация полной русификации для космического симулятора <a href="https://store.steampowered.com/app/1020210/Ostranauts/">Ostranauts</a></b><br>
  Разработано <b>CFLabs (CoreForgeLabs)</b>
</p>

<p align="center">
  <a href="https://boosty.to/coreforgelabs"><img src="https://img.shields.io/badge/Boosty-Поддержать_автора-orange?style=for-the-badge&logo=boosty" alt="Boosty"/></a>
  <a href="https://github.com/CoreForgeLabs/Ostranauts_i18n/releases"><img src="https://img.shields.io/badge/Версия-v2.0-blue?style=for-the-badge" alt="Version"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/Лицензия-MIT-green?style=for-the-badge" alt="License"/></a>
</p>

---

## 🌟 Особенности модификации

- 🚀 **100% перевод интерфейса и механик:**
  - Главное меню, создание персонажа, истории жизни и навыки.
  - Бортовые экраны MFD (многофункциональные дисплеи), терминалы и радары.
  - Система радиосвязи (Comms) и переговоры со станциями и кораблями.
  - Панель управления термоядерным реактором (Fusion Reactor IC).
  - Биржа кораблей (Ship Broker), магазины, контракты и инвентарь.
  - Всплывающие подсказки (MegaTooltip) и диалоговые окна.

- 🧠 **Грамматический движок склонений:**
  - Динамическое спряжение глаголов по лицам (1-е, 2-е и 3-е лицо: *«Ты собираешься...»* / *«Доркас Гулд собирается...»*).
  - Падежные окончания для сгенерированных имён NPC, предметов и помещений.

- 🔤 **Модульные кириллические SDF-шрифты:**
  - Чёткий, масштабируемый кириллический шрифт `Jura` высокого разрешения (TextMeshPro Signed Distance Field), исключающий размытие на любых разрешениях.

- 🌐 **Мгновенное переключение языка на лету:**
  - Интерактивный космонавт в Главном меню позволяет переключаться между русским и английским языками в один клик без перезапуска игры.

- 🎖️ **Бортовой манифест экипажа:**
  - Встроенное интерактивное окно с благодарностями всем, кто поддерживает модификацию на Boosty!

---

## 📥 Установка для игроков

1. Перейдите в раздел **[Releases](https://github.com/CoreForgeLabs/Ostranauts_i18n/releases)** и скачайте архив **`OstraI18n_v2.0.zip`**.
2. Распакуйте **всё содержимое архива** в корневую директорию игры *Ostranauts*:
   > `.../Steam/steamapps/common/Ostranauts/` *(так, чтобы файл `winhttp.dll` оказался рядом с `Ostranauts.exe`)*
3. Запустите игру. Приятных космических полётов, капитан!

---

## 🛠️ Структура репозитория и сборка из исходников

```
├── core/                  # Исходный код ядра OstraI18n.Core (C# .NET Standard 2.1)
├── plugin/                # Исходный код плагина OstraI18n для BepInEx 6 (Unity Mono)
├── catalog/               # Каталоги префабов и утверждённых литералов
├── langs/                 # Языковые пакеты (ru, en: грамматика, шрифты, текстуры, json-данные)
├── workshop/              # Файлы для Мастерской Steam (обложка preview.png, mod_info.json)
├── Релиз/                 # Готовые дистрибутивы и скрипты упаковки
├── build_release.py       # Скрипт автоматической компиляции и сборки релиза
└── build_release.bat      # Батник для сборки релиза в 1 клик
```

### Сборка проекта:
Требуется **.NET SDK 8.0+** и среда разработки (Visual Studio / JetBrains Rider / VS Code).

```bash
# Сборка ядра и плагина
dotnet build core/OstraI18n.Core/OstraI18n.Core.csproj -c Release
dotnet build plugin/OstraI18n/OstraI18n.csproj -c Release

# Автоматическая упаковка полного релиза
python build_release.py
```

---

## ❤️ Благодарности экипажу (Boosty)

*Модификация живёт и развивается благодаря поддержке нашего замечательного сообщества:*

- 👑 **ШЕЙХ:** Сергей Коршунов
- 🎖️ **АДМИРАЛЫ:** Миша Аверин, Towland
- 🚀 **КАПИТАНЫ:** Gundyar, Сергей Примаков, Zurics Game
- ⚓ **ЮНГИ:** GreyViS, Pavel Bezik, LunarGoat, jard, languin, Анна Плагиатор

---

## ☕ Поддержка автора

Разработкой и развитием модификации занимается один человек. Если вам нравится русификатор — вы можете поддержать автора, голосовать за следующие проекты и первыми получать обновления:

👉 **[https://boosty.to/coreforgelabs](https://boosty.to/coreforgelabs)**
