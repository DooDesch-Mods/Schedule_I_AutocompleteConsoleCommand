using System;
using System.Text;
using ConsoleAutocomplete.Util;
using UnityEngine;
using UnityEngine.UI;

namespace ConsoleAutocomplete.Autocomplete
{
    /// <summary>
    /// Dropdown panel hung <b>below</b> the console input bar (not above — that goes off the top of the screen).
    /// Order: structure → helper/source → suggestion rows.
    /// </summary>
    public sealed class SuggestionOverlay
    {
        private const int MaxRows = SuggestionEngine.MaxVisibleSuggestions;

        private GameObject _root;
        private RectTransform _rootRt;
        private GameObject _rowsRoot;
        private TextMeshProUGUI _header;
        private TextMeshProUGUI _helper;
        private TextMeshProUGUI _ghost;
        private readonly TextMeshProUGUI[] _rows = new TextMeshProUGUI[MaxRows];
        private TMP_InputField _input;
        private ConsoleUI _ui;
        private bool _visible;

        public bool IsVisible => _visible;
        public TMP_InputField BoundInput => _input;

        public void Attach(ConsoleUI consoleUi)
        {
            if (consoleUi == null || consoleUi.InputField == null)
                return;

            _ui = consoleUi;
            _input = consoleUi.InputField;
            if (_root != null)
            {
                RepositionUnderInput();
                return;
            }

            // Prefer the screen canvas so we are not clipped inside the thin top bar.
            Transform parent = null;
            if (consoleUi.canvas != null)
                parent = consoleUi.canvas.transform;
            else if (consoleUi.Container != null)
                parent = consoleUi.Container.transform;
            else
                parent = consoleUi.transform;

            _root = new GameObject("ConsoleAutocompleteOverlay");
            _root.transform.SetParent(parent, false);
            _root.transform.SetAsLastSibling();

            _rootRt = _root.AddComponent<RectTransform>();
            // Hang downward from a top anchor (console is a top bar — growing upward leaves the screen).
            _rootRt.anchorMin = new Vector2(0f, 1f);
            _rootRt.anchorMax = new Vector2(1f, 1f);
            _rootRt.pivot = new Vector2(0.5f, 1f);
            _rootRt.sizeDelta = new Vector2(-24f, 0f);

            Image background = _root.AddComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.08f, 0.97f);
            background.raycastTarget = false;

            VerticalLayoutGroup layout = _root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            ContentSizeFitter fitter = _root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _header = CreateText(_root.transform, "Structure", 17f);
            _header.color = new Color(1f, 0.86f, 0.4f, 1f);
            SetFixedHeight(_header, 24f);

            _helper = CreateText(_root.transform, "Helper", 13f);
            _helper.color = new Color(0.78f, 0.84f, 0.92f, 1f);
#if MONO
            _helper.textWrappingMode = TextWrappingModes.Normal;
#else
            _helper.enableWordWrapping = true;
#endif
            _helper.overflowMode = TextOverflowModes.Overflow;
            SetFixedHeight(_helper, 36f);

            _rowsRoot = new GameObject("Rows");
            _rowsRoot.transform.SetParent(_root.transform, false);
            VerticalLayoutGroup rowsLayout = _rowsRoot.AddComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = 2f;
            rowsLayout.childControlHeight = true;
            rowsLayout.childControlWidth = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.childForceExpandWidth = true;
            ContentSizeFitter rowsFit = _rowsRoot.AddComponent<ContentSizeFitter>();
            rowsFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            for (int i = 0; i < MaxRows; i++)
            {
                _rows[i] = CreateText(_rowsRoot.transform, "Row" + i, 15f);
                _rows[i].richText = true;
                _rows[i].overflowMode = TextOverflowModes.Overflow;
            }

            SyncFontsFromInput();
            CreateGhost(consoleUi);
            RepositionUnderInput();
            Hide();
            ModLog.Debug("SuggestionOverlay attached under console input (canvas parent).");
        }

        public void Destroy()
        {
            if (_ghost != null)
            {
                UnityEngine.Object.Destroy(_ghost.gameObject);
                _ghost = null;
            }

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
                _rootRt = null;
            }

            _visible = false;
        }

        public void Hide()
        {
            _visible = false;
            if (_root != null)
                _root.SetActive(false);
            if (_ghost != null)
            {
                _ghost.text = string.Empty;
                _ghost.gameObject.SetActive(false);
            }
        }

        public void Render(SuggestionEngine.Result result, string inputText)
        {
            if (_root == null)
                return;

            if (result == null || !result.HasHelper)
            {
                Hide();
                return;
            }

            SyncFontsFromInput();
            RepositionUnderInput();

            _visible = true;
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();

            SuggestionItem selected = result.Selected;
            string structure = !string.IsNullOrEmpty(result.StructureHeader)
                ? result.StructureHeader
                : (selected?.StructureHeader ?? string.Empty);

            if (selected != null && !string.IsNullOrEmpty(selected.StructureHeader))
                structure = selected.StructureHeader;

            _header.text = structure ?? string.Empty;
            _header.gameObject.SetActive(!string.IsNullOrEmpty(_header.text));

            string helper = result.DescriptionHelper ?? string.Empty;
            string source = selected?.SourceLabel;
            if (string.IsNullOrEmpty(source))
                source = result.SourceHelper;

            if (!string.IsNullOrEmpty(source))
            {
                string sourceLine = "Source: " + source;
                helper = string.IsNullOrEmpty(helper) ? sourceLine : helper + "  ·  " + sourceLine;
            }

            _helper.text = helper;
            _helper.gameObject.SetActive(!string.IsNullOrEmpty(helper));

            int count = Math.Min(result.Suggestions?.Count ?? 0, MaxRows);
            _rowsRoot.SetActive(count > 0);
            for (int i = 0; i < MaxRows; i++)
            {
                if (i >= count)
                {
                    _rows[i].gameObject.SetActive(false);
                    continue;
                }

                SuggestionItem item = result.Suggestions[i];
                bool isSelected = i == result.SelectedIndex;
                string left = item.DisplayLeft ?? item.Value ?? string.Empty;
                string right = item.SourceLabel ?? string.Empty;

                var sb = new StringBuilder(64);
                if (isSelected)
                    sb.Append("<color=#FFD27F>");
                sb.Append(Escape(left));
                if (isSelected)
                    sb.Append("</color>");

                if (!string.IsNullOrEmpty(right))
                {
                    bool isMod = !right.Equals(ModAttribution.VanillaLabel, StringComparison.OrdinalIgnoreCase);
                    sb.Append(isMod ? " <color=#7DFFB2>— " : " <color=#8B93A0>— ");
                    sb.Append(Escape(right));
                    sb.Append("</color>");
                }

                _rows[i].text = sb.ToString();
                _rows[i].gameObject.SetActive(true);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rootRt);

            ModLog.Debug(
                "Overlay render header='"
                + (_header.text ?? string.Empty)
                + "' helper='"
                + (_helper.text ?? string.Empty)
                + "' rows="
                + count
                + "' pos="
                + (_rootRt != null ? _rootRt.anchoredPosition.ToString() : "?"));

            UpdateGhost(result, inputText);
        }

        /// <summary>
        /// Place the panel directly under the TMP input so it hangs into the game view
        /// (console is a top bar — anchoring upward puts the UI off-screen).
        /// </summary>
        private void RepositionUnderInput()
        {
            if (_rootRt == null || _input == null)
                return;

            try
            {
                RectTransform inputRt = _input.transform as RectTransform;
                RectTransform canvasRt = _rootRt.parent as RectTransform;
                if (inputRt == null || canvasRt == null)
                {
                    ApplyTopBarFallback();
                    return;
                }

                Camera eventCam = null;
                if (_ui != null && _ui.canvas != null
                    && _ui.canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    eventCam = _ui.canvas.worldCamera;

                Vector3[] corners = new Vector3[4];
                inputRt.GetWorldCorners(corners);
                // corners: 0=BL, 1=TL, 2=TR, 3=BR
                Vector2 screenBL = RectTransformUtility.WorldToScreenPoint(eventCam, corners[0]);
                Vector2 screenBR = RectTransformUtility.WorldToScreenPoint(eventCam, corners[3]);

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRt, screenBL, eventCam, out Vector2 localBL)
                    || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRt, screenBR, eventCam, out Vector2 localBR))
                {
                    ApplyTopBarFallback();
                    return;
                }

                float width = Mathf.Abs(localBR.x - localBL.x);
                if (width < 200f)
                    width = Mathf.Max(420f, canvasRt.rect.width - 48f);

                float centerX = (localBL.x + localBR.x) * 0.5f;
                float topY = localBL.y - 6f; // a few px under the input

                _rootRt.anchorMin = new Vector2(0.5f, 0.5f);
                _rootRt.anchorMax = new Vector2(0.5f, 0.5f);
                _rootRt.pivot = new Vector2(0.5f, 1f);
                _rootRt.anchoredPosition = new Vector2(centerX, topY);
                _rootRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

                ModLog.Debug(
                    "Overlay under input anchored="
                    + _rootRt.anchoredPosition
                    + " w="
                    + width.ToString("F0"));
            }
            catch (Exception ex)
            {
                ModLog.Warning("Overlay reposition failed: " + ex.Message);
                ApplyTopBarFallback();
            }
        }

        private void ApplyTopBarFallback()
        {
            if (_rootRt == null)
                return;

            // Fixed drop just under the top edge of the console canvas.
            _rootRt.anchorMin = new Vector2(0f, 1f);
            _rootRt.anchorMax = new Vector2(1f, 1f);
            _rootRt.pivot = new Vector2(0.5f, 1f);
            _rootRt.anchoredPosition = new Vector2(0f, -42f);
            _rootRt.sizeDelta = new Vector2(-24f, _rootRt.sizeDelta.y);
        }

        private void SyncFontsFromInput()
        {
            TextMeshProUGUI source = ResolveInputText();
            if (source == null || source.font == null)
                return;

            ApplyFont(_header, source);
            ApplyFont(_helper, source);
            for (int i = 0; i < _rows.Length; i++)
                ApplyFont(_rows[i], source);
        }

        private static void ApplyFont(TextMeshProUGUI target, TextMeshProUGUI source)
        {
            if (target == null || source == null)
                return;

            target.font = source.font;
            if (source.fontSharedMaterial != null)
                target.fontSharedMaterial = source.fontSharedMaterial;
            target.fontStyle = FontStyles.Normal;
        }

        private TextMeshProUGUI ResolveInputText()
        {
            if (_input == null || _input.textComponent == null)
                return null;

#if IL2CPP
            return _input.textComponent.TryCast<TextMeshProUGUI>();
#else
            return _input.textComponent as TextMeshProUGUI;
#endif
        }

        private void UpdateGhost(SuggestionEngine.Result result, string inputText)
        {
            if (_ghost == null || _input == null)
                return;

            string suffix = SuggestionEngine.GhostSuffix(result);
            if (string.IsNullOrEmpty(suffix))
            {
                _ghost.gameObject.SetActive(false);
                _ghost.text = string.Empty;
                return;
            }

            string typed = inputText ?? string.Empty;
            _ghost.text = "<color=#00000000>" + Escape(typed) + "</color><color=#9A9A9A>" + Escape(suffix) + "</color>";
            _ghost.gameObject.SetActive(true);
            SyncGhostStyle();
        }

        private void SyncGhostStyle()
        {
            TextMeshProUGUI source = ResolveInputText();
            if (_ghost == null || source == null)
                return;

            _ghost.font = source.font;
            if (source.fontSharedMaterial != null)
                _ghost.fontSharedMaterial = source.fontSharedMaterial;
            _ghost.fontSize = source.fontSize;
            _ghost.fontStyle = FontStyles.Normal;
            _ghost.alignment = source.alignment;
            _ghost.margin = source.margin;
            _ghost.characterSpacing = source.characterSpacing;
            _ghost.lineSpacing = source.lineSpacing;
#if MONO
            _ghost.textWrappingMode = TextWrappingModes.NoWrap;
#else
            _ghost.enableWordWrapping = false;
#endif
            _ghost.overflowMode = TextOverflowModes.Overflow;
        }

        private void CreateGhost(ConsoleUI consoleUi)
        {
            try
            {
                TextMeshProUGUI source = null;
                if (consoleUi.InputField != null && consoleUi.InputField.textComponent != null)
                {
#if IL2CPP
                    source = consoleUi.InputField.textComponent.TryCast<TextMeshProUGUI>();
#else
                    source = consoleUi.InputField.textComponent as TextMeshProUGUI;
#endif
                }

                if (source == null)
                    return;

                GameObject ghostGo = new GameObject("ConsoleAutocompleteGhost");
                ghostGo.transform.SetParent(source.transform.parent, false);
                ghostGo.transform.SetSiblingIndex(source.transform.GetSiblingIndex());

                RectTransform rt = ghostGo.AddComponent<RectTransform>();
                RectTransform sourceRt = source.rectTransform;
                rt.anchorMin = sourceRt.anchorMin;
                rt.anchorMax = sourceRt.anchorMax;
                rt.pivot = sourceRt.pivot;
                rt.anchoredPosition = sourceRt.anchoredPosition;
                rt.sizeDelta = sourceRt.sizeDelta;
                rt.offsetMin = sourceRt.offsetMin;
                rt.offsetMax = sourceRt.offsetMax;

                _ghost = ghostGo.AddComponent<TextMeshProUGUI>();
                _ghost.raycastTarget = false;
                _ghost.richText = true;
                SyncGhostStyle();
                _ghost.gameObject.SetActive(false);
            }
            catch (Exception ex)
            {
                ModLog.Warning("Failed to create ghost text: " + ex.Message);
                _ghost = null;
            }
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, float size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Normal;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.richText = true;
#if MONO
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
#else
            tmp.enableWordWrapping = false;
#endif
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = size + 6f;
            le.preferredHeight = size + 6f;
            le.flexibleWidth = 1f;
            return tmp;
        }

        private static void SetFixedHeight(TextMeshProUGUI tmp, float height)
        {
            LayoutElement le = tmp != null ? tmp.GetComponent<LayoutElement>() : null;
            if (le == null)
                return;

            le.minHeight = height;
            le.preferredHeight = height;
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}
