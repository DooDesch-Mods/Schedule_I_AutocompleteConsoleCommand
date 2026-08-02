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
                    _repeatKey = KeyCode.None;
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

                // Escape closes the console on the FIRST press.
                //
                // Vanilla needs two. Its exit handling gives up while the player is typing
                // (ScheduleOne/GameInput.cs:258 returns early on GameInput.IsTyping), and an open console is by
                // definition typing - so the first Escape never reaches the exit listener ConsoleUI.Awake
                // registered. All it does is take the focus out of the input field, which leaves the console
                // standing there looking unchanged. Only the second press, with IsTyping false, gets through.
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    __instance.SetIsOpen(false);
                    return;
                }

                bool suggestionsOpen = SuggestionsActive;

                if (suggestionsOpen && Input.GetKeyDown(KeyCode.Tab))
                {
                    ApplySelection(__instance);
                    return;
                }

                // Read every frame, not only when the arrows drive the list: the repeat timer has to see the
                // key go up, or the next press would inherit the last one's schedule and fire its whole burst.
                int step = ArrowStep();

                // Both arrows, always, with no test for what is in the prompt. The command history is the bottom
                // of this same list now (SuggestionEngine.AppendHistory), so there is no second list to hand them
                // to and no mode to be in.
                if (suggestionsOpen && step != 0)
                    MoveSelection(__instance, step);
            }
            catch (Exception ex)
            {
                ModLog.ErrorOnce("console-update", "ConsoleUI Update hook failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(ConsoleUI), "UpdateCommandHistory")]
        [HarmonyPrefix]
        private static bool UpdateCommandHistoryPrefix(ConsoleUI __instance, out string __state)
        {
            __state = __instance?.InputField != null ? __instance.InputField.text : null;

            // Vanilla's own history walk is switched off whenever there is a list on screen, because that
            // list already ends with the same history and the arrows are stepping it. Left running, one press
            // would move the selection AND recall a line into the prompt.
            //
            // Still allowed to run when there is nothing to suggest: then it is the only thing the arrows could
            // do, and the mod has no screen up to own them.
            return !SuggestionsActive;
        }

        [HarmonyPatch(typeof(ConsoleUI), "UpdateCommandHistory")]
        [HarmonyPostfix]
        private static void UpdateCommandHistoryPostfix(ConsoleUI __instance, string __state)
        {
            try
            {
                if (__instance?.InputField == null || __state == null)
                    return;

                // Vanilla recalls history with SetTextWithoutNotify, so onValueChanged never fires
                // and the overlay would keep showing suggestions for whatever stood there before.
                string now = __instance.InputField.text;
                if (!string.Equals(now, __state, StringComparison.Ordinal))
                {
                    Refresh(__instance, now);
                }
            }
            catch (Exception ex)
            {
                ModLog.ErrorOnce("console-history", "ConsoleUI UpdateCommandHistory hook failed: " + ex);
            }
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
            ui.InputField.onValidateInput = Validator();
            _listenerWired = true;
            _wiredUi = ui;
            ModLog.Debug("Wired onValueChanged + overlay to ConsoleUI.");
        }

        /// <summary>
        /// The marks a dead key leaves behind on their own, when the layout could not compose them with
        /// the letter that followed: circumflex, grave, acute, tilde.
        /// </summary>
        private const string DeadKeyMarks = "^`\u00B4~";   // ^ ` ´ ~

        /// <summary>
        /// Refuses the console toggle's own mark before it can reach the prompt.
        ///
        /// `^` opens the console on German and Swiss layouts, and it is a dead key: pressing it emits nothing of
        /// its own, and the mark arrives as an input event a moment later - into the prompt vanilla has just
        /// emptied. The player sees `^help` and never typed the `^`.
        ///
        /// REFUSED, NOT DELETED, and the difference is the whole fix. TMP calls this before inserting and drops
        /// the character when the answer is 0 (TMP_InputField.KeyPressed: `if (input != 0) Insert(input)`), so
        /// nothing is written and no caret has to be repaired. Taking the mark out afterwards cannot be made to
        /// work: caretPosition and stringPosition are mapped through textInfo.characterInfo, the RENDERED text,
        /// and a field written with SetTextWithoutNotify still carries the old label - so ClampCaretPos pulls
        /// every position back onto it and the next keystroke lands at the front. That is what turned `give` into
        /// `iveg` through three attempts at fixing it up after the fact.
        ///
        /// Only at position 0. Nothing in the command set begins with one of these marks, and a `^` typed
        /// anywhere else is somebody's argument, not the toggle key.
        /// </summary>
        private static char RejectLeadingDeadKey(string text, int charIndex, char addedChar)
        {
            if (charIndex == 0 && DeadKeyMarks.IndexOf(addedChar) >= 0)
            {
                ModLog.Debug("refused the toggle key's mark '" + addedChar + "' at the front of the prompt");
                return '\0';
            }

            return addedChar;
        }

        /// <summary>
        /// Wraps the validator for the runtime this build targets. Assigning it replaces TMP's own
        /// `characterValidation`, which the console leaves at None - a command line takes any character.
        /// </summary>
        private static TMP_InputField.OnValidateInput Validator()
        {
#if IL2CPP
            // A method group cannot be assigned to an Il2Cpp delegate type: the interop wrapper has to build the
            // native side of it, so the managed one is handed over explicitly.
            return Il2CppInterop.Runtime.DelegateSupport
                .ConvertDelegate<TMP_InputField.OnValidateInput>(
                    (Func<string, int, char, char>)RejectLeadingDeadKey);
#else
            return RejectLeadingDeadKey;
#endif
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

            ModLog.Debug("onValueChanged: '" + text + "' string=" + ui.InputField.stringPosition);
            _selectedIndex = 0;
            Refresh(ui, text);
        }

        // Held-arrow repeat, in seconds. The pause before the first repeat is long enough that a normal
        // tap can never double-step, and the second gear exists because the suggestion list runs to
        // hundreds of entries - at one speed, holding Up is either twitchy on a short list or a wait on
        // a long one. Not read from the OS key-repeat settings: those only reach text fields, and this
        // list is drawn and stepped by the mod.
        private const float RepeatDelay = 0.35f;
        private const float RepeatInterval = 0.06f;
        private const float RepeatSecondGearAfter = 1.2f;
        private const float RepeatSecondGearInterval = 0.03f;

        private static KeyCode _repeatKey = KeyCode.None;
        private static float _repeatHeldSince;
        private static float _repeatNextAt;

        /// <summary>
        /// -1 to step up, +1 to step down, 0 for nothing this frame - including the quiet stretch
        /// between a key going down and its repeat starting.
        ///
        /// Unity's GetKeyDown fires exactly once per press, so holding an arrow moved one entry and then
        /// sat there. Unscaled time, because the console is usable while the game is not running.
        /// </summary>
        private static int ArrowStep()
        {
            KeyCode key = Input.GetKey(KeyCode.UpArrow)
                ? KeyCode.UpArrow
                : Input.GetKey(KeyCode.DownArrow)
                    ? KeyCode.DownArrow
                    : KeyCode.None;

            if (key == KeyCode.None)
            {
                _repeatKey = KeyCode.None;
                return 0;
            }

            float now = Time.unscaledTime;
            int step = key == KeyCode.UpArrow ? -1 : 1;

            // A fresh press, or a reversal while the other arrow is still down: either way the new
            // direction starts its own delay, so turning round never fires a burst.
            if (_repeatKey != key)
            {
                _repeatKey = key;
                _repeatHeldSince = now;
                _repeatNextAt = now + RepeatDelay;
                return step;
            }

            if (now < _repeatNextAt)
                return 0;

            _repeatNextAt = now + (now - _repeatHeldSince >= RepeatSecondGearAfter
                ? RepeatSecondGearInterval
                : RepeatInterval);
            return step;
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
                + "' match="
                + (_current.Selected != null ? _current.Selected.MatchKind.ToString() : "-")
                + " indexedCommands="
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

        /// <summary>
        /// Puts the caret at the end of the text, in BOTH of the two places TextMeshPro keeps it.
        ///
        /// A TMP_InputField carries two cursors: `caretPosition` is where the bar is drawn, and
        /// `stringPosition` is where the next character is actually inserted. They are normally moved
        /// together, and setting only the visible one leaves the field looking right and typing wrong.
        ///
        /// That is what happened after the dead-key mark was taken off the prompt: the caret was drawn
        /// after the first letter while the insertion point was still at 0, so typing `give` produced
        /// `iveg` - the g stayed where it was and everything after it went in front of it.
        /// </summary>
        /// <summary>Puts the caret at the end, after the mod has replaced the whole prompt.</summary>
        private static void PinCaret(TMP_InputField field)
        {
            if (field == null)
                return;

            SetCaret(field, field.text != null ? field.text.Length : 0);
        }

        /// <summary>
        /// Moves the caret, in BOTH of the two places TextMeshPro keeps it.
        ///
        /// A TMP_InputField carries two cursors: `caretPosition` is where the bar is drawn, and `stringPosition`
        /// is where the next character is actually inserted. Setting only the visible one leaves the field looking
        /// right and typing wrong, which is a fault nobody sees until the keystroke after the one that caused it.
        /// </summary>
        private static void SetCaret(TMP_InputField field, int at)
        {
            if (field == null)
                return;

            int max = field.text != null ? field.text.Length : 0;
            at = Mathf.Clamp(at, 0, max);

            // The label first. Both positions are mapped through textInfo.characterInfo, which describes the
            // RENDERED text - after SetTextWithoutNotify that is still the old, shorter one, and ClampCaretPos
            // would drag everything written here back onto it.
            field.ForceLabelUpdate();

            field.caretPosition = at;
            field.selectionAnchorPosition = at;
            field.selectionFocusPosition = at;
            field.stringPosition = at;
            field.selectionStringAnchorPosition = at;
            field.selectionStringFocusPosition = at;
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

                // The same line, kept verbatim for the history block at the bottom of the suggestion list. Recorded
                // from here rather than read out of ConsoleUI's private list, which would need interop reflection
                // for something this hook already has in its hands.
                CommandHistory.Record(string.Join(" ", tokens));
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
