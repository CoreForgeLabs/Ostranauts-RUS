<p align="center">
  <img src="workshop/OstraI18n/preview.png" alt="OstraI18n Logo" width="280"/>
</p>

# OstraI18n — Полная русификация Ostranauts (v2.0)

[![Game: Ostranauts](https://img.shields.io/badge/Game-Ostranauts-00e5ff?style=flat-square)](https://store.steampowered.com/app/1020210/Ostranauts/)
[![Framework: BepInEx 6](https://img.shields.io/badge/Framework-BepInEx_6-green?style=flat-square)](https://github.com/BepInEx/BepInEx)
[![Version: 2.0](https://img.shields.io/badge/Version-v2.0-orange?style=flat-square)](https://github.com/CoreForgeLabs/Ostranauts_i18n/releases)
[![Boosty](https://img.shields.io/badge/Поддержка-Boosty-red?style=flat-square)](https://boosty.to/coreforgelabs)

Комплексный русификатор и система локализации для космического симулятора **Ostranauts**.  
Полный перевод интерфейса, диалогов, MFD-терминалов, реактора, нормальная русская грамматика со склонениями и чёткие шрифты.

---

### ⚠️ Важно: где брать актуальную версию
Из-за того, что поисковики индексируют прямые ссылки на старые релизы, игроки часто качают устаревшие версии и ловят баги.  
Все актуальные и проверенные сборки публикуются на **Boosty** (всегда в один клик и последней версии).

👉 **Скачать актуальный перевод:** [boosty.to/coreforgelabs](https://boosty.to/coreforgelabs)  
*(Исходный код мода и движка локализации открыт и лежит в этом репозитории).*

---

## ❤️ Поддержка проекта

**Made with love by [@CoreForgeLabs](https://t.me/CoreForgeLabs)**  
*Telegram · Discord*

Это одна из моих любимых игр, и я искренне хочу развивать наше сообщество.  
Ваша поддержка — это не просто финансовая помощь. Это мотивация продолжать разработку и уверенность, что проект кому-то действительно важен.

| Способ | Реквизиты / Ссылка |
|:---|:---|
| **Boosty** | [boosty.to/coreforgelabs](https://boosty.to/coreforgelabs) |
| **Т-Банк** | `2200 7013 8955 0366` |
| **BTC** | `bc1qjzw4nz6y0dl3pvy8v46j70yywsh4l78sg0eq3x` |
| **ETH / USDT / USDC (ERC-20)** | `0xc9B7c16ef301E6277BbEB28C9AfCEC7c107d244E` |

**Помимо модов:**  
🤖 Telegram/Discord боты • ⚙️ Автоматизация • 🔗 Интеграции • 🌍 Переводы игр  
*Пишите — отвечу всем! :)*

---

## 🎖️ Благодарности экипажу (Boosty)

Огромное спасибо парням за поддержку проекта:

- **Шейх:** Сергей Коршунов
- **Адмиралы:** Миша Аверин, Towland
- **Капитаны:** Gundyar, Сергей Примаков, Zurics Game
- **Юнга:** GreyViS, Pavel Bezik, LunarGoat, jard, languin, Анна Плагиатор

---

## 🔧 Что входит в версию 2.0

| Компонент | Описание |
|:---|:---|
| **Интерфейс и терминалы** | Главное меню, создание персонажа, MFD-экраны, радар, навигация, PDA и инвентарь |
| **Связь и переговоры** | Полный перевод системы Comms, диалогов со станциями, полицией и другими кораблями |
| **Реактор и корабли** | Управление термоядерным реактором (Fusion IC), биржа кораблей, компоненты и ремонт |
| **Грамматический движок** | Динамические склонения русских имён, глаголов (1-е/2-е/3-е лицо), без сломанных артиклей |
| **Шрифты TextMeshPro** | Чёткий кириллический шрифт `Jura` (SDF) высокого разрешения, не мылит на любых экранах |
| **Переключение языка** | Интерактивный космонавт в Главном меню — смена языка (RU / EN) в один клик без перезапуска |

---

## 📦 Установка

1. Скачайте архив **`OstraI18n_v2.0.zip`** (на [Boosty](https://boosty.to/coreforgelabs) или в [Releases](https://github.com/CoreForgeLabs/Ostranauts_i18n/releases)).
2. Распакуйте **всё содержимое архива** в корневую папку игры:
   ```
   ...\Steam\steamapps\common\Ostranauts\
   ```
   *(файлы `winhttp.dll` и `doorstop_config.ini` должны оказаться в одной папке с `Ostranauts.exe`)*
3. Запустите игру через Steam. **Готово!**

---

## 🛠️ Сборка из исходников

Для разработчиков и моддеров:

```bash
# Сборка плагина и ядра (.NET 8 SDK / C#)
dotnet build core/OstraI18n.Core/OstraI18n.Core.csproj -c Release
dotnet build plugin/OstraI18n/OstraI18n.csproj -c Release

# Сборка готового архива релиза
python build_release.py
```

---

© 2026 **CoreForgeLabs**
