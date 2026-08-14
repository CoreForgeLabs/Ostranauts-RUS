using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OstraI18n
{
    public class MenuLanguageAstronaut : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public class LanguageEntry
        {
            public string Code;
            public string Name;
            public string DisplayName;
            public Sprite Sprite;
        }

        private static MenuLanguageAstronaut _instance;
        private static readonly List<LanguageEntry> _availableLanguages = new List<LanguageEntry>();
        private static string _pluginDir;

        private Image _imgAstronaut;
        private GameObject _goTooltip;
        private TMP_Text _txtTooltip;
        private RectTransform _rectTransform;
        private Vector3 _originalScale = Vector3.one;

        public static void Init(string pluginDir)
        {
            _pluginDir = pluginDir;
            DiscoverLanguages();
        }

        private static void DiscoverLanguages()
        {
            _availableLanguages.Clear();
            var langsDir = Path.Combine(_pluginDir, "langs");
            if (!Directory.Exists(langsDir)) return;

            foreach (var sub in Directory.GetDirectories(langsDir))
            {
                var folderName = Path.GetFileName(sub);
                var metaPath = Path.Combine(sub, "meta.json");
                string code = folderName;
                string name = folderName;
                string displayName = folderName;

                if (File.Exists(metaPath))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
                        if (doc.RootElement.TryGetProperty("code", out var cEl)) code = cEl.GetString();
                        if (doc.RootElement.TryGetProperty("name", out var nEl)) name = nEl.GetString();
                        if (doc.RootElement.TryGetProperty("description", out var dEl)) displayName = dEl.GetString();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning("[i18n] Error reading " + metaPath + ": " + ex.Message);
                    }
                }

                // Look for astronaut sprite
                Sprite sprite = null;
                var imagesDir = Path.Combine(sub, "images");
                var specificSprite = Path.Combine(imagesDir, "astronaut_" + code + ".png");
                var generalSprite = Path.Combine(imagesDir, "astronaut.png");

                // Fallback to ru/images or en/images if not in own folder
                if (!File.Exists(specificSprite))
                    specificSprite = Path.Combine(_pluginDir, "langs", "ru", "images", "astronaut_" + code + ".png");

                var targetPath = File.Exists(specificSprite) ? specificSprite : (File.Exists(generalSprite) ? generalSprite : null);
                if (targetPath != null)
                {
                    var tex = LoadTexture(targetPath);
                    if (tex != null)
                        sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }

                _availableLanguages.Add(new LanguageEntry
                {
                    Code = code,
                    Name = name,
                    DisplayName = !string.IsNullOrEmpty(displayName) ? displayName : name,
                    Sprite = sprite
                });

                Plugin.Log.LogInfo("[i18n] Discovered language pack: " + name + " (" + code + ")");
            }

            // Ensure Russian and English are available as fallback if directory scan was empty
            if (_availableLanguages.Count == 0)
            {
                _availableLanguages.Add(new LanguageEntry { Code = "ru", Name = "Russian", DisplayName = "Русский" });
                _availableLanguages.Add(new LanguageEntry { Code = "en", Name = "English", DisplayName = "English" });
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                ImageConversion.LoadImage(tex, bytes);
                return tex;
            }
            catch { return null; }
        }

        public static void AttachToMainMenu(MainMenu mm)
        {
            if (mm == null) return;
            try
            {
                if (_availableLanguages.Count == 0) DiscoverLanguages();

                var canvasField = AccessTools.Field(typeof(MainMenu), "_canvasScreen");
                var canvasTransform = canvasField != null ? canvasField.GetValue(mm) as Transform : null;
                if (canvasTransform == null)
                    canvasTransform = mm.transform.Find("Canvas") ?? mm.transform;

                var existing = canvasTransform.Find("OstraLanguageAstronaut");
                if (existing != null)
                {
                    var comp = existing.GetComponent<MenuLanguageAstronaut>();
                    if (comp != null) comp.UpdateVisuals();
                    return;
                }

                var rootGo = new GameObject("OstraLanguageAstronaut", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MenuLanguageAstronaut));
                rootGo.transform.SetParent(canvasTransform, false);

                var rt = rootGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(25f, 20f);
                rt.sizeDelta = new Vector2(160f, 153f);

                var compSelf = rootGo.GetComponent<MenuLanguageAstronaut>();
                compSelf.Setup(rootGo);
                Plugin.Log.LogInfo("[i18n] MenuLanguageAstronaut attached to MainMenu successfully!");

                // Attach author credits banner next to astronaut
                var creditsExisting = canvasTransform.Find("OstraAuthorCredits");
                if (creditsExisting != null)
                {
                    var cComp = creditsExisting.GetComponent<MenuAuthorCredits>();
                    if (cComp != null) cComp.UpdateText();
                }
                else
                {
                    var creditsGo = new GameObject("OstraAuthorCredits", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MenuAuthorCredits));
                    creditsGo.transform.SetParent(canvasTransform, false);

                    var cRt = creditsGo.GetComponent<RectTransform>();
                    cRt.anchorMin = new Vector2(0f, 0f);
                    cRt.anchorMax = new Vector2(0f, 0f);
                    cRt.pivot = new Vector2(0f, 0f);
                    cRt.anchoredPosition = new Vector2(195f, 16f);
                    cRt.sizeDelta = new Vector2(620f, 165f);

                    var cComp = creditsGo.GetComponent<MenuAuthorCredits>();
                    cComp.Setup();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] AttachToMainMenu failed: " + ex.Message);
            }
        }

        private void Setup(GameObject rootGo)
        {
            _instance = this;
            _rectTransform = GetComponent<RectTransform>();
            _imgAstronaut = GetComponent<Image>();
            _imgAstronaut.raycastTarget = true;

            _goTooltip = new GameObject("Tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _goTooltip.transform.SetParent(rootGo.transform, false);

            var tooltipRt = _goTooltip.GetComponent<RectTransform>();
            tooltipRt.anchorMin = new Vector2(0.5f, 1f);
            tooltipRt.anchorMax = new Vector2(0.5f, 1f);
            tooltipRt.pivot = new Vector2(0.5f, 0f);
            tooltipRt.anchoredPosition = new Vector2(0f, 8f);
            tooltipRt.sizeDelta = new Vector2(240f, 50f);

            var tooltipBg = _goTooltip.GetComponent<Image>();
            tooltipBg.color = new Color(0.05f, 0.08f, 0.12f, 0.94f);

            var txtGo = new GameObject("TxtTooltip", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(_goTooltip.transform, false);

            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(6f, 4f);
            txtRt.offsetMax = new Vector2(-6f, -4f);

            _txtTooltip = txtGo.GetComponent<TextMeshProUGUI>();
            _txtTooltip.alignment = TextAlignmentOptions.Center;
            _txtTooltip.fontSize = 12f;
            _txtTooltip.color = Color.white;

            UpdateVisuals();
            _goTooltip.SetActive(false);
        }

        public void UpdateVisuals()
        {
            var curLang = GetCurrentLanguageEntry();
            var nextLang = GetNextLanguageEntry();

            if (_imgAstronaut != null && curLang.Sprite != null)
            {
                _imgAstronaut.sprite = curLang.Sprite;
            }

            if (_txtTooltip != null)
            {
                var isRu = curLang.Code.Equals("ru", StringComparison.OrdinalIgnoreCase);
                if (isRu)
                {
                    _txtTooltip.text = $"<b>ЯЗЫК:</b> <color=#55ffff>{curLang.DisplayName.ToUpper()}</color>\n<size=80%><color=#aaaaaa>Кликните для смены на {nextLang.DisplayName}</color></size>";
                }
                else
                {
                    _txtTooltip.text = $"<b>LANGUAGE:</b> <color=#55ffff>{curLang.DisplayName.ToUpper()}</color>\n<size=80%><color=#aaaaaa>Click to switch to {nextLang.DisplayName}</color></size>";
                }
            }
        }

        private LanguageEntry GetCurrentLanguageEntry()
        {
            var curName = Plugin.Language.Value;
            foreach (var l in _availableLanguages)
            {
                if (l.Name.Equals(curName, StringComparison.OrdinalIgnoreCase) || l.Code.Equals(curName, StringComparison.OrdinalIgnoreCase))
                    return l;
            }
            return _availableLanguages[0];
        }

        private LanguageEntry GetNextLanguageEntry()
        {
            if (_availableLanguages.Count <= 1) return GetCurrentLanguageEntry();
            var cur = GetCurrentLanguageEntry();
            int idx = _availableLanguages.IndexOf(cur);
            int nextIdx = (idx + 1) % _availableLanguages.Count;
            return _availableLanguages[nextIdx];
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_rectTransform != null) _rectTransform.localScale = _originalScale * 1.08f;
            if (_goTooltip != null) _goTooltip.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_rectTransform != null) _rectTransform.localScale = _originalScale;
            if (_goTooltip != null) _goTooltip.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            SwitchToNextLanguage();
        }

        private void SwitchToNextLanguage()
        {
            try
            {
                var target = GetNextLanguageEntry();

                var configValue = target.Code.Equals("en", StringComparison.OrdinalIgnoreCase) ? "English" : target.Code;
                Plugin.Language.Value = configValue;
                Plugin.Instance.Config.Save();

                Plugin.Log.LogInfo("[i18n] Switched language to: " + target.DisplayName + " (code=" + target.Code + ", config=" + configValue + ")");

                // 1. Reload Grammar & LangPack
                LangPack.Load(Plugin.DataDir.Value, configValue, false);

                // 2. Reload I18n strings dictionary
                I18n.Init(Plugin.DataDir.Value, target.Code);

                // 3. Reload modular fonts for the new language
                FontManager.LoadFontsForLanguage(target.Code, Path.Combine(_pluginDir, "langs", target.Code));

                // 4. Reapply ContentOverlay & Name lists
                ContentOverlay.Reapply(Plugin.DataDir.Value, target.Code);

                // 5. Update ImagePatcher's active folder for the new language
                ImagePatcher.SetActiveLanguage(Plugin.DataDir.Value, target.Code);

                // 6. Play audio emitter
                AudioManager.am?.PlayAudioEmitter("ShipUIBtnNewGameIn", false);

                // 7. Update astronaut graphics & tooltip
                UpdateVisuals();

                // 8. Update credits banner text
                if (MenuAuthorCredits.Instance != null) MenuAuthorCredits.Instance.UpdateText();

                // 9. Reload Main Menu buttons directly in real-time
                var mm = MainMenu.staticref != null ? MainMenu.staticref : MainMenu.FindAnyObjectByType<MainMenu>();
                if (mm != null)
                {
                    ImagePatcher.ReloadButtonsLive(mm, target.Code);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[i18n] SwitchToNextLanguage failed: " + ex);
            }
        }
    }

    public class MenuAuthorCredits : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private const string BOOSTY_URL = "https://boosty.to/coreforgelabs";
        private Image _bgImage;
        private TMP_Text _txtContent;
        private GameObject _btnBoosty;
        private Image _btnImage;
        private TMP_Text _btnText;
        private static Sprite _panelSprite;
        private static Sprite _btnNormalSprite;
        private static Sprite _btnHoverSprite;

        public static MenuAuthorCredits Instance { get; private set; }

        public void Setup()
        {
            Instance = this;
            _bgImage = GetComponent<Image>();

            if (_panelSprite == null) _panelSprite = CreatePanelSprite();
            if (_btnNormalSprite == null) _btnNormalSprite = CreateButtonSprite(false);
            if (_btnHoverSprite == null) _btnHoverSprite = CreateButtonSprite(true);

            if (_bgImage != null)
            {
                _bgImage.sprite = _panelSprite;
                _bgImage.type = Image.Type.Sliced;
                _bgImage.color = Color.white;
                _bgImage.raycastTarget = true;
            }

            // Text container
            var txtGo = new GameObject("TxtCredits", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(transform, false);

            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = new Vector2(0f, 0f);
            txtRt.anchorMax = new Vector2(1f, 1f);
            txtRt.offsetMin = new Vector2(18f, 48f);
            txtRt.offsetMax = new Vector2(-18f, -12f);

            _txtContent = txtGo.GetComponent<TextMeshProUGUI>();
            _txtContent.alignment = TextAlignmentOptions.TopLeft;
            _txtContent.fontSize = 15f;
            _txtContent.color = Color.white;
            _txtContent.lineSpacing = 1.5f;
            FontFallback.EnsureCyrillicFont(_txtContent);

            // Boosty Action Button
            _btnBoosty = new GameObject("BtnBoosty", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _btnBoosty.transform.SetParent(transform, false);

            var btnRt = _btnBoosty.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0f, 0f);
            btnRt.anchorMax = new Vector2(1f, 0f);
            btnRt.pivot = new Vector2(0.5f, 0f);
            btnRt.anchoredPosition = new Vector2(0f, 10f);
            btnRt.sizeDelta = new Vector2(-36f, 32f);

            _btnImage = _btnBoosty.GetComponent<Image>();
            _btnImage.sprite = _btnNormalSprite;
            _btnImage.type = Image.Type.Sliced;
            _btnImage.color = Color.white;
            _btnImage.raycastTarget = false;

            var btnTxtGo = new GameObject("BtnText", typeof(RectTransform), typeof(TextMeshProUGUI));
            btnTxtGo.transform.SetParent(_btnBoosty.transform, false);

            var btnTxtRt = btnTxtGo.GetComponent<RectTransform>();
            btnTxtRt.anchorMin = Vector2.zero;
            btnTxtRt.anchorMax = Vector2.one;
            btnTxtRt.offsetMin = Vector2.zero;
            btnTxtRt.offsetMax = Vector2.zero;

            _btnText = btnTxtGo.GetComponent<TextMeshProUGUI>();
            _btnText.alignment = TextAlignmentOptions.Center;
            _btnText.fontSize = 14.5f;
            _btnText.fontStyle = FontStyles.Bold;
            _btnText.color = new Color(1f, 0.95f, 0.85f, 1f);
            FontFallback.EnsureCyrillicFont(_btnText);

            UpdateText();
        }

        public void UpdateText()
        {
            if (_txtContent == null) return;
            FontFallback.EnsureCyrillicFont(_txtContent);
            if (_btnText != null) FontFallback.EnsureCyrillicFont(_btnText);

            var isRu = string.Equals(Plugin.Language.Value, "ru", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Plugin.Language.Value, "Russian", StringComparison.OrdinalIgnoreCase);

            if (isRu)
            {
                _txtContent.text =
                    "<b><size=115%><color=#00e5ff>// CFLABS</color></size></b> <size=95%><color=#5588a0>- РУСИФИКАЦИЯ OSTRANAUTS</color></size>\n" +
                    "<color=#ffaa33><b>Подписывайся на Boosty!</b></color> <color=#d8e4f0>Мод живёт благодаря вашей поддержке.</color>\n" +
                    "<color=#a0b4c8>Голосуй за проекты, присылай баг-репорты - автор рад любой помощи :)</color>\n" +
                    "<size=92%><color=#758a9e><i>P.S. Разработкой модификации занимается один человек. Удачи, капитан!</i></color></size>";

                if (_btnText != null)
                    _btnText.text = "[ >> ПОДДЕРЖАТЬ НА BOOSTY | boosty.to/coreforgelabs << ]";
            }
            else
            {
                _txtContent.text =
                    "<b><size=115%><color=#00e5ff>// CFLABS</color></size></b> <size=95%><color=#5588a0>- OSTRANAUTS TRANSLATION</color></size>\n" +
                    "<color=#ffaa33><b>Support on Boosty!</b></color> <color=#d8e4f0>The mod lives thanks to your support.</color>\n" +
                    "<color=#a0b4c8>Vote for projects, report bugs, and share your thoughts with author :)</color>\n" +
                    "<size=92%><color=#758a9e><i>P.S. Developed with care by a solo creator. Good luck, Captain!</i></color></size>";

                if (_btnText != null)
                    _btnText.text = "[ >> SUPPORT ON BOOSTY | boosty.to/coreforgelabs << ]";
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_btnImage != null) _btnImage.sprite = _btnHoverSprite;
            if (_bgImage != null) _bgImage.color = new Color(1.1f, 1.15f, 1.25f, 1f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_btnImage != null) _btnImage.sprite = _btnNormalSprite;
            if (_bgImage != null) _bgImage.color = Color.white;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            try
            {
                AudioManager.am?.PlayAudioEmitter("ShipUIBtnNewGameIn", false);
                Application.OpenURL(BOOSTY_URL);
                Plugin.Log.LogInfo("[i18n] Opening Boosty link: " + BOOSTY_URL);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] Failed to open URL: " + ex.Message);
            }
        }

        private static Sprite CreatePanelSprite()
        {
            int w = 48;
            int h = 48;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[w * h];
            var bg = new Color32(6, 12, 20, 235);
            var border = new Color32(0, 140, 185, 175);
            var corner = new Color32(0, 230, 255, 255);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isBorder = (x == 0 || x == w - 1 || y == 0 || y == h - 1);
                    bool isCorner = ((x < 5 || x >= w - 5) && (y < 5 || y >= h - 5)) && isBorder;
                    bool isInnerCorner = ((x == 1 || x == w - 2) && (y < 4 || y >= h - 4)) ||
                                         ((y == 1 || y == h - 2) && (x < 4 || x >= w - 4));

                    if (isCorner || isInnerCorner)
                        pixels[y * w + x] = corner;
                    else if (isBorder)
                        pixels[y * w + x] = border;
                    else
                        pixels[y * w + x] = bg;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(6, 6, 6, 6));
        }

        private static Sprite CreateButtonSprite(bool hover)
        {
            int w = 32;
            int h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[w * h];
            var bg = hover ? new Color32(230, 125, 25, 245) : new Color32(165, 80, 12, 220);
            var border = hover ? new Color32(255, 220, 120, 255) : new Color32(220, 130, 35, 220);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool isBorder = (x == 0 || x == w - 1 || y == 0 || y == h - 1);
                    pixels[y * w + x] = isBorder ? border : bg;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(3, 3, 3, 3));
        }
    }
}
