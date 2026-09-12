using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.EditorTools
{
    /// <summary>
    /// The research tree scene's own overlay: how big the balls are drawn, and nothing else.
    ///
    /// An overlay rather than a window or a menu, for the same reason the Link tool is a scene tool -
    /// the setting is about what is on screen, so it belongs on that screen, beside it. It shows
    /// itself only in the tree scene: registered against every SceneView because that is the only
    /// thing overlays can be registered against, and then hidden everywhere else rather than adding a
    /// line to every scene's overlay menu.
    /// </summary>
    [Overlay(typeof(SceneView), OverlayId, "Research Tree", defaultDisplay = true)]
    sealed class ResearchTreeOverlay : Overlay
    {
        const string OverlayId = "research-tree-display";

        /// <summary>Kept so Reset can put back what the sliders show. They hold their own copy of the value, so a preference changed behind their back would leave them displaying the old one.</summary>
        Slider _coreRadius;
        Slider _nodeRadius;

        public override void OnCreated()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            displayed = ResearchTreeScene.IsOpen;
        }

        public override void OnWillBeDestroyed() => EditorSceneManager.sceneOpened -= OnSceneOpened;

        void OnSceneOpened(Scene scene, OpenSceneMode mode) => displayed = ResearchTreeScene.IsOpen;

        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.minWidth = 220f;

            _coreRadius = Radius("Noyaux", ResearchTreeDisplay.CoreRadius, value => ResearchTreeDisplay.CoreRadius = value);
            _nodeRadius = Radius("Recherches", ResearchTreeDisplay.NodeRadius, value => ResearchTreeDisplay.NodeRadius = value);

            root.Add(_coreRadius);
            root.Add(_nodeRadius);
            root.Add(new Button(Reset) { text = "Réinitialiser" });

            return root;
        }

        void Reset()
        {
            ResearchTreeDisplay.ResetToDefaults();

            // Without notifying, or each slider would write straight back the value it was just given.
            _coreRadius?.SetValueWithoutNotify(ResearchTreeDisplay.CoreRadius);
            _nodeRadius?.SetValueWithoutNotify(ResearchTreeDisplay.NodeRadius);
            SceneView.RepaintAll();
        }

        static Slider Radius(string label, float value, System.Action<float> write)
        {
            var slider = new Slider(label, ResearchTreeDisplay.MinRadius, ResearchTreeDisplay.MaxRadius)
            {
                value = value,
                showInputField = true
            };

            slider.RegisterValueChangedCallback(changed =>
            {
                write(changed.newValue);
                SceneView.RepaintAll();
            });

            return slider;
        }
    }
}
