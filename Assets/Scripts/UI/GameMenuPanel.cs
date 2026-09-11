using System.Collections.Generic;
using Game.Presentation;
using Game.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The in-game menu behind the Top Bar's Menu button: save, load, options, quit.
    ///
    /// A plain object rather than a component, the same shape as <see cref="ShortcutsPanel"/>: its
    /// markup is already in the Top Bar's UXML, so it needs the element and the two things only the
    /// game can give it - the runtime that knows how to write a save, and the shortcuts screen that
    /// Options hands over to.
    ///
    /// <b>Saving and loading are named</b> (<see cref="SaveService"/>): one folder per name under
    /// the persistent data path. Saving prefills the name the session is already using, so the
    /// ordinary gesture overwrites your own run rather than quietly making a second copy of it.
    /// </summary>
    sealed class GameMenuPanel
    {
        /// <summary>
        /// Loading from inside the game re-enters this same scene, which is where GameRuntime.Awake
        /// reads PendingGameStart. Named rather than serialized because this is a plain object with
        /// no inspector; docs/BUILD.md already requires the scene to be in the build under this
        /// name, so a rename would break the build before it broke this.
        /// </summary>
        const string BootstrapSceneName = "Bootstrap";

        readonly VisualElement _overlay;
        readonly VisualElement _saveDialog;
        readonly VisualElement _loadDialog;
        readonly TextField _saveName;
        readonly Label _saveNotice;
        readonly ScrollView _loadList;
        readonly GameRuntime _runtime;
        readonly ShortcutsPanel _shortcuts;

        public GameMenuPanel(VisualElement overlay, GameRuntime runtime, ShortcutsPanel shortcuts)
        {
            _overlay = overlay;
            _runtime = runtime;
            _shortcuts = shortcuts;

            _saveDialog = overlay.Q<VisualElement>("GameMenuSaveDialog");
            _loadDialog = overlay.Q<VisualElement>("GameMenuLoadDialog");
            _saveName = overlay.Q<TextField>("GameMenuSaveName");
            _saveNotice = overlay.Q<Label>("GameMenuSaveNotice");
            _loadList = overlay.Q<ScrollView>("GameMenuLoadList");

            overlay.Q<Button>("GameMenuSave").clicked += ShowSaveDialog;
            overlay.Q<Button>("GameMenuLoad").clicked += ShowLoadDialog;
            overlay.Q<Button>("GameMenuOptions").clicked += ShowOptions;
            overlay.Q<Button>("GameMenuQuit").clicked += Quit;
            overlay.Q<Button>("GameMenuClose").clicked += Hide;

            overlay.Q<Button>("GameMenuSaveConfirm").clicked += ConfirmSave;
            overlay.Q<Button>("GameMenuSaveCancel").clicked += HideDialogs;
            overlay.Q<Button>("GameMenuLoadCancel").clicked += HideDialogs;
        }

        public bool IsOpen => !_overlay.ClassListContains("hidden");

        public void Show()
        {
            HideDialogs();
            _overlay.RemoveFromClassList("hidden");
        }

        public void Hide()
        {
            HideDialogs();
            _overlay.AddToClassList("hidden");
        }

        void HideDialogs()
        {
            _saveDialog.AddToClassList("hidden");
            _loadDialog.AddToClassList("hidden");
            if (_saveNotice != null) _saveNotice.text = string.Empty;
        }

        /// <summary>Prefilled with the name this session already writes to - saving over your own run is the ordinary case, and a second copy under a new name is the deliberate one.</summary>
        void ShowSaveDialog()
        {
            HideDialogs();
            _saveName.value = _runtime != null ? _runtime.CurrentSaveName : SaveService.DefaultName;
            _saveDialog.RemoveFromClassList("hidden");
            _saveName.Focus();
        }

        void ConfirmSave()
        {
            if (_runtime == null) return;

            string name = SaveService.Sanitise(_saveName.value);

            // Said rather than assumed: a write can fail on a locked file or a full disk, and a
            // menu that closes cheerfully on a failed save is how a run gets lost.
            if (_runtime.SaveAs(name))
            {
                _saveNotice.text = $"Sauvegardé sous « {name} ».";
                _saveDialog.AddToClassList("hidden");
                return;
            }

            _saveNotice.text = "La sauvegarde a échoué. Voir la console pour la raison.";
        }

        /// <summary>Rebuilt on every open: a save could have been written since the panel was built - by this very session.</summary>
        void ShowLoadDialog()
        {
            HideDialogs();
            _loadList.Clear();

            IReadOnlyList<string> names = SaveService.List();
            if (names.Count == 0)
            {
                var empty = new Label("Aucune sauvegarde sur ce poste.");
                empty.AddToClassList("save-list-empty");
                _loadList.Add(empty);
            }

            foreach (string name in names)
            {
                string chosen = name; // captured per row, not by reference to the loop
                var row = new Button(() => LoadNamed(chosen)) { text = name };
                row.AddToClassList("game-menu-button");
                row.AddToClassList("save-row");
                _loadList.Add(row);
            }

            _loadDialog.RemoveFromClassList("hidden");
        }

        void LoadNamed(string name)
        {
            SaveData data = SaveService.Load(name);

            // The row came from a listing; a null read means that save vanished or failed to parse
            // since. Fail safe - rebuild the list rather than reload the scene with nothing to
            // restore, which would drop the running game for an empty one.
            if (data == null)
            {
                ShowLoadDialog();
                return;
            }

            // Leaving the menu paused would carry a zero time scale into the scene that replaces it.
            Time.timeScale = 1f;

            PendingGameStart.RequestLoadGame(data, name);
            SceneManager.LoadScene(BootstrapSceneName);
        }

        void ShowOptions()
        {
            Hide();
            _shortcuts?.Show();
        }

        /// <summary>Does nothing in the Editor, by design - Application.Quit is a no-op there, and the build is where the button matters.</summary>
        static void Quit() => Application.Quit();
    }
}
