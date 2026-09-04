using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Intro.unity: plays once between MainMenu's New Game click and Genesis.unity (never on Load
    /// - MainMenuController only routes New Game through this scene). Scrolls CrawlText
    /// bottom-to-top at a constant speed via VisualElement.transform.position (a post-layout
    /// offset, not a layout-triggering style change) until it clears the top of the screen, then
    /// fades to black and loads Genesis. Any key or click at any point starts that same fade-out
    /// immediately, skipping the rest of the scroll.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(SceneFadeController))]
    public sealed class IntroController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] string genesisSceneName = "Genesis";
        [SerializeField] float scrollSpeed = 30f;
        [SerializeField] float fadeOutDuration = 0.6f;
        [SerializeField] AudioClip introMusic;

        AudioSource _audioSource;
        SceneFadeController _fade;
        VisualElement _root;
        VisualElement _crawlText;
        float _currentY;
        float _textHeight;
        bool _scrolling;
        bool _leaving;

        void Start()
        {
            _audioSource = GetComponent<AudioSource>();
            if (introMusic != null)
            {
                _audioSource.clip = introMusic;
                _audioSource.Play();
            }

            _fade = GetComponent<SceneFadeController>();

            VisualElement content = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(content);
            content.StretchToParentSize();

            _root = content.Q<VisualElement>("IntroRoot");
            _crawlText = content.Q<VisualElement>("CrawlText");
            _root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
        }

        void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            _root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            _currentY = _root.resolvedStyle.height;
            _textHeight = _crawlText.resolvedStyle.height;
            _crawlText.transform.position = new Vector3(0f, _currentY, 0f);
            _scrolling = true;
        }

        void Update()
        {
            if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame))
            {
                LeaveToGenesis();
                return;
            }

            if (!_scrolling) return;

            _currentY -= scrollSpeed * Time.deltaTime;
            _crawlText.transform.position = new Vector3(0f, _currentY, 0f);

            if (_currentY <= -_textHeight)
            {
                LeaveToGenesis();
            }
        }

        void LeaveToGenesis()
        {
            if (_leaving) return;
            _leaving = true;
            _scrolling = false;
            _fade.FadeOut(fadeOutDuration, LoadGenesis);
        }

        void LoadGenesis()
        {
            SceneManager.LoadScene(genesisSceneName);
        }
    }
}
