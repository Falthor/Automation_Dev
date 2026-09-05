using System.Collections.Generic;
using Game.Gameplay.Notifications;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Left-edge notification banner (TASK_05_ROBOT_CONSTRUCTEUR.md §6). Deliberately generic: it
    /// renders whatever NotificationSystem currently holds - severity, message, optional countdown -
    /// and knows nothing about robots, construction sites, or any other specific source. It never
    /// blocks interaction (the whole banner is picking-mode Ignore, no buttons).
    ///
    /// Rows are rebuilt only when the set of live notifications changes; countdown labels are
    /// refreshed in place every frame, so a ticking countdown never rebuilds the hierarchy.
    /// </summary>
    public sealed class NotificationBannerController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        VisualElement _list;
        readonly List<int> _renderedIds = new List<int>();
        readonly Dictionary<int, Label> _countdownLabels = new Dictionary<int, Label>();

        void Start()
        {
            // Start(), not OnEnable() - GameRuntime.Awake() must have run first, see
            // BottomNavController/StoragePanelController for the same reasoning.
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _list = panelRoot.Q<VisualElement>("NotificationBannerList");
        }

        void Update()
        {
            if (_list == null || gameRuntime == null || gameRuntime.Notifications == null) return;

            IReadOnlyList<Notification> active = gameRuntime.Notifications.Active;

            if (HasSameIds(active))
            {
                RefreshCountdowns(active);
                return;
            }

            Rebuild(active);
        }

        bool HasSameIds(IReadOnlyList<Notification> active)
        {
            if (active.Count != _renderedIds.Count) return false;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Id != _renderedIds[i]) return false;
            }
            return true;
        }

        void Rebuild(IReadOnlyList<Notification> active)
        {
            _list.Clear();
            _renderedIds.Clear();
            _countdownLabels.Clear();

            foreach (Notification notification in active)
            {
                var item = new VisualElement();
                item.AddToClassList("notification-item");
                item.AddToClassList(SeverityClass(notification.Severity));
                item.pickingMode = PickingMode.Ignore;

                var message = new Label(notification.Message);
                message.AddToClassList("notification-message");
                item.Add(message);

                if (notification.CountdownRemainingSeconds.HasValue)
                {
                    var countdown = new Label(FormatCountdown(notification.CountdownRemainingSeconds.Value));
                    countdown.AddToClassList("notification-countdown");
                    item.Add(countdown);
                    _countdownLabels[notification.Id] = countdown;
                }

                _list.Add(item);
                _renderedIds.Add(notification.Id);
            }
        }

        void RefreshCountdowns(IReadOnlyList<Notification> active)
        {
            foreach (Notification notification in active)
            {
                if (!notification.CountdownRemainingSeconds.HasValue) continue;
                if (_countdownLabels.TryGetValue(notification.Id, out Label label))
                {
                    label.text = FormatCountdown(notification.CountdownRemainingSeconds.Value);
                }
            }
        }

        static string FormatCountdown(float seconds) => $"{Mathf.CeilToInt(Mathf.Max(0f, seconds))} s";

        static string SeverityClass(NotificationSeverity severity) => severity switch
        {
            NotificationSeverity.Warning => "notification-item-warning",
            NotificationSeverity.Critical => "notification-item-critical",
            _ => "notification-item-info"
        };
    }
}
