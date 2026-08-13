# Ensemble retranslate — статус (обновлено 2026-08-13 08:13)

## Прогон
- Скрипт: `tools/ensemble_retranslate.py`, PID 21476, старт 05:35:42.
- Метод: a+b (2 независимых перевода Qwen) + c (третий Qwen-арбитр выбирает/синтезирует).
- Прогресс: **31/62** готово (последняя запись 08:06:58), инкрементально сохраняется в `lang_src/ensemble_results.json`.
- Темп: ~5–8 мин/запись (упирается в проксю qwen-api на :3089). При этом темпе оставшиеся 31 запись — ещё ~2.5–3 часа.
- Лог: `lang_src/ensemble_retranslate.log`.
- Процесс жив, не завис (не путать со вторым python-процессом PID 24392 — это отдельный `memory_manager.py`, не относится к прогону).

## Что осталось сделать (после завершения прогона)
1. Дождаться завершения всех 62/62 в `lang_src/ensemble_results.json`.
2. Просмотреть арбитражные (c) результаты — применить выбранные переводы в `langs/ru/data/interactions.json` и `conditions.json` (Class A: 50 записей person/gender agreement + падеж; Class B: 12 записей has-quality calque).
3. Прогнать `python tools/validate_content_overlay.py` — ожидается 0 ошибок.
4. Данные-only изменение — пересборка DLL не требуется, но передеплоить `langs/ru/data/*.json` в `F:\Games\Steam\steamapps\common\Ostranauts\BepInEx\plugins\OstraI18n\langs\ru\data\`.
5. Живая проверка через логи игры (0 exceptions, applied fields count не должен упасть) — БЕЗ ручных кликов в игре, только через `BepInEx\LogOutput.log`.
6. Закоммитить.

## Незавершённое из прошлых фаз (не блокирует, но не забыть)
- Фаза 7–9 архитектурного плана (`docs/superpowers/plans/2026-08-13-i18n-architecture-v2.md`): композиция строк через StringBuilder.Append (~521 место), непокрытые экраны (chargen, Crew Roster, Remote Nav Console, ATC/comms).
- Проверить остальные категории StreamingAssets на предмет непокрытых оверлеем полей (по образцу найденного ранее `strTutorialKey`-пробела).
