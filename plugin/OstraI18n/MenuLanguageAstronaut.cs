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
            var curCode = LangPack.Code;
            var curName = Plugin.Language != null ? Plugin.Language.Value : "ru";

            if (!string.IsNullOrEmpty(curCode))
            {
                foreach (var l in _availableLanguages)
                {
                    if (l.Code.Equals(curCode, StringComparison.OrdinalIgnoreCase))
                        return l;
                }
            }

            foreach (var l in _availableLanguages)
            {
                if (l.Code.Equals(curName, StringComparison.OrdinalIgnoreCase) ||
                    l.Name.Equals(curName, StringComparison.OrdinalIgnoreCase) ||
                    l.DisplayName.Equals(curName, StringComparison.OrdinalIgnoreCase) ||
                    (curName.StartsWith("ru", StringComparison.OrdinalIgnoreCase) && l.Code.Equals("ru", StringComparison.OrdinalIgnoreCase)) ||
                    (curName.StartsWith("en", StringComparison.OrdinalIgnoreCase) && l.Code.Equals("en", StringComparison.OrdinalIgnoreCase)))
                {
                    return l;
                }
            }

            foreach (var l in _availableLanguages)
            {
                if (l.Code.Equals("ru", StringComparison.OrdinalIgnoreCase))
                    return l;
            }

            return _availableLanguages.Count > 0 ? _availableLanguages[0] : new LanguageEntry { Code = "ru", Name = "Russian", DisplayName = "Русский" };
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

    public class MenuSimpleButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action OnClick;
        public Image TargetImage;
        public Sprite NormalSprite;
        public Sprite HoverSprite;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (TargetImage != null && HoverSprite != null) TargetImage.sprite = HoverSprite;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (TargetImage != null && NormalSprite != null) TargetImage.sprite = NormalSprite;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            AudioManager.am?.PlayAudioEmitter("ShipUIBtnNewGameIn", false);
            OnClick?.Invoke();
        }
    }

    public class MenuAuthorCredits : MonoBehaviour
    {
        private const string BOOSTY_URL = "https://boosty.to/coreforgelabs";
        private Image _bgImage;
        private TMP_Text _txtContent;
        private GameObject _btnBoostyGo;
        private GameObject _btnCrewGo;
        private TMP_Text _btnBoostyText;
        private TMP_Text _btnCrewText;
        private static Sprite _panelSprite;
        private static Sprite _btnOrangeNormal;
        private static Sprite _btnOrangeHover;
        private static Sprite _btnCyanNormal;
        private static Sprite _btnCyanHover;

        public static MenuAuthorCredits Instance { get; private set; }

        public static Sprite GetPanelSprite() => _panelSprite ?? (_panelSprite = CreatePanelSprite());
        public static Sprite GetCyanNormalSprite() => _btnCyanNormal ?? (_btnCyanNormal = CreateButtonSprite(false, new Color32(10, 65, 95, 220), new Color32(0, 160, 210, 220)));
        public static Sprite GetCyanHoverSprite() => _btnCyanHover ?? (_btnCyanHover = CreateButtonSprite(true, new Color32(15, 105, 150, 245), new Color32(0, 230, 255, 255)));

        public void Setup()
        {
            Instance = this;
            _bgImage = GetComponent<Image>();

            if (_panelSprite == null) _panelSprite = CreatePanelSprite();
            if (_btnOrangeNormal == null) _btnOrangeNormal = CreateButtonSprite(false, new Color32(165, 80, 12, 220), new Color32(220, 130, 35, 220));
            if (_btnOrangeHover == null) _btnOrangeHover = CreateButtonSprite(true, new Color32(230, 125, 25, 245), new Color32(255, 220, 120, 255));
            if (_btnCyanNormal == null) _btnCyanNormal = CreateButtonSprite(false, new Color32(10, 65, 95, 220), new Color32(0, 160, 210, 220));
            if (_btnCyanHover == null) _btnCyanHover = CreateButtonSprite(true, new Color32(15, 105, 150, 245), new Color32(0, 230, 255, 255));

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

            // Button 1: Boosty (Left)
            _btnBoostyGo = new GameObject("BtnBoosty", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MenuSimpleButton));
            _btnBoostyGo.transform.SetParent(transform, false);

            var btn1Rt = _btnBoostyGo.GetComponent<RectTransform>();
            btn1Rt.anchorMin = new Vector2(0f, 0f);
            btn1Rt.anchorMax = new Vector2(0.60f, 0f);
            btn1Rt.pivot = new Vector2(0f, 0f);
            btn1Rt.anchoredPosition = new Vector2(16f, 10f);
            btn1Rt.sizeDelta = new Vector2(-22f, 32f);

            var btn1Img = _btnBoostyGo.GetComponent<Image>();
            btn1Img.sprite = _btnOrangeNormal;
            btn1Img.type = Image.Type.Sliced;
            btn1Img.color = Color.white;

            var btn1Comp = _btnBoostyGo.GetComponent<MenuSimpleButton>();
            btn1Comp.TargetImage = btn1Img;
            btn1Comp.NormalSprite = _btnOrangeNormal;
            btn1Comp.HoverSprite = _btnOrangeHover;
            btn1Comp.OnClick = () => Application.OpenURL(BOOSTY_URL);

            var btn1TxtGo = new GameObject("BtnText", typeof(RectTransform), typeof(TextMeshProUGUI));
            btn1TxtGo.transform.SetParent(_btnBoostyGo.transform, false);
            var btn1TxtRt = btn1TxtGo.GetComponent<RectTransform>();
            btn1TxtRt.anchorMin = Vector2.zero;
            btn1TxtRt.anchorMax = Vector2.one;
            btn1TxtRt.offsetMin = Vector2.zero;
            btn1TxtRt.offsetMax = Vector2.zero;
            _btnBoostyText = btn1TxtGo.GetComponent<TextMeshProUGUI>();
            _btnBoostyText.alignment = TextAlignmentOptions.Center;
            _btnBoostyText.fontSize = 13.5f;
            _btnBoostyText.fontStyle = FontStyles.Bold;
            _btnBoostyText.color = new Color(1f, 0.95f, 0.85f, 1f);
            _btnBoostyText.richText = false;
            _btnBoostyText.enableWordWrapping = false;
            _btnBoostyText.overflowMode = TextOverflowModes.Overflow;
            FontFallback.EnsureCyrillicFont(_btnBoostyText);

            // Button 2: Crew Manifest (Right)
            _btnCrewGo = new GameObject("BtnCrew", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MenuSimpleButton));
            _btnCrewGo.transform.SetParent(transform, false);

            var btn2Rt = _btnCrewGo.GetComponent<RectTransform>();
            btn2Rt.anchorMin = new Vector2(0.60f, 0f);
            btn2Rt.anchorMax = new Vector2(1f, 0f);
            btn2Rt.pivot = new Vector2(0f, 0f);
            btn2Rt.anchoredPosition = new Vector2(6f, 10f);
            btn2Rt.sizeDelta = new Vector2(-22f, 32f);

            var btn2Img = _btnCrewGo.GetComponent<Image>();
            btn2Img.sprite = _btnCyanNormal;
            btn2Img.type = Image.Type.Sliced;
            btn2Img.color = Color.white;

            var btn2Comp = _btnCrewGo.GetComponent<MenuSimpleButton>();
            btn2Comp.TargetImage = btn2Img;
            btn2Comp.NormalSprite = _btnCyanNormal;
            btn2Comp.HoverSprite = _btnCyanHover;
            btn2Comp.OnClick = () => MenuCrewManifestModal.Toggle(transform.parent);

            var btn2TxtGo = new GameObject("BtnText", typeof(RectTransform), typeof(TextMeshProUGUI));
            btn2TxtGo.transform.SetParent(_btnCrewGo.transform, false);
            var btn2TxtRt = btn2TxtGo.GetComponent<RectTransform>();
            btn2TxtRt.anchorMin = Vector2.zero;
            btn2TxtRt.anchorMax = Vector2.one;
            btn2TxtRt.offsetMin = Vector2.zero;
            btn2TxtRt.offsetMax = Vector2.zero;
            _btnCrewText = btn2TxtGo.GetComponent<TextMeshProUGUI>();
            _btnCrewText.alignment = TextAlignmentOptions.Center;
            _btnCrewText.fontSize = 13.5f;
            _btnCrewText.fontStyle = FontStyles.Bold;
            _btnCrewText.color = new Color(0.85f, 0.96f, 1f, 1f);
            _btnCrewText.richText = false;
            _btnCrewText.enableWordWrapping = false;
            _btnCrewText.overflowMode = TextOverflowModes.Overflow;
            FontFallback.EnsureCyrillicFont(_btnCrewText);

            UpdateText();
        }

        public void UpdateText()
        {
            if (_txtContent == null) return;
            FontFallback.EnsureCyrillicFont(_txtContent);
            if (_btnBoostyText != null) FontFallback.EnsureCyrillicFont(_btnBoostyText);
            if (_btnCrewText != null) FontFallback.EnsureCyrillicFont(_btnCrewText);

            var isRu = string.Equals(Plugin.Language.Value, "ru", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(Plugin.Language.Value, "Russian", StringComparison.OrdinalIgnoreCase);

            if (isRu)
            {
                _txtContent.text =
                    "<b><size=115%><color=#00e5ff>// CFLABS</color></size></b> <size=95%><color=#5588a0>- РУСИФИКАЦИЯ OSTRANAUTS</color></size>\n" +
                    "<color=#ffaa33><b>Подписывайся на Boosty!</b></color> <color=#d8e4f0>Мод живёт благодаря вашей поддержке.</color>\n" +
                    "<color=#a0b4c8>Голосуй за проекты, присылай баг-репорты - автор рад любой помощи :)</color>\n" +
                    "<size=92%><color=#758a9e><i>P.S. Разработкой модификации занимается один человек. Удачи, капитан!</i></color></size>";

                if (_btnBoostyText != null)
                    _btnBoostyText.text = "[ // ПОДДЕРЖАТЬ НА BOOSTY // ]";
                if (_btnCrewText != null)
                    _btnCrewText.text = "[ // ЭКИПАЖ // ]";
            }
            else
            {
                _txtContent.text =
                    "<b><size=115%><color=#00e5ff>// CFLABS</color></size></b> <size=95%><color=#5588a0>- OSTRANAUTS TRANSLATION</color></size>\n" +
                    "<color=#ffaa33><b>Support on Boosty!</b></color> <color=#d8e4f0>The mod lives thanks to your support.</color>\n" +
                    "<color=#a0b4c8>Vote for projects, report bugs, and share your thoughts with author :)</color>\n" +
                    "<size=92%><color=#758a9e><i>P.S. Developed with care by a solo creator. Good luck, Captain!</i></color></size>";

                if (_btnBoostyText != null)
                    _btnBoostyText.text = "[ // SUPPORT ON BOOSTY // ]";
                if (_btnCrewText != null)
                    _btnCrewText.text = "[ // CREW // ]";
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

        private static Sprite CreateButtonSprite(bool hover, Color32 bg, Color32 border)
        {
            int w = 32;
            int h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[w * h];

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

    public class MenuCrewManifestModal : MonoBehaviour
    {
        public static MenuCrewManifestModal Instance { get; private set; }
        private GameObject _dialogGo;
        private TMP_Text _txtBody;

        public static void Toggle(Transform parentCanvas)
        {
            if (Instance == null)
            {
                var modalGo = new GameObject("OstraCrewManifestModal", typeof(RectTransform), typeof(MenuCrewManifestModal));
                modalGo.transform.SetParent(parentCanvas, false);
                var comp = modalGo.GetComponent<MenuCrewManifestModal>();
                comp.Setup();
            }
            else
            {
                Instance.gameObject.SetActive(!Instance.gameObject.activeSelf);
                if (Instance.gameObject.activeSelf) Instance.UpdateText();
            }
        }

        public void Setup()
        {
            Instance = this;
            var rt = GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Backdrop dismisser
            var bgGo = new GameObject("Backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MenuSimpleButton));
            bgGo.transform.SetParent(transform, false);
            var bgRt = bgGo.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.65f);
            var bgBtn = bgGo.GetComponent<MenuSimpleButton>();
            bgBtn.OnClick = () => gameObject.SetActive(false);

            // Dialog Window
            _dialogGo = new GameObject("Dialog", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _dialogGo.transform.SetParent(transform, false);
            var dRt = _dialogGo.GetComponent<RectTransform>();
            dRt.anchorMin = new Vector2(0.5f, 0.5f);
            dRt.anchorMax = new Vector2(0.5f, 0.5f);
            dRt.pivot = new Vector2(0.5f, 0.5f);
            dRt.anchoredPosition = Vector2.zero;
            dRt.sizeDelta = new Vector2(700f, 460f);

            var dImg = _dialogGo.GetComponent<Image>();
            dImg.sprite = MenuAuthorCredits.GetPanelSprite();
            dImg.type = Image.Type.Sliced;
            dImg.color = Color.white;

            // Content text
            var txtGo = new GameObject("TxtBody", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(_dialogGo.transform, false);
            var txtRt = txtGo.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(26f, 58f);
            txtRt.offsetMax = new Vector2(-26f, -22f);

            _txtBody = txtGo.GetComponent<TextMeshProUGUI>();
            _txtBody.alignment = TextAlignmentOptions.TopLeft;
            _txtBody.fontSize = 15f;
            _txtBody.color = Color.white;
            _txtBody.lineSpacing = 2.5f;
            FontFallback.EnsureCyrillicFont(_txtBody);

            // Close button
            var closeGo = new GameObject("BtnClose", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(MenuSimpleButton));
            closeGo.transform.SetParent(_dialogGo.transform, false);
            var cRt = closeGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0.5f, 0f);
            cRt.anchorMax = new Vector2(0.5f, 0f);
            cRt.pivot = new Vector2(0.5f, 0f);
            cRt.anchoredPosition = new Vector2(0f, 14f);
            cRt.sizeDelta = new Vector2(220f, 34f);

            var cImg = closeGo.GetComponent<Image>();
            cImg.sprite = MenuAuthorCredits.GetCyanNormalSprite();
            cImg.type = Image.Type.Sliced;
            cImg.color = Color.white;

            var cBtn = closeGo.GetComponent<MenuSimpleButton>();
            cBtn.TargetImage = cImg;
            cBtn.NormalSprite = MenuAuthorCredits.GetCyanNormalSprite();
            cBtn.HoverSprite = MenuAuthorCredits.GetCyanHoverSprite();
            cBtn.OnClick = () => gameObject.SetActive(false);

            var cTxtGo = new GameObject("TxtClose", typeof(RectTransform), typeof(TextMeshProUGUI));
            cTxtGo.transform.SetParent(closeGo.transform, false);
            var cTxtRt = cTxtGo.GetComponent<RectTransform>();
            cTxtRt.anchorMin = Vector2.zero;
            cTxtRt.anchorMax = Vector2.one;
            cTxtRt.offsetMin = Vector2.zero;
            cTxtRt.offsetMax = Vector2.zero;
            var cTxt = cTxtGo.GetComponent<TextMeshProUGUI>();
            cTxt.alignment = TextAlignmentOptions.Center;
            cTxt.fontSize = 14f;
            cTxt.fontStyle = FontStyles.Bold;
            cTxt.color = new Color(0.85f, 0.96f, 1f, 1f);
            cTxt.text = "[ >> ЗАКРЫТЬ << ]";
            FontFallback.EnsureCyrillicFont(cTxt);

            UpdateText();
        }

        public void UpdateText()
        {
            if (_txtBody == null) return;
            FontFallback.EnsureCyrillicFont(_txtBody);

            string creditsBody = "";
            try
            {
                var path = System.IO.Path.Combine(Plugin.DataDir.Value, "langs", "ru", "data", "credits.json");
                if (System.IO.File.Exists(path))
                {
                    var json = System.IO.File.ReadAllText(path);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("aModCredits", out var modCredits) && modCredits.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        var colors = new string[] { "#ffd700", "#00e5ff", "#ffaa33", "#88c0d0", "#ffcc00", "#ffffff" };
                        int colorIdx = 0;
                        foreach (var prop in modCredits.EnumerateObject())
                        {
                            string rank = prop.Name;
                            var namesArray = prop.Value.EnumerateArray();
                            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
                            foreach (var name in namesArray) names.Add(name.GetString());
                            string color = colors[colorIdx % colors.Length];
                            creditsBody += $"<color={color}><b>· {rank}:</b></color> <color=#ffffff>{string.Join(", ", names)}</color>\n\n";
                            colorIdx++;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("[i18n] Failed to load custom credits for MenuLanguageAstronaut: " + ex.Message);
            }

            if (string.IsNullOrEmpty(creditsBody))
            {
                creditsBody =
                    "<color=#ffd700><b>• ШЕЙХ:</b></color> <color=#ffffff>Сергей Коршунов</color>\n\n" +
                    "<color=#00e5ff><b>• АДМИРАЛЫ:</b></color> <color=#ffffff>Миша Аверин, Towland, Игорь Мирошниченко</color>\n\n" +
                    "<color=#ffaa33><b>• КАПИТАНЫ:</b></color> <color=#ffffff>Gundyar, Сергей Примаков, Zurics Game</color>\n\n" +
                    "<color=#88c0d0><b>• ЮНГИ:</b></color> <color=#ffffff>GreyViS, Pavel Bezik, LunarGoat, jard, languin, Анна Плагиатор</color>\n\n";
            }

            _txtBody.text =
                "<b><size=125%><color=#00e5ff>// БОРТОВОЙ МАНИФЕСТ ЭКИПАЖА</color></size></b>\n" +
                "<size=95%><color=#ffaa33>ТЕ, БЛАГОДАРЯ КОМУ МОД ВООБЩЕ СУЩЕСТВУЕТ:</color></size>\n\n" +
                creditsBody +
                "<size=88%><color=#708599><i>Сердечная благодарность каждому члену экипажа за поддержку и веру в проект!</i></color></size>";
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                gameObject.SetActive(false);
            }
        }
    }
}
