using System;
using System.Collections.Generic;
using System.Text;
using ConsoleAutocomplete.Util;
using UnityEngine;
using UnityEngine.UI;

namespace ConsoleAutocomplete.Autocomplete
{
    /// <summary>
    /// Runtime overlay: structure helper, description, suggestion rows (value — source), ghost suffix.
    /// </summary>
    public sealed class SuggestionOverlay
    {
        private const int MaxRows = SuggestionEngine.MaxVisibleSuggestions;

        private GameObject _root;
        private TextMeshProUGUI _header;
        private TextMeshProUGUI _helper;
        private TextMeshProUGUI _ghost;
        private readonly TextMeshProUGUI[] _rows = new TextMeshProUGUI[MaxRows];
        private Image _background;
        private TMP_InputField _input;
        private bool _visible;

        public bool IsVisible => _visible;
        public TMP_InputField BoundInput => _input;

        public void Attach(ConsoleUI consoleUi)
        {
            if (consoleUi == null || consoleUi.InputField == null)
                return;

            _input = consoleUi.InputField;
            if (_root != null)
                return;

            Transform parent = consoleUi.Container != null
                ? consoleUi.Container.transform
                : consoleUi.transform;

            _root = new GameObject("ConsoleAutocompleteOverlay");
            _root.transform.SetParent(parent, false);

            RectTransform rootRt = _root.AddComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0f, 0f);
            rootRt.anchorMax = new Vector2(1f, 0f);
            rootRt.pivot = new Vector2(0.5f, 0f);
            rootRt.anchoredPosition = new Vector2(0f, 36f);
            rootRt.sizeDelta = new Vector2(-20f, 260f);

            _background = _root.AddComponent<Image>();
            _background.color = new Color(0.05f, 0.05f, 0.05f, 0.92f);

            VerticalLayoutGroup layout = _root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            ContentSizeFitter fitter = _root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _header = CreateText(_root.transform, "Header", 16f, FontStyles.Bold);
            _header.color = new Color(0.85f, 0.92f, 1f, 1f);

            _helper = CreateText(_root.transform, "Helper", 13f, FontStyles.Italic);
            _helper.color = new Color(0.65f, 0.7f, 0.75f, 1f);
#if MONO
            _helper.textWrappingMode = TextWrappingModes.Normal;
#else
            _helper.enableWordWrapping = true;
#endif
            _helper.overflowMode = TextOverflowModes.Ellipsis;
            LayoutElement helperLe = _helper.GetComponent<LayoutElement>();
            if (helperLe != null)
            {
                helperLe.minHeight = 18f;
                helperLe.preferredHeight = 32f;
            }

            for (int i = 0; i < MaxRows; i++)
            {
                _rows[i] = CreateText(_root.transform, "Row" + i, 15f, FontStyles.Normal);
                _rows[i].richText = true;
            }

            CreateGhost(consoleUi);
            Hide();
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

            if (result == null || !result.HasSuggestions)
            {
                Hide();
                return;
            }

            _visible = true;
            _root.SetActive(true);

            SuggestionItem selected = result.Selected;
            string structure = selected?.StructureHeader
                               ?? result.StructureHeader
                               ?? string.Empty;
            _header.text = string.IsNullOrEmpty(structure)
                ? string.Empty
                : structure;

            string helper = result.DescriptionHelper ?? string.Empty;
            if (selected != null && !string.IsNullOrEmpty(selected.SourceLabel))
            {
                string sourceLine = "Source: " + selected.SourceLabel;
                helper = string.IsNullOrEmpty(helper)
                    ? sourceLine
                    : helper + "  ·  " + sourceLine;
            }

            _helper.text = helper;
            _helper.gameObject.SetActive(!string.IsNullOrEmpty(helper));

            int count = Math.Min(result.Suggestions.Count, MaxRows);
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

                var sb = new StringBuilder();
                if (isSelected)
                    sb.Append("<color=#FFD27F>");
                sb.Append(left);
                if (isSelected)
                    sb.Append("</color>");

                if (!string.IsNullOrEmpty(right))
                {
                    sb.Append(" <color=#9AA0A6>— ");
                    sb.Append(right);
                    sb.Append("</color>");
                }

                _rows[i].text = sb.ToString();
                _rows[i].gameObject.SetActive(true);
            }

            UpdateGhost(result, inputText);
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
            _ghost.text = "<color=#00000000>" + Escape(typed) + "</color><color=#888888>" + Escape(suffix) + "</color>";
            _ghost.gameObject.SetActive(true);
            SyncGhostStyle();
        }

        private void SyncGhostStyle()
        {
            TextMeshProUGUI source = null;
            if (_input != null && _input.textComponent != null)
            {
#if IL2CPP
                source = _input.textComponent.TryCast<TextMeshProUGUI>();
#else
                source = _input.textComponent as TextMeshProUGUI;
#endif
            }

            if (_ghost == null || source == null)
                return;

            _ghost.font = source.font;
            _ghost.fontSize = source.fontSize;
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

        private static TextMeshProUGUI CreateText(Transform parent, string name, float size, FontStyles style)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Left;
#if MONO
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
#else
            tmp.enableWordWrapping = false;
#endif
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = size + 6f;
            le.preferredHeight = size + 6f;
            return tmp;
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
