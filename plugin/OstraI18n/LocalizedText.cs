using System.Collections;
using TMPro;
using UnityEngine;

namespace OstraI18n
{
    /// Вешается в рантайме на объект, найденный PrefabBinder-ом по пути из каталога.
    /// Не хранит и не сравнивает текст — только ключ. Поэтому не может повторить
    /// баги Фазы 1 (обрывок анимации, совпадение коротких слов): единственный
    /// источник истины — путь, зафиксированный один раз при привязке.
    public class LocalizedText : MonoBehaviour
    {
        public string Key;

        private void OnEnable()
        {
            Apply();
            // Компонент часто добавляется к уже активному объекту (PrefabBinder находит
            // его через поллинг уже после инстанцирования) — собственный Start() объекта
            // может выполниться ПОСЛЕ нашего OnEnable в том же кадре и перезаписать text
            // родным значением. Повторное применение в конце кадра переживает эту гонку.
            StartCoroutine(ApplyAtEndOfFrame());
        }

        private IEnumerator ApplyAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            Apply();
        }

        internal static string OverflowReportPath;

        internal void Apply()
        {
            if (string.IsNullOrEmpty(Key)) return;
            var value = I18n.Get(Key);
            if (string.IsNullOrEmpty(value)) return;

            var tmp = GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = value;
                if (I18n.QaMode) CheckOverflow(tmp, value);
                return;
            }

            var legacy = GetComponent<UnityEngine.UI.Text>();
            if (legacy != null) legacy.text = value;
        }

        private void CheckOverflow(TMP_Text tmp, string value)
        {
            try
            {
                var rect = tmp.rectTransform.rect;
                var preferred = tmp.GetPreferredValues(value, rect.width > 0 ? rect.width : 10000f, 0f);
                bool overflowsWidth = rect.width > 0 && preferred.x > rect.width * 1.02f;
                bool overflowsHeight = rect.height > 0 && preferred.y > rect.height * 1.02f;
                if (!overflowsWidth && !overflowsHeight) return;

                var line = Key + "\t" + GetPath(transform) + "\tширина " + preferred.x.ToString("F0") +
                           "/" + rect.width.ToString("F0") + "\tвысота " + preferred.y.ToString("F0") +
                           "/" + rect.height.ToString("F0");
                if (!string.IsNullOrEmpty(OverflowReportPath))
                    System.IO.File.AppendAllText(OverflowReportPath, line + "\n");
            }
            catch (System.Exception) { /* детектор не должен ронять игру */ }
        }

        private static string GetPath(Transform t)
        {
            var segs = new System.Collections.Generic.List<string>();
            for (var cur = t; cur != null; cur = cur.parent) segs.Insert(0, cur.name);
            return string.Join("/", segs);
        }
    }
}
