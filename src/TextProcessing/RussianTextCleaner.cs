using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BepInEx.Logging;

namespace OstranautsRusPatch.TextProcessing
{
    /// <summary>
    /// Центральный процессор очистки. Вызывает модули по очереди.
    /// </summary>
    public static class RussianTextCleaner
    {
        private static ManualLogSource _logger;
        private static readonly LRUCache<string, string> _cache = new LRUCache<string, string>(2000);

#if DEBUG
        private static long _cacheHits = 0;
        private static long _cacheMisses = 0;
        private static long _totalCalls = 0;
        private static Stopwatch _sw = new Stopwatch();
        private static double _totalMs = 0.0;
        private const int DIAG_INTERVAL = 500;
#endif

        public static void SetLogger(ManualLogSource logger)
        {
            _logger = logger;
            // Прокидываем логгер в модули
            ExactLookup.SetLogger(logger);
            CommonFixLookup.SetLogger(logger);
            NameGenderLookup.SetLogger(logger);
            GrammarRules.SetLogger(logger);
            DebugPatches.SetLogger(logger);
        }

        /// <summary>Главный пайплайн очистки текста. Вызывает все модули по порядку.</summary>
        public static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return raw;

            // Кэш первого уровня — быстрый возврат
            if (_cache.TryGetValue(raw, out var cached))
                return cached;

            try
            {
#if DEBUG
                _totalCalls++;
                _sw.Restart();
#endif

                var result = raw;
                result = ExactLookup.Apply(result);        // 1. Точный словарь
                result = CommonFixLookup.Apply(result);     // 2. Быстрые замены
                result = NameGenderLookup.Apply(result);    // 3. Коррекция местоимений
                result = GrammarRules.Apply(result);        // 4. Грамматика (склонения)
                result = DebugPatches.Apply(result);        // 5. Отладочные замены
                result = PostProcess.Apply(result);         // 6. Финальная чистка

                // Сохраняем в кэш
                _cache.Add(raw, result);

#if DEBUG
                _totalMs += _sw.Elapsed.TotalMilliseconds;
                if (_totalCalls % DIAG_INTERVAL == 0)
                {
                    double avgMs = _totalMs / _totalCalls;
                    _logger?.LogDebug($"[Cleaner] Calls: {_totalCalls}, Avg time: {avgMs:F3}ms, Cache size: {_cache.Count}");
                }
#endif

                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"RussianTextCleaner.Clean crashed: {ex.Message}");
                _cache.Add(raw, raw); // кэшируем даже падение, чтобы не повторять
                return raw;
            }
        }

        /// <summary>Очистить кэш (например, после загрузки нового JSON).</summary>
        public static void InvalidateCache()
        {
            _cache.Clear();
#if DEBUG
            _cacheHits = 0;
            _cacheMisses = 0;
            _totalCalls = 0;
            _totalMs = 0.0;
#endif
        }
    }

    /// <summary>
    /// LRU-кэш фиксированного размера. При переполнении удаляет самые старые записи.
    /// </summary>
    public class LRUCache<TKey, TValue>
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _dict;
        private readonly LinkedList<KeyValuePair<TKey, TValue>> _list;

        public LRUCache(int capacity)
        {
            _capacity = capacity;
            _dict = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
            _list = new LinkedList<KeyValuePair<TKey, TValue>>();
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            if (_dict.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default;
            return false;
        }

        public void Add(TKey key, TValue value)
        {
            if (_dict.TryGetValue(key, out var node))
            {
                _list.Remove(node);
            }
            else if (_dict.Count >= _capacity)
            {
                var last = _list.Last;
                _dict.Remove(last.Value.Key);
                _list.RemoveLast();
            }
            var newNode = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
            _list.AddFirst(newNode);
            _dict[key] = newNode;
        }

        public void Clear()
        {
            _dict.Clear();
            _list.Clear();
        }

        public int Count => _dict.Count;
    }
}