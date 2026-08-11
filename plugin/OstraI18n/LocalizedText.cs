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
        }

        internal void Apply()
        {
            if (string.IsNullOrEmpty(Key)) return;
            var value = I18n.Get(Key);
            if (string.IsNullOrEmpty(value)) return;

            var tmp = GetComponent<TMP_Text>();
            if (tmp != null) { tmp.text = value; return; }

            var legacy = GetComponent<UnityEngine.UI.Text>();
            if (legacy != null) legacy.text = value;
        }
    }
}
