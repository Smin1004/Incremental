using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// One node of the skill tree view. Pointer events go back to <see cref="TreeView"/>: a click buys one level,
    /// holding repeats, a drag that starts here pans the tree (drag events bubble to <see cref="TreeDragArea"/>).
    /// </summary>
    public sealed class TreeNodeWidget : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Null for the center node ("성운").</summary>
        public NodeDef node;
        public TreeView view;
        public RectTransform rt;
        public Image core;
        public Image glow;
        public Text label;
        public float size;

        public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) view.OnNodeDown(this); }
        public void OnPointerUp(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) view.OnNodeUp(this); }
        public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) view.OnNodeClick(this); }
        public void OnPointerEnter(PointerEventData e) => view.OnNodeEnter(this);
        public void OnPointerExit(PointerEventData e) => view.OnNodeExit(this);
    }

    /// <summary>Full-screen background of the tree view: drag pans, the wheel zooms around the pointer.</summary>
    public sealed class TreeDragArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public TreeView view;

        public void OnBeginDrag(PointerEventData e) => view.OnPanBegin();
        public void OnDrag(PointerEventData e) => view.OnPan(e.delta);
        public void OnEndDrag(PointerEventData e) => view.OnPanEnd();
        public void OnScroll(PointerEventData e) => view.OnZoom(e.scrollDelta.y, e.position);
    }
}
