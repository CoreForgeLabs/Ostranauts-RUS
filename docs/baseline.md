# Baseline перед переходом на key-based i18n

Дата: 2026-08-11

Состояние до начала Фазы 1 (литералы). Плагин на текущий момент использует
рантайм-подмену по английскому тексту (`GuiText.Translate`) — тот механизм,
который эта фаза заменяет.

## Лог запуска (RUNSAVE.bat, автозагрузка сейва)

```
[Info   : OstraI18n] [i18n] pack Russian [lang_ru]: 8 pronoun cats, 176 verbs, 1179 strings, 1829 gui
[Info   : OstraI18n] [i18n] OstraI18n 0.1.3: 12 patches ok, 0 failed/skipped, lang=Russian
```

Крашей в `Player.log` (`OUTPUTTING STACK TRACE`) нет.

## Хеш сборки игры на момент старта работ

b6bb6d0633b0078944c7d345d9dc03d998b9d5d2 */f/Games/Steam/steamapps/common/Ostranauts/Ostranauts_Data/Managed/Assembly-CSharp.dll
