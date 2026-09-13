using System.Collections.Generic;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws one ring per Communication Relay, the same ActionRadiusOverlay ring the Core's own
    /// radius uses. Not a MonoBehaviour, like ExplorerRobotFleetView before it: GameRuntime owns it
    /// and refreshes it from the one central Update. A relay currently not IsActive (unpowered, or
    /// starved of its CU upkeep) simply hides its ring rather than being destroyed - it comes back
    /// the instant power/CU does, with no view rebuilt.
    /// </summary>
    public sealed class CommunicationRelayRadiusFleetView
    {
        readonly GridRuntime _grid;
        readonly Shader _overlayShader;

        readonly List<ActionRadiusView> _views = new List<ActionRadiusView>();

        public CommunicationRelayRadiusFleetView(GridRuntime grid, Shader overlayShader)
        {
            _grid = grid;
            _overlayShader = overlayShader;
        }

        /// <summary>How many rings stand in the world - one per relay ever placed, created once and reused, never one per frame.</summary>
        public int MarkerCount => _views.Count;

        public void Refresh(IReadOnlyList<CommunicationRelayRuntime> relays)
        {
            if (relays == null || _grid == null || _overlayShader == null) return;

            for (int i = 0; i < relays.Count; i++)
            {
                if (i >= _views.Count) _views.Add(CreateView());

                ActionRadiusView view = _views[i];
                CommunicationRelayRuntime relay = relays[i];

                view.gameObject.SetActive(relay.IsActive);
                if (!relay.IsActive) continue;

                Vector3 center = _grid.FootprintCenterToWorld(relay.Cell, relay.Definition.FootprintSize);
                view.Initialize(center, relay.ActionRadiusCells * _grid.CellSize);
            }
        }

        ActionRadiusView CreateView()
        {
            var view = new GameObject("CommunicationRelayRadius").AddComponent<ActionRadiusView>();
            view.OverlayShader = _overlayShader;
            return view;
        }
    }
}
