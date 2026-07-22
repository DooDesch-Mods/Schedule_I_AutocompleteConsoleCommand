using System;
using System.Collections.Generic;
using ConsoleAutocomplete.Autocomplete.ArgProviders;
using ConsoleAutocomplete.Util;
using HarmonyLib;
using UnityEngine;
#if IL2CPP
using GameConsole = Il2CppScheduleOne.Console;
#else
using GameConsole = ScheduleOne.Console;
#endif

namespace ConsoleAutocomplete.Autocomplete
{
    [HarmonyPatch]
    internal static class ConsoleUIPatches
    {
        private static SuggestionOverlay _overlay;
        private static SuggestionEngine.Result _current;
        private static int _selectedIndex;
        private static bool _suppressValueChanged;

        internal static bool SuggestionsActive =>
            _overlay != null && _overlay.IsVisible && _current != null && _current.HasSuggestions;

        internal static bool IsBoundInput(TMP_InputField field) =>
            field != null && _overlay != null && _overlay.BoundInput == field;

        [HarmonyPatch(typeof(ConsoleUI), "Awake")]
        [HarmonyPostfix]
        private static void AwakePostfix(ConsoleUI __instance)
        {
            try
            {
                if (__instance?.InputField == null)
                    return;

                _overlay ??= new SuggestionOverlay();
                _overlay.Attach(__instance);
                __instance.InputField.onValueChanged.AddListener(
                    UnityEvents.Action<string>(text => OnValueChanged(__instance, text)));
            }
            catch (Exception ex)
            {
                ModLog.ErrorOnce("console-awake", "ConsoleUI Awake hook failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(ConsoleUI), nameof(ConsoleUI.SetIsOpen))]
        [HarmonyPostfix]
        private static void SetIsOpenPostfix(ConsoleUI __instance, bool open)
        {
            try
            {
                if (!open)
                {
                    _overlay?.Hide();
                    _current = null;
                    _selectedIndex = 0;
                    return;
                }

                UsageStats.EnsureLoadedForCurrentSave();
                CommandIndex.EnsureBuilt();
                Refresh(__instance, __instance.InputField != null ? __instance.InputField.text : string.Empty);
            }
            catch (Exception ex)
            {
                ModLog.Warning("ConsoleUI SetIsOpen hook failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(ConsoleUI), "Update")]
        [HarmonyPostfix]
        private static void UpdatePostfix(ConsoleUI __instance)
        {
            try
            {
                if (__instance?.canvas == null || !__instance.canvas.enabled)
                    return;

                if (__instance.InputField == null)
                    return;

                bool suggestionsOpen = SuggestionsActive;

                if (suggestionsOpen && Input.GetKeyDown(KeyCode.Tab))
                {
                    ApplySelection(__instance);
                    return;
                }

                if (suggestionsOpen && Input.GetKeyDown(KeyCode.UpArrow))
                {
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                    Refresh(__instance, __instance.InputField.text, preserveSelection: true);
                    PinCaret(__instance.InputField);
                    return;
                }

                if (suggestionsOpen && Input.GetKeyDown(KeyCode.DownArrow))
                {
                    _selectedIndex = Math.Min(
                        (_current?.Suggestions?.Count ?? 1) - 1,
                        _selectedIndex + 1);
                    Refresh(__instance, __instance.InputField.text, preserveSelection: true);
                    PinCaret(__instance.InputField);
                }
            }
            catch (Exception ex)
            {
                ModLog.ErrorOnce("console-update", "ConsoleUI Update hook failed: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(ConsoleUI), "UpdateCommandHistory")]
        [HarmonyPrefix]
        private static bool UpdateCommandHistoryPrefix()
        {
            return !SuggestionsActive;
        }

        private static void OnValueChanged(ConsoleUI ui, string text)
        {
            if (_suppressValueChanged)
                return;

            _selectedIndex = 0;
            Refresh(ui, text);
        }

        private static void Refresh(ConsoleUI ui, string text, bool preserveSelection = false)
        {
            if (ui?.InputField == null)
                return;

            CommandIndex.EnsureBuilt();
            int caret = ui.InputField.caretPosition;
            int selected = preserveSelection ? _selectedIndex : 0;
            _current = SuggestionEngine.Build(text, caret, selected);
            _selectedIndex = _current.SelectedIndex;
            _overlay?.Render(_current, text);
        }

        private static void ApplySelection(ConsoleUI ui)
        {
            if (ui?.InputField == null || _current?.Selected == null)
                return;

            string next = SuggestionEngine.ApplySelection(ui.InputField.text, _current);
            _suppressValueChanged = true;
            try
            {
                ui.InputField.SetTextWithoutNotify(next);
                PinCaret(ui.InputField);
            }
            finally
            {
                _suppressValueChanged = false;
            }

            _selectedIndex = 0;
            Refresh(ui, next);
        }

        private static void PinCaret(TMP_InputField field)
        {
            if (field == null)
                return;

            int end = field.text != null ? field.text.Length : 0;
            field.caretPosition = end;
            field.selectionAnchorPosition = end;
            field.selectionFocusPosition = end;
        }
    }

    /// <summary>
    /// Blocks TMP's MoveUp/MoveDown from yanking the caret while suggestions are open.
    /// </summary>
    [HarmonyPatch]
    internal static class TmpInputNavigationPatches
    {
        [HarmonyPatch(typeof(TMP_InputField), "MoveUp", new Type[] { typeof(bool) })]
        [HarmonyPrefix]
        private static bool MoveUpBoolPrefix(TMP_InputField __instance) =>
            AllowVerticalMove(__instance);

        [HarmonyPatch(typeof(TMP_InputField), "MoveUp", new Type[] { typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        private static bool MoveUpBoolBoolPrefix(TMP_InputField __instance) =>
            AllowVerticalMove(__instance);

        [HarmonyPatch(typeof(TMP_InputField), "MoveDown", new Type[] { typeof(bool) })]
        [HarmonyPrefix]
        private static bool MoveDownBoolPrefix(TMP_InputField __instance) =>
            AllowVerticalMove(__instance);

        [HarmonyPatch(typeof(TMP_InputField), "MoveDown", new Type[] { typeof(bool), typeof(bool) })]
        [HarmonyPrefix]
        private static bool MoveDownBoolBoolPrefix(TMP_InputField __instance) =>
            AllowVerticalMove(__instance);

        [HarmonyPatch(typeof(TMP_InputField), "MoveTextStart")]
        [HarmonyPrefix]
        private static bool MoveTextStartPrefix(TMP_InputField __instance) =>
            AllowVerticalMove(__instance);

        private static bool AllowVerticalMove(TMP_InputField field)
        {
            if (!ConsoleUIPatches.SuggestionsActive)
                return true;

            if (!ConsoleUIPatches.IsBoundInput(field))
                return true;

            // Block TMP caret navigation; ConsoleUIPatches owns Up/Down for suggestions.
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ConsoleLifecyclePatches
    {
        [HarmonyPatch(typeof(GameConsole), "Awake")]
        [HarmonyPostfix]
        private static void ConsoleAwakePostfix()
        {
            CommandIndex.MarkDirty();
            CommandIndex.Rebuild();
        }

#if MONO
        [HarmonyPatch(typeof(GameConsole), nameof(GameConsole.SubmitCommand), new Type[] { typeof(List<string>) })]
        [HarmonyPostfix]
        private static void SubmitCommandPostfixMono(List<string> args)
        {
            RecordSubmit(args);
        }
#else
        [HarmonyPatch(typeof(GameConsole), nameof(GameConsole.SubmitCommand), new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
        [HarmonyPostfix]
        private static void SubmitCommandPostfixIl2Cpp(Il2CppSystem.Collections.Generic.List<string> args)
        {
            var tokens = new List<string>();
            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    string value = args[i];
                    if (!string.IsNullOrWhiteSpace(value))
                        tokens.Add(value.Trim().ToLowerInvariant());
                }
            }

            RecordSubmit(tokens);
        }
#endif

        private static void RecordSubmit(List<string> tokens)
        {
            try
            {
                if (tokens == null || tokens.Count == 0)
                    return;

                UsageStats.EnsureLoadedForCurrentSave();
                UsageStats.RecordCommandLine(tokens);
                // Persisted on game Save (SaveManager), not on every console submit.
            }
            catch (Exception ex)
            {
                ModLog.Debug("SubmitCommand usage record failed: " + ex.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class RegistryPatches
    {
        [HarmonyPatch(typeof(Registry), nameof(Registry.AddToRegistry))]
        [HarmonyPostfix]
        private static void AddToRegistryPostfix(ItemDefinition item)
        {
            try
            {
                if (item == null || string.IsNullOrWhiteSpace(item.ID))
                    return;

                string source = ModAttribution.LabelFromStackTrace(2);
                if (source == ModAttribution.UnknownLabel)
                    source = "Mod";

                ArgProviderRegistry.RememberItemSource(item.ID, source);
            }
            catch (Exception ex)
            {
                ModLog.Debug("AddToRegistry attribution failed: " + ex.Message);
            }
        }
    }
}
