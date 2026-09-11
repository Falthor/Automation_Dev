using Game.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Home screen (MainMenu.unity): New Game writes an initial save and enters Play mode via the
    /// Intro scene's crawl (Intro.unity, skippable, plays once per New Game click); Load restores
    /// the existing save and enters Play mode directly, skipping Intro. Mono-save system
    /// (CONTRACTS.md §14) - one fixed save file, so New Game over an existing save asks for
    /// confirmation before overwriting it, and Load is disabled whenever no save exists.
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

            _loadButton.SetEnabled(SaveService.SaveExists());

            _shortcuts = new ShortcutsPanel(root.Q<VisualElement>("ShortcutsOverlay"));

            _newGameButton.clicked += OnNewGameClicked;
            _loadButton.clicked += OnLoadClicked;
            _shortcutsButton.clicked += _shortcuts.Show;
            _confirmCancelButton.clicked += HideConfirm;
            _confirmAcceptButton.clicked += OnConfirmOverwrite;
        }

        void OnNewGameClicked()
        {
            if (SaveService.SaveExists())
            {
                _confirmOverlay.RemoveFromClassList("hidden");
            }
            else
            {
                StartNewGame();
            }
        }

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
            PendingGameStart.RequestNewGame();
            SceneManager.LoadScene(introSceneName);
        }

        void OnLoadClicked()
        {
            SaveData data = SaveService.Load();
            // The Load button is disabled whenever no save exists; a null read here means the
            // file vanished or failed to parse between that check and this click - fail safe
            // (stay on the menu) rather than send GameRuntime into Awake() with nothing to load.
            if (data == null) return;

            PendingGameStart.RequestLoadGame(data);
            SceneManager.LoadScene(bootstrapSceneName);
        }
    }
}
