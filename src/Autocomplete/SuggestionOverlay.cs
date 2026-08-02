using System;
using System.Text;
using ConsoleAutocomplete.Util;
using UnityEngine;
using UnityEngine.UI;

namespace ConsoleAutocomplete.Autocomplete
{
    /// <summary>
    /// Dropdown panel hung <b>below</b> the console input bar (not above, that goes off the top of the screen).
    /// Order: structure, helper/source, separator, suggestion rows.
    /// The panel docks flush against the console bar; all breathing room is inner padding.
    /// </summary>
    public sealed class SuggestionOverlay
    {
        private const int MaxRows = SuggestionEngine.MaxVisibleSuggestions;
        private const float HeaderFontSize = 17f;
        private const float HelperFontSize = 13f;
        private const float RowFontSize = 15f;
        private const int DefaultInnerPadding = 12;
        private const int MinInnerPadding = 8;
        private const int MaxInnerPadding = 48;
        private const float SeparatorBlockHeight = 9f;
        private const float ScrollCueWidth = 18f;
        private const float MaxIndentFraction = 0.35f;

        /// <summary>Console bars taller/lower than this are not the bar, so the panel ignores them.</summary>
        private const float MaxBarHeight = 220f;
        private const float MaxBarBottomOffset = 64f;

        /// <summary>Keeps the source label off the suggestion value instead of gluing them together.</summary>
        private const string SourceGap = "    ";

        /// <summary>Clear space between the longest visible suggestion and the source column.</summary>
        private const float SourceColumnGap = 26f;

        /// <summary>The source column never takes more than this share of the panel width.</summary>
        private const float MaxSourceColumnFraction = 0.6f;

        private GameObject _root;
        private RectTransform _rootRt;
        private GameObject _rowsRoot;
        private VerticalLayoutGroup _layout;
        private VerticalLayoutGroup _rowsLayout;
        private GameObject _separator;
        private TextMeshProUGUI _header;
        private TextMeshProUGUI _helper;
        private TextMeshProUGUI _ghost;
        private TextMeshProUGUI _measure;
        private TextMeshProUGUI _scrollUp;
        private TextMeshProUGUI _scrollDown;
        private readonly TextMeshProUGUI[] _rows = new TextMeshProUGUI[MaxRows];
        private TMP_InputField _input;
        private ConsoleUI _ui;
        private bool _visible;
        private int _scrollOffset;
        private string _scrollKey = string.Empty;
        private string _indentKey = string.Empty;
        private int _indentWidth;
        private string _columnKey = string.Empty;
        private float _sourceColumn;
        private readonly float[] _rowNameWidth = new float[MaxRows];

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
            // Hang downward from a top anchor (console is a top bar, growing upward leaves the screen).
            _rootRt.anchorMin = new Vector2(0f, 1f);
            _rootRt.anchorMax = new Vector2(1f, 1f);
            _rootRt.pivot = new Vector2(0.5f, 1f);
            _rootRt.sizeDelta = Vector2.zero;

            Image background = _root.AddComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.08f, 0.97f);
            background.raycastTarget = false;

            _layout = _root.AddComponent<VerticalLayoutGroup>();
            _layout.padding = new RectOffset(DefaultInnerPadding, DefaultInnerPadding, 10, 10);
            _layout.spacing = 6f;
            _layout.childAlignment = TextAnchor.UpperLeft;
            _layout.childControlHeight = true;
            _layout.childControlWidth = true;
            _layout.childForceExpandHeight = false;
            _layout.childForceExpandWidth = true;

            ContentSizeFitter fitter = _root.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _header = CreateText(_root.transform, "Structure", HeaderFontSize);
            _header.color = new Color(1f, 0.86f, 0.4f, 1f);
            SetFixedHeight(_header, 24f);

            _helper = CreateText(_root.transform, "Helper", HelperFontSize);
            _helper.color = new Color(0.78f, 0.84f, 0.92f, 1f);
#if MONO
            _helper.textWrappingMode = TextWrappingModes.Normal;
#else
            _helper.enableWordWrapping = true;
#endif
            _helper.overflowMode = TextOverflowModes.Overflow;
            SetFixedHeight(_helper, 36f);

            _separator = CreateSeparator(_root.transform);

            _rowsRoot = new GameObject("Rows");
            _rowsRoot.transform.SetParent(_root.transform, false);
            _rowsLayout = _rowsRoot.AddComponent<VerticalLayoutGroup>();
            _rowsLayout.padding = new RectOffset(0, 0, 0, 0);
            _rowsLayout.spacing = 2f;
            _rowsLayout.childAlignment = TextAnchor.UpperLeft;
            _rowsLayout.childControlHeight = true;
            _rowsLayout.childControlWidth = true;
            _rowsLayout.childForceExpandHeight = false;
            _rowsLayout.childForceExpandWidth = true;
            ContentSizeFitter rowsFit = _rowsRoot.AddComponent<ContentSizeFitter>();
            rowsFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            for (int i = 0; i < MaxRows; i++)
            {
                _rows[i] = CreateText(_rowsRoot.transform, "Row" + i, RowFontSize);
                _rows[i].richText = true;
                _rows[i].overflowMode = TextOverflowModes.Overflow;
            }

            // Scroll cues live at the right edge of the row block, off the text baseline.
            _scrollUp = CreateScrollCue(_rowsRoot.transform, "ScrollUp", "▲", new Vector2(1f, 1f));
            _scrollDown = CreateScrollCue(_rowsRoot.transform, "ScrollDown", "▼", new Vector2(1f, 0f));
            _measure = CreateMeasureProbe(_root.transform);

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
            _scrollOffset = 0;
            _scrollKey = string.Empty;
            _indentKey = string.Empty;
            _columnKey = string.Empty;
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

            int total = result.Suggestions?.Count ?? 0;
            int selectedIndex = result.SelectedIndex;
            UpdateScrollWindow(result, total, selectedIndex);

            int visible = Math.Min(MaxRows, Math.Max(0, total - _scrollOffset));
            _rowsRoot.SetActive(total > 0);
            if (_separator != null)
                _separator.SetActive(total > 0 && _helper.gameObject.activeSelf);

            ApplyRowIndent(result, total > 0);
            MeasureSourceColumn(result, visible);

            for (int row = 0; row < MaxRows; row++)
            {
                int index = _scrollOffset + row;
                if (row >= visible || index >= total)
                {
                    _rows[row].gameObject.SetActive(false);
                    continue;
                }

                SuggestionItem item = result.Suggestions[index];
                bool isSelected = index == selectedIndex;
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

                    // An absolute x, so every label in the list starts in the same column and the eye
                    // reads straight down instead of tracking a ragged edge. A name too wide for the
                    // column keeps the plain gap: <pos> MOVES the cursor rather than pushing, so a
                    // column to the left of the name would draw the label back over it.
                    if (_sourceColumn > 0f && _rowNameWidth[row] + 4f < _sourceColumn)
                        sb.Append("<pos=").Append(Mathf.RoundToInt(_sourceColumn)).Append("px>");
                    else
                        sb.Append(SourceGap);

                    sb.Append(isMod ? "<color=#7DFFB2>- " : "<color=#8B93A0>- ");
                    sb.Append(Escape(right));
                    sb.Append("</color>");
                }

                _rows[row].text = sb.ToString();
                _rows[row].gameObject.SetActive(true);
            }

            UpdateScrollCues(visible, total);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rootRt);

            ModLog.Debug(
                "Overlay render header='"
                + (_header.text ?? string.Empty)
                + "' helper='"
                + (_helper.text ?? string.Empty)
                + "' rows="
                + visible
                + "/"
                + total
                + " scroll="
                + _scrollOffset
                + " sel="
                + selectedIndex
                + "' pos="
                + (_rootRt != null ? _rootRt.anchoredPosition.ToString() : "?"));

            UpdateGhost(result, inputText);
        }

        private void UpdateScrollWindow(SuggestionEngine.Result result, int total, int selectedIndex)
        {
            string key = total + "|" + (result.CurrentToken ?? string.Empty) + "|" + (result.StructureHeader ?? string.Empty);
            if (!string.Equals(key, _scrollKey, StringComparison.Ordinal))
            {
                _scrollKey = key;
                _scrollOffset = 0;
            }

            if (total <= 0)
            {
                _scrollOffset = 0;
                return;
            }

            int window = Math.Min(MaxRows, total);
            if (selectedIndex < 0)
                selectedIndex = 0;
            if (selectedIndex >= total)
                selectedIndex = total - 1;

            if (selectedIndex < _scrollOffset)
                _scrollOffset = selectedIndex;
            else if (selectedIndex >= _scrollOffset + window)
                _scrollOffset = selectedIndex - window + 1;

            int maxOffset = Math.Max(0, total - window);
            if (_scrollOffset < 0)
                _scrollOffset = 0;
            if (_scrollOffset > maxOffset)
                _scrollOffset = maxOffset;
        }

        private void UpdateScrollCues(int visible, int total)
        {
            if (_scrollUp != null)
                _scrollUp.gameObject.SetActive(visible > 0 && _scrollOffset > 0);

            if (_scrollDown != null)
                _scrollDown.gameObject.SetActive(visible > 0 && _scrollOffset + visible < total);
        }

        /// <summary>
        /// Works out the x every source label starts at: the widest visible suggestion plus a gap.
        ///
        /// Measured rather than fixed, because the widest name decides where the column can begin - a
        /// constant would waste half the panel on a list of short names and collide on a list of long
        /// ones. Recomputed only when the visible set changes, so moving the selection never shifts the
        /// column sideways, and holding an arrow does not re-measure eight rows per frame.
        /// </summary>
        private void MeasureSourceColumn(SuggestionEngine.Result result, int visible)
        {
            string key = _scrollOffset
                         + "|" + visible
                         + "|" + (result?.Suggestions?.Count ?? 0)
                         + "|" + (result?.CurrentToken ?? string.Empty);
            if (string.Equals(key, _columnKey, StringComparison.Ordinal))
                return;

            _columnKey = key;
            _sourceColumn = 0f;
            if (_measure == null || result?.Suggestions == null || visible <= 0)
                return;

            _measure.fontSize = RowFontSize;
            float widest = 0f;
            for (int row = 0; row < visible && row < MaxRows; row++)
            {
                int index = _scrollOffset + row;
                if (index >= result.Suggestions.Count)
                    break;

                SuggestionItem item = result.Suggestions[index];
                _measure.text = item.DisplayLeft ?? item.Value ?? string.Empty;
                float width = _measure.preferredWidth;
                _rowNameWidth[row] = width;
                if (width > widest)
                    widest = width;
            }

            float limit = _rootRt != null ? _rootRt.rect.width * MaxSourceColumnFraction : 240f;
            _sourceColumn = Mathf.Clamp(widest + SourceColumnGap, 0f, Mathf.Max(0f, limit));
        }

        /// <summary>
        /// Indents the rows to the column of the argument they complete, so the options for
        /// <c>give &lt;item&gt;</c> line up under <c>&lt;item&gt;</c> in the structure header.
        /// </summary>
        private void ApplyRowIndent(SuggestionEngine.Result result, bool hasRows)
        {
            if (_rowsLayout == null)
                return;

            int argIndex = hasRows && result != null ? result.ArgIndex : 0;
            string header = _header != null ? _header.text : string.Empty;
            string key = argIndex + "|" + header;
            if (!string.Equals(key, _indentKey, StringComparison.Ordinal))
            {
                _indentKey = key;
                _indentWidth = argIndex > 0
                    ? Mathf.RoundToInt(MeasureHeaderPrefix(header, argIndex))
                    : 0;
            }

            if (_rowsLayout.padding.left == _indentWidth)
                return;

            _rowsLayout.padding = new RectOffset(_indentWidth, 0, 0, 0);
        }

        /// <summary>
        /// Width of the header up to the start of argument <paramref name="argIndex"/>. Falls back to
        /// the last argument when the header lists fewer arguments than the player typed.
        /// </summary>
        private float MeasureHeaderPrefix(string header, int argIndex)
        {
            if (_measure == null || string.IsNullOrEmpty(header) || argIndex <= 0)
                return 0f;

            int cut = 0;
            int tokenIndex = 0;
            int lastTokenStart = 0;
            bool inToken = false;
            for (int i = 0; i < header.Length; i++)
            {
                if (!char.IsWhiteSpace(header[i]))
                {
                    if (inToken)
                        continue;

                    inToken = true;
                    lastTokenStart = i;
                    if (tokenIndex == argIndex)
                    {
                        cut = i;
                        break;
                    }
                }
                else if (inToken)
                {
                    inToken = false;
                    tokenIndex++;
                }
            }

            if (cut == 0)
                cut = tokenIndex > 0 ? lastTokenStart : 0;

            if (cut <= 0)
                return 0f;

            // The probe is shared with the row-column measurement, which runs at the row size - so the
            // size is set here rather than assumed, or the indent would be measured in the wrong font.
            _measure.fontSize = HeaderFontSize;
            _measure.text = header.Substring(0, cut);
            float width = _measure.preferredWidth;
            float limit = _rootRt != null ? _rootRt.rect.width * MaxIndentFraction : 160f;
            return Mathf.Clamp(width, 0f, Mathf.Max(0f, limit));
        }

        /// <summary>
        /// Docks the panel flush against the bottom edge of the console bar so it hangs into the
        /// game view (console is a top bar, anchoring upward puts the UI off-screen). The panel
        /// spans the bar's width and turns the bar's own text inset into its inner padding, so the
        /// suggestion rows line up with the console prompt instead of floating in a gap.
        /// </summary>
        private void RepositionUnderInput()
        {
            if (_rootRt == null || _input == null)
                return;

            try
            {
                RectTransform inputRt = AsRect(_input.transform);
                RectTransform canvasRt = AsRect(_rootRt.parent);
                if (inputRt == null || canvasRt == null)
                {
                    ApplyTopBarFallback();
                    return;
                }

                Camera eventCam = null;
                if (_ui != null && _ui.canvas != null
                    && _ui.canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    eventCam = _ui.canvas.worldCamera;

                if (!TryGetLocalEdges(inputRt, canvasRt, eventCam, out Vector2 inputMin, out Vector2 inputMax))
                {
                    ApplyTopBarFallback();
                    return;
                }

                Vector2 dockMin = inputMin;
                Vector2 dockMax = inputMax;
                int innerPadding = DefaultInnerPadding;
                bool usedBar = false;

                RectTransform barRt = AsRect(_ui != null && _ui.Container != null ? _ui.Container.transform : null);
                if (barRt != null
                    && TryGetLocalEdges(barRt, canvasRt, eventCam, out Vector2 barMin, out Vector2 barMax)
                    && IsPlausibleBar(barMin, barMax, inputMin, inputMax))
                {
                    dockMin = barMin;
                    dockMax = barMax;
                    usedBar = true;
                    // The gap that used to sit outside the panel becomes padding inside it.
                    innerPadding = Mathf.Clamp(
                        Mathf.RoundToInt(inputMin.x - barMin.x),
                        MinInnerPadding,
                        MaxInnerPadding);
                }

                float width = dockMax.x - dockMin.x;
                if (width < 200f)
                {
                    width = Mathf.Max(420f, canvasRt.rect.width - 48f);
                    innerPadding = DefaultInnerPadding;
                    usedBar = false;
                }

                float centerX = (dockMin.x + dockMax.x) * 0.5f;
                float topY = dockMin.y; // flush against the bar, no outside gap

                // A projection that lands outside the parent would hide the panel; the fixed
                // fallback drop is always on screen, so prefer it over a silent disappearance.
                Rect parentRect = canvasRt.rect;
                if (topY <= parentRect.yMin + 1f
                    || topY > parentRect.yMax + 1f
                    || centerX < parentRect.xMin - 1f
                    || centerX > parentRect.xMax + 1f)
                {
                    ModLog.Warning(
                        "Overlay dock target off screen (center="
                        + centerX.ToString("F0")
                        + " top="
                        + topY.ToString("F0")
                        + "), using fallback.");
                    ApplyTopBarFallback();
                    return;
                }

                _rootRt.anchorMin = new Vector2(0.5f, 0.5f);
                _rootRt.anchorMax = new Vector2(0.5f, 0.5f);
                _rootRt.pivot = new Vector2(0.5f, 1f);
                _rootRt.anchoredPosition = new Vector2(centerX, topY);
                _rootRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                ApplyInnerPadding(innerPadding);

                ModLog.Debug(
                    "Overlay docked anchored="
                    + _rootRt.anchoredPosition
                    + " w="
                    + width.ToString("F0")
                    + " pad="
                    + innerPadding
                    + " bar="
                    + usedBar
                    + " input=["
                    + inputMin.ToString("F0")
                    + ".."
                    + inputMax.ToString("F0")
                    + "]");
            }
            catch (Exception ex)
            {
                ModLog.Warning("Overlay reposition failed: " + ex.Message);
                ApplyTopBarFallback();
            }
        }

        /// <summary>
        /// Rejects containers that are not the console bar (a full-screen wrapper would drop the
        /// panel to the bottom of the screen).
        /// </summary>
        private static bool IsPlausibleBar(Vector2 barMin, Vector2 barMax, Vector2 inputMin, Vector2 inputMax)
        {
            float barWidth = barMax.x - barMin.x;
            float barHeight = barMax.y - barMin.y;
            float inputWidth = inputMax.x - inputMin.x;

            return barWidth >= inputWidth - 1f
                   && barHeight <= MaxBarHeight
                   && barMin.y <= inputMin.y + 1f
                   && inputMin.y - barMin.y <= MaxBarBottomOffset
                   && inputMin.x >= barMin.x - 1f;
        }

        private void ApplyTopBarFallback()
        {
            if (_rootRt == null)
                return;

            // Fixed drop just under the top edge of the console canvas, full width.
            _rootRt.anchorMin = new Vector2(0f, 1f);
            _rootRt.anchorMax = new Vector2(1f, 1f);
            _rootRt.pivot = new Vector2(0.5f, 1f);
            _rootRt.anchoredPosition = new Vector2(0f, -42f);
            _rootRt.sizeDelta = new Vector2(0f, _rootRt.sizeDelta.y);
            ApplyInnerPadding(DefaultInnerPadding);
        }

        private void ApplyInnerPadding(int horizontal)
        {
            if (_layout == null || _layout.padding.left == horizontal)
                return;

            _layout.padding = new RectOffset(horizontal, horizontal, 10, 10);
        }

        /// <summary>
        /// Projects a rect's bottom-left and top-right corner into the overlay's parent space.
        /// Corners come from <see cref="Transform.TransformPoint(Vector3)"/> rather than
        /// <c>GetWorldCorners</c>: under IL2CPP the array argument is copied into interop memory and
        /// never written back, so the managed array stays all-zero and the panel lands off-screen.
        /// Returns false for a degenerate rect (layout not built yet) so callers keep their fallback.
        /// </summary>
        private static bool TryGetLocalEdges(
            RectTransform target,
            RectTransform space,
            Camera eventCam,
            out Vector2 min,
            out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;

            Rect rect = target.rect;
            Vector3 worldBL = target.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f));
            Vector3 worldTR = target.TransformPoint(new Vector3(rect.xMax, rect.yMax, 0f));

            Vector2 screenBL = RectTransformUtility.WorldToScreenPoint(eventCam, worldBL);
            Vector2 screenTR = RectTransformUtility.WorldToScreenPoint(eventCam, worldTR);

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(space, screenBL, eventCam, out Vector2 localBL)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(space, screenTR, eventCam, out Vector2 localTR))
                return false;

            min = new Vector2(Mathf.Min(localBL.x, localTR.x), Mathf.Min(localBL.y, localTR.y));
            max = new Vector2(Mathf.Max(localBL.x, localTR.x), Mathf.Max(localBL.y, localTR.y));
            return max.x - min.x >= 1f && max.y - min.y >= 1f;
        }

        /// <summary>
        /// GetComponent instead of a cast: under IL2CPP a managed <c>as</c> on an interop wrapper
        /// hands back null even when the object really is a <see cref="RectTransform"/>.
        /// </summary>
        private static RectTransform AsRect(Transform transform) =>
            transform != null ? transform.GetComponent<RectTransform>() : null;

        private void SyncFontsFromInput()
        {
            TextMeshProUGUI source = ResolveInputText();
            if (source == null || source.font == null)
                return;

            ApplyFont(_header, source);
            ApplyFont(_helper, source);
            ApplyFont(_measure, source);
            ApplyFont(_scrollUp, source);
            ApplyFont(_scrollDown, source);
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

        /// <summary>Thin rule between the description block and the rows, the panel's own hr.</summary>
        private static GameObject CreateSeparator(Transform parent)
        {
            GameObject go = new GameObject("Separator");
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();

            LayoutElement le = go.AddComponent<LayoutElement>();
            le.minHeight = SeparatorBlockHeight;
            le.preferredHeight = SeparatorBlockHeight;
            le.flexibleWidth = 1f;

            GameObject line = new GameObject("Line");
            line.transform.SetParent(go.transform, false);
            RectTransform lineRt = line.AddComponent<RectTransform>();
            lineRt.anchorMin = new Vector2(0f, 0.5f);
            lineRt.anchorMax = new Vector2(1f, 0.5f);
            lineRt.pivot = new Vector2(0.5f, 0.5f);
            lineRt.anchoredPosition = Vector2.zero;
            lineRt.sizeDelta = new Vector2(0f, 1f);

            Image image = line.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.14f);
            image.raycastTarget = false;

            go.SetActive(false);
            return go;
        }

        /// <summary>
        /// Scroll indicator pinned to a corner of the row block instead of riding along in a row's text.
        /// </summary>
        private static TextMeshProUGUI CreateScrollCue(
            Transform parent,
            string name,
            string glyph,
            Vector2 corner)
        {
            TextMeshProUGUI tmp = CreateText(parent, name, RowFontSize);
            tmp.text = glyph;
            tmp.color = new Color(0.42f, 0.45f, 0.5f, 1f);
            tmp.alignment = TextAlignmentOptions.Right;
            IgnoreLayout(tmp);

            RectTransform rt = tmp.rectTransform;
            rt.anchorMin = corner;
            rt.anchorMax = corner;
            rt.pivot = corner;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(ScrollCueWidth, RowFontSize + 6f);

            tmp.gameObject.SetActive(false);
            return tmp;
        }

        /// <summary>Invisible, layout-exempt label used only to measure header widths.</summary>
        private static TextMeshProUGUI CreateMeasureProbe(Transform parent)
        {
            TextMeshProUGUI tmp = CreateText(parent, "IndentProbe", HeaderFontSize);
            tmp.color = new Color(1f, 1f, 1f, 0f);
            IgnoreLayout(tmp);
            tmp.rectTransform.sizeDelta = Vector2.zero;
            return tmp;
        }

        private static void IgnoreLayout(TextMeshProUGUI tmp)
        {
            LayoutElement le = tmp != null ? tmp.GetComponent<LayoutElement>() : null;
            if (le != null)
                le.ignoreLayout = true;
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
