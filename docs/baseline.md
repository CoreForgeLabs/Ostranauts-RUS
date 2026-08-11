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

## Task 3 — извлечение литералов (CatalogExtract)

```
методов с выводом текста: 449
литералов всего: 2708
кандидатов в UI-текст: 1762
```

Контрольная запись `"At Large"` найдена: `methodKey = GUIChargenCareer::PageListCareers/0`,
совпадает с методом, пропатченным в Task 1 вручную (`GUIChargenCareer.PageListCareers`).

**Известная проблема фильтра `LooksLikeUiText`:** в случайной выборке 30 кандидатов
преобладали НЕ человекочитаемые фразы, а идентификаторы (`strPIN`,
`ShipUIBtnSuppliesAcceptNeg`, `IsRoom`, `Self`, `F2`) и уже готовые ключи локализации
(`GUI_TRADE_ERROR_NO_SPACE_3` — это аргумент существующего `GetString`, а не текст для
перевода). План не ставит здесь жёсткого гейта, и апстрим-защита есть: транспайлер
(Task 7/8) применяет только записи с `approved: true`, для Фазы 1 массовое утверждение
безопасно (мусорные записи самопереводятся в себя). Ужесточение фильтра — отдельная
задача перед началом реального перевода каталога, вне рамок Фазы 1.
