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
        private static bool _listenerWired;
        private static ConsoleUI _wiredUi;

        internal static bool SuggestionsActive =>
            _overlay != null && _overlay.IsVisible && _current != null && _current.HasSuggestions;

        internal static bool HelperActive =>
            _overlay != null && _overlay.IsVisible && _current != null && _current.HasHelper;

        internal static bool IsBoundInput(TMP_InputField field) =>
            field != null && _overlay != null && _overlay.BoundInput == field;

        [HarmonyPatch(typeof(ConsoleUI), "Awake")]
        [HarmonyPostfix]
        private static void AwakePostfix(ConsoleUI __instance)
        {
            try
            {
                ModLog.Debug("ConsoleUI.Awake postfix.");
                EnsureWired(__instance);
            }
            catch (Exception ex)
            {
                ModLog.ErrorOnce("console-awake", "ConsoleUI Awake hook failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(ConsoleUI), nameof(ConsoleUI.SetIsOpen))]
        [HarmonyPostfix]
        private static void SetIsOpenPostfix(ConsoleUI __instance, bool open)
        {
            try
            {
                ModLog.Debug("ConsoleUI.SetIsOpen(" + open + ").");

                // Awake may have run before our patches on IL2CPP - wire on first open.
                EnsureWired(__instance);

                if (!open)
                {
                    _overlay?.Hide();
                    _current = null;
                    _selectedIndex = 0;
                    return;
                }

                UsageStats.EnsureLoadedForCurrentSave();
                CommandIndex.MarkDirty();
                CommandIndex.EnsureBuilt();
                Refresh(
                    __instance,
                    __instance.InputField != null ? __instance.InputField.text : string.Empty);
            }
            catch (Exception ex)
            {
                ModLog.Warning("ConsoleUI SetIsOpen hook failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(ConsoleUI), "Update")]
        [HarmonyPostfix]
        private static void UpdatePostfix(ConsoleUI __instance)
        {
            try
            {
                if (!IsConsoleCanvasEnabled(__instance))
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
                    MoveSelection(__instance, -1);
                    return;
                }

                if (suggestionsOpen && Input.GetKeyDown(KeyCode.DownArrow))
                {
                    MoveSelection(__instance, 1);
                }
            }
            catch (Exception ex)
            {
                ModLog.ErrorOnce("console-update", "ConsoleUI Update hook failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(ConsoleUI), "UpdateCommandHistory")]
        [HarmonyPrefix]
        private static bool UpdateCommandHistoryPrefix()
        {
            return !SuggestionsActive;
        }

        private static void EnsureWired(ConsoleUI ui)
        {
            if (ui == null || ui.InputField == null)
            {
                ModLog.Debug("EnsureWired skipped: ui or InputField null.");
                return;
            }

            _overlay ??= new SuggestionOverlay();
            _overlay.Attach(ui);

            if (_listenerWired && _wiredUi == ui)
                return;

            ui.InputField.onValueChanged.AddListener(
                UnityEvents.Action<string>(text => OnValueChanged(ui, text)));
            _listenerWired = true;
            _wiredUi = ui;
            ModLog.Debug("Wired onValueChanged + overlay to ConsoleUI.");
        }

        private static bool IsConsoleCanvasEnabled(ConsoleUI ui)
        {
            if (ui == null)
                return false;

            try
            {
                if (ui.canvas != null)
                    return ui.canvas.enabled;
            }
            catch
            {
                // ignored
            }

            try
            {
                return ui.Container != null && ui.Container.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }

        private static void OnValueChanged(ConsoleUI ui, string text)
        {
            if (_suppressValueChanged)
                return;

            ModLog.Debug("onValueChanged: '" + text + "'");
            _selectedIndex = 0;
            Refresh(ui, text);
        }

        /// <summary>
        /// Steps the highlight and wraps around at both ends, so Up on the first entry lands on the
        /// last one instead of getting stuck.
        /// </summary>
        private static void MoveSelection(ConsoleUI ui, int step)
        {
            int count = _current?.Suggestions?.Count ?? 0;
            if (ui?.InputField == null || count <= 0)
                return;

            _selectedIndex = ((_selectedIndex + step) % count + count) % count;
            Refresh(ui, ui.InputField.text, preserveSelection: true);
            PinCaret(ui.InputField);
        }

        private static void Refresh(ConsoleUI ui, string text, bool preserveSelection = false)
        {
            if (ui?.InputField == null)
                return;

            EnsureWired(ui);
            CommandIndex.EnsureBuilt();
            int caret = ui.InputField.caretPosition;
            int selected = preserveSelection ? _selectedIndex : 0;
            _current = SuggestionEngine.Build(text, caret, selected);
            _selectedIndex = _current.SelectedIndex;

            ModLog.Debug(
                "Refresh suggestions="
                + (_current.Suggestions?.Count ?? 0)
                + " helper="
                + _current.HasHelper
                + " header='"
                + _current.StructureHeader
                + "' indexedCommands="
                + CommandIndex.Commands.Count);

            _overlay?.Render(_current, text);
        }

        private static void ApplySelection(ConsoleUI ui)
        {
            if (ui?.InputField == null || _current?.Selected == null)
                return;

            string next = SuggestionEngine.ApplySelection(ui.InputField.text, _current);
            ModLog.Debug("Tab apply → '" + next + "'");
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
            ModLog.Debug("Game Console.Awake - rebuilding command index.");
            CommandIndex.MarkDirty();
            CommandIndex.Rebuild();
        }

#if MONO
        [HarmonyPatch(typeof(GameConsole), nameof(GameConsole.SubmitCommand), new Type[] { typeof(List<string>) })]
        [HarmonyPrefix]
        private static void SubmitCommandPrefixMono(List<string> args)
        {
            RecordSubmit(CopyTokens(args));
        }
#else
        [HarmonyPatch(typeof(GameConsole), nameof(GameConsole.SubmitCommand), new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
        [HarmonyPrefix]
        private static void SubmitCommandPrefixIl2Cpp(Il2CppSystem.Collections.Generic.List<string> args)
        {
            RecordSubmit(CopyTokens(args));
        }
#endif

        private static List<string> CopyTokens(object args)
        {
            var tokens = new List<string>();
            if (args == null)
                return tokens;

#if IL2CPP
            if (args is Il2CppSystem.Collections.Generic.List<string> il2)
            {
                for (int i = 0; i < il2.Count; i++)
                {
                    string value = il2[i];
                    if (!string.IsNullOrWhiteSpace(value))
                        tokens.Add(value.Trim().ToLowerInvariant());
                }

                return tokens;
            }
#endif
            if (args is List<string> managed)
            {
                for (int i = 0; i < managed.Count; i++)
                {
                    string value = managed[i];
                    if (!string.IsNullOrWhiteSpace(value))
                        tokens.Add(value.Trim().ToLowerInvariant());
                }
            }

            return tokens;
        }

        private static void RecordSubmit(List<string> tokens)
        {
            try
            {
                if (tokens == null || tokens.Count == 0)
                    return;

                ModLog.Debug("SubmitCommand tokens: " + string.Join(" ", tokens));
                UsageStats.EnsureLoadedForCurrentSave();
                UsageStats.RecordCommandLine(tokens);
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
                ModLog.Debug("Item source '" + item.ID + "' ← " + source);
            }
            catch (Exception ex)
            {
                ModLog.Debug("AddToRegistry attribution failed: " + ex.Message);
            }
        }
    }
}
