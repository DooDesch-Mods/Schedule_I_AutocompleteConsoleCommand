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

        /// <summary>
        /// The prompt the current suggestion list was built for.
        ///
        /// Not every onValueChanged means the player typed. The dead-key mark from the toggle key is delivered
        /// whenever the next input event happens - which can be a press of an ARROW key, long after the console
        /// opened - and taking it back off writes to the field, which raises another change for a prompt that ends
        /// up exactly as it was. Handled blindly, that resets the highlight the arrow had just moved: the list read
        /// `unbind` and Tab entered `give`.
        /// </summary>
        private static string _lastText = string.Empty;

        /// <summary>
        /// True from the moment the console opens until the first character lands in the prompt.
        ///
        /// The key that opens the console is a DEAD KEY on several layouts - `^` on German and Swiss keyboards,
        /// `´` on others. A dead key emits nothing of its own when pressed: the system holds the mark and hands
        /// it to the next keystroke. So the console opens on `^`, the player types `help`, and the prompt reads
        /// `^help` - or `âdd`, when the next letter is a vowel the mark composes with.
        ///
        /// Vanilla cannot catch this. It clears the field in SetIsOpen (ScheduleOne.UI/ConsoleUI.cs:107) BEFORE
        /// the mark arrives, and once it arrives nothing distinguishes it from typing.
        /// </summary>
        private static bool _awaitingFirstChar;

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
                    _awaitingFirstChar = false;
                    _repeatKey = KeyCode.None;
                    _lastText = string.Empty;
                    return;
                }

                // Vanilla has just emptied the field, so whatever arrives next is the first thing the
                // player typed - and possibly the toggle key's dead-key mark riding along with it.
                _awaitingFirstChar = true;

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

            if (_awaitingFirstChar)
            {
                _awaitingFirstChar = false;
                text = DropPendingDeadKey(ui, text);
            }

            // Nothing the player did survived: the whole change was a stray mark being taken off again, and the
            // list already stands for this prompt. Rebuilding it here would throw away a selection the arrow keys
            // had moved a moment earlier - which is exactly what the dead key's late arrival used to do.
            if (string.Equals(text, _lastText, StringComparison.Ordinal))
            {
                ModLog.Debug("prompt unchanged after the dead key came off - keeping the selection");
                return;
            }

            ModLog.Debug("onValueChanged: '" + text + "'");
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
        /// The marks a dead key leaves behind on their own, when the layout could not compose them with
        /// the letter that followed: circumflex, grave, acute, tilde.
        /// </summary>
        private const string DeadKeyMarks = "^`\u00B4~";   // ^ ` ´ ~

        /// <summary>
        /// Composed characters a dead key produces, paired index-for-index with the letter underneath.
        /// Circumflex first (the German and Swiss console key), then grave, acute and tilde, because
        /// those sit under the console toggle on French, Spanish and Portuguese layouts.
        ///
        /// Written as escapes rather than as the characters themselves so the table cannot be silently
        /// mangled by a tool that guesses this file's encoding wrong.
        /// </summary>
        private const string ComposedChars =
            "\u00E2\u00EA\u00EE\u00F4\u00FB\u00C2\u00CA\u00CE\u00D4\u00DB"    // â ê î ô û Â Ê Î Ô Û
            + "\u00E0\u00E8\u00EC\u00F2\u00F9\u00C0\u00C8\u00CC\u00D2\u00D9"  // à è ì ò ù À È Ì Ò Ù
            + "\u00E1\u00E9\u00ED\u00F3\u00FA\u00C1\u00C9\u00CD\u00D3\u00DA"  // á é í ó ú Á É Í Ó Ú
            + "\u00E3\u00F1\u00F5\u00C3\u00D1\u00D5";                         // ã ñ õ Ã Ñ Õ

        private const string BaseChars =
            "aeiouAEIOU"
            + "aeiouAEIOU"
            + "aeiouAEIOU"
            + "anoANO";

        /// <summary>
        /// Takes the toggle key's pending dead-key mark off the front of a freshly opened prompt.
        ///
        /// Only ever looks at the FIRST character of the FIRST input after opening, which is the only
        /// place a pending mark can land - so a `^` typed anywhere else, at any later moment, is left
        /// alone. Nothing in the command set starts with one of these characters.
        ///
        /// The composed case has to put the letter back rather than drop the character: `^` followed by
        /// `a` arrives as a single `â`, and deleting it would eat the first letter of the command.
        /// </summary>
        private static string DropPendingDeadKey(ConsoleUI ui, string text)
        {
            if (ui?.InputField == null || string.IsNullOrEmpty(text))
                return text;

            char first = text[0];
            string fixedText;

            if (DeadKeyMarks.IndexOf(first) >= 0)
            {
                fixedText = text.Substring(1);
            }
            else
            {
                int composed = ComposedChars.IndexOf(first);
                if (composed < 0)
                    return text;

                fixedText = BaseChars[composed] + text.Substring(1);
            }

            ModLog.Debug("dead key off the prompt: '" + text + "' -> '" + fixedText + "'");

            _suppressValueChanged = true;
            try
            {
                ui.InputField.SetTextWithoutNotify(fixedText);
                PinCaret(ui.InputField);
            }
            finally
            {
                _suppressValueChanged = false;
            }

            return fixedText;
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
            _lastText = text ?? string.Empty;
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
