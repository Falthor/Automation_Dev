using Game.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Home screen (MainMenu.unity): New Game writes an initial save and enters Play mode; Load
    /// restores the existing save and enters Play mode. Mono-save system (CONTRACTS.md §14) - one
    /// fixed save file, so New Game over an existing save asks for confirmation before overwriting
    /// it, and Load is disabled whenever no save exists.
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] Texture2D menuBackground;
        [SerializeField] string bootstrapSceneName = "Bootstrap";

        Button _newGameButton;
        Button _loadButton;
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
            _confirmOverlay = root.Q<VisualElement>("ConfirmOverlay");
            _confirmCancelButton = root.Q<Button>("ConfirmCancelButton");
            _confirmAcceptButton = root.Q<Button>("ConfirmAcceptButton");

            _loadButton.SetEnabled(SaveService.SaveExists());

            _newGameButton.clicked += OnNewGameClicked;
            _loadButton.clicked += OnLoadClicked;
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
            SceneManager.LoadScene(bootstrapSceneName);
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
