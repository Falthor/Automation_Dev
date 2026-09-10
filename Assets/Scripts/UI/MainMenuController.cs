using System.Collections.Generic;
using Game.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Home screen (MainMenu.unity): New Game asks what to call the run, writes an initial save
    /// and enters Play mode via the Intro scene's crawl (Intro.unity, skippable, plays once per New
    /// Game click); Load picks one of the saves on disk and enters Play mode directly, skipping
    /// Intro.
    ///
    /// <b>Named saves, one folder each</b> (<see cref="SaveService"/>). A new game whose name is
    /// already taken asks before overwriting that one - the confirmation is now about a specific
    /// save rather than about the only one there is. Load is disabled while the disk holds none.
    ///
    /// It also carries the shortcuts screen (<see cref="ShortcutsPanel"/>), which lives here rather
    /// than in the game because a keyboard layout is not part of a run: it has to be reachable before
    /// there is a world, and it survives starting a new one.
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] Texture2D menuBackground;
        [SerializeField] string introSceneName = "Intro";
        [SerializeField] string bootstrapSceneName = "Bootstrap";

        Button _newGameButton;
        Button _loadButton;
        Button _shortcutsButton;

        /// <summary>
        /// Owns the shortcuts overlay. A plain object rather than a second component: its markup is
        /// already in this screen's UXML, so it needs the element and nothing else - no scene wiring,
        /// no serialized reference.
        /// </summary>
        ShortcutsPanel _shortcuts;
        VisualElement _confirmOverlay;
        Button _confirmCancelButton;
        Button _confirmAcceptButton;
        Label _confirmMessage;

        VisualElement _nameOverlay;
        TextField _nameField;
        Button _nameCancelButton;
        Button _nameAcceptButton;

        VisualElement _loadOverlay;
        ScrollView _loadList;
        Button _loadCancelButton;

        /// <summary>The name waiting on the overwrite confirmation - the dialog itself carries no state.</summary>
        string _pendingName;

        void Start()
        {
            VisualElement root = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(root);
            root.StretchToParentSize();

            if (menuBackground != null)
            {
                root.Q<VisualElement>("MainMenuBackground").style.backgroundImage = new StyleBackground(menuBackground);
            }

            _newGameButton = root.Q<Button>("NewGameButton");
            _loadButton = root.Q<Button>("LoadButton");
            _shortcutsButton = root.Q<Button>("ShortcutsButton");
            _confirmOverlay = root.Q<VisualElement>("ConfirmOverlay");
            _confirmCancelButton = root.Q<Button>("ConfirmCancelButton");
            _confirmAcceptButton = root.Q<Button>("ConfirmAcceptButton");
            _confirmMessage = root.Q<Label>("ConfirmMessage");

            _nameOverlay = root.Q<VisualElement>("NameOverlay");
            _nameField = root.Q<TextField>("NameField");
            _nameCancelButton = root.Q<Button>("NameCancelButton");
            _nameAcceptButton = root.Q<Button>("NameAcceptButton");

            _loadOverlay = root.Q<VisualElement>("LoadOverlay");
            _loadList = root.Q<ScrollView>("LoadList");
            _loadCancelButton = root.Q<Button>("LoadCancelButton");

            _loadButton.SetEnabled(SaveService.List().Count > 0);

            _shortcuts = new ShortcutsPanel(root.Q<VisualElement>("ShortcutsOverlay"));

            _newGameButton.clicked += OnNewGameClicked;
            _loadButton.clicked += OnLoadClicked;
            _shortcutsButton.clicked += _shortcuts.Show;
            _confirmCancelButton.clicked += HideConfirm;
            _confirmAcceptButton.clicked += OnConfirmOverwrite;
            _nameCancelButton.clicked += HideName;
            _nameAcceptButton.clicked += OnNameAccepted;
            _loadCancelButton.clicked += HideLoad;
        }

        /// <summary>Asks for the name first: it decides which folder the run's saves go to, so it cannot be inferred later.</summary>
        void OnNewGameClicked()
        {
            _nameField.value = SuggestName();
            _nameOverlay.RemoveFromClassList("hidden");
            _nameField.Focus();
        }

        /// <summary>
        /// A name that is free, so the common case needs no thought and no overwrite warning.
        /// "Partie", then "Partie 2", "Partie 3" - it stops at a bound rather than looping forever
        /// on a disk that somehow refuses every name.
        /// </summary>
        static string SuggestName()
        {
            if (!SaveService.Exists(SaveService.DefaultName)) return SaveService.DefaultName;

            for (var i = 2; i <= 99; i++)
            {
                string candidate = SaveService.DefaultName + " " + i;
                if (!SaveService.Exists(candidate)) return candidate;
            }

            return SaveService.DefaultName;
        }

        void OnNameAccepted()
        {
            _pendingName = SaveService.Sanitise(_nameField.value);
            HideName();

            // Overwriting is now about one named save rather than about the only one there is, so
            // the message has to say which.
            if (SaveService.Exists(_pendingName))
            {
                _confirmMessage.text = $"Une sauvegarde nommée « {_pendingName} » existe déjà. "
                                       + "Commencer une nouvelle partie l'écrasera définitivement.";
                _confirmOverlay.RemoveFromClassList("hidden");
                return;
            }

            StartNewGame();
        }

        void HideName() => _nameOverlay.AddToClassList("hidden");

        void HideLoad() => _loadOverlay.AddToClassList("hidden");

        void OnConfirmOverwrite()
        {
            HideConfirm();
            StartNewGame();
        }

        void HideConfirm()
        {
            _confirmOverlay.AddToClassList("hidden");
        }

        void StartNewGame()
        {
            PendingGameStart.RequestNewGame(_pendingName);
            SceneManager.LoadScene(introSceneName);
        }

        /// <summary>Lists what is on disk, rebuilt on every open - a save could have been written or removed since the screen was built.</summary>
        void OnLoadClicked()
        {
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
                row.AddToClassList("main-menu-button");
                row.AddToClassList("save-row");
                _loadList.Add(row);
            }

            _loadOverlay.RemoveFromClassList("hidden");
        }

        void LoadNamed(string name)
        {
            SaveData data = SaveService.Load(name);

            // The row was built from a listing; a null read here means that save vanished or failed
            // to parse between the listing and this click - fail safe (stay on the menu, drop the
            // stale row) rather than send GameRuntime into Awake() with nothing to load.
            if (data == null)
            {
                OnLoadClicked();
                return;
            }

            PendingGameStart.RequestLoadGame(data, name);
            SceneManager.LoadScene(bootstrapSceneName);
        }
    }
}
