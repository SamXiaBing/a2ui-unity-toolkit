using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    /// <summary>
    /// 鼠标 / 触摸拖拽 VisualElement（Pointer 统一管线，含 Capture）。
    /// 点击 Button / Toggle / TextField 等交互控件时不抢拖拽。
    /// </summary>
    public sealed class A2uiDragManipulator : Manipulator
    {
        readonly float _dragThresholdPx;
        bool _pressed;
        bool _dragging;
        int _pointerId = -1;
        Vector2 _pressPanelPos;
        Vector2 _originLeftTop;

        public A2uiDragManipulator(float dragThresholdPx = 4f)
        {
            _dragThresholdPx = Mathf.Max(0f, dragThresholdPx);
        }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.NoTrickleDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.NoTrickleDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (_pressed) return;
            if (evt.button != 0 && evt.pointerType == UnityEngine.UIElements.PointerType.mouse)
                return;
            if (IsInteractive(evt.target as VisualElement))
                return;

            _pressed = true;
            _dragging = false;
            _pointerId = evt.pointerId;
            _pressPanelPos = evt.position;
            EnsureAbsoluteLayout();
            _originLeftTop = CurrentLeftTop();
            target.CapturePointer(_pointerId);
            evt.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_pressed || evt.pointerId != _pointerId) return;

            var delta = (Vector2)evt.position - _pressPanelPos;
            if (!_dragging)
            {
                if (delta.sqrMagnitude < _dragThresholdPx * _dragThresholdPx)
                    return;
                _dragging = true;
                target.AddToClassList("a2ui-dragging");
            }

            var next = _originLeftTop + delta;
            ApplyClamped(next);
            evt.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!_pressed || evt.pointerId != _pointerId) return;
            EndDrag(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPointerCancel(PointerCancelEvent evt)
        {
            if (!_pressed || evt.pointerId != _pointerId) return;
            EndDrag(evt.pointerId);
        }

        void OnCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_pressed)
                EndDrag(_pointerId, releaseCapture: false);
        }

        void EndDrag(int pointerId, bool releaseCapture = true)
        {
            if (releaseCapture && target.HasPointerCapture(pointerId))
                target.ReleasePointer(pointerId);
            _pressed = false;
            _dragging = false;
            _pointerId = -1;
            target.RemoveFromClassList("a2ui-dragging");
        }

        void EnsureAbsoluteLayout()
        {
            if (target.style.position.value == UnityEngine.UIElements.Position.Absolute)
                return;

            var parent = target.parent;
            if (parent == null) return;

            var world = target.worldBound;
            var parentWorld = parent.worldBound;
            target.style.position = UnityEngine.UIElements.Position.Absolute;
            target.style.left = world.x - parentWorld.x;
            target.style.top = world.y - parentWorld.y;
            target.style.right = StyleKeyword.Auto;
            target.style.bottom = StyleKeyword.Auto;
            target.style.marginLeft = 0;
            target.style.marginTop = 0;
            target.style.marginRight = 0;
            target.style.marginBottom = 0;
        }

        Vector2 CurrentLeftTop()
        {
            return new Vector2(
                target.resolvedStyle.left,
                target.resolvedStyle.top);
        }

        void ApplyClamped(Vector2 leftTop)
        {
            var parent = target.parent;
            if (parent == null)
            {
                target.style.left = leftTop.x;
                target.style.top = leftTop.y;
                return;
            }

            var pw = parent.contentRect.width;
            var ph = parent.contentRect.height;

            // 卡片变 absolute 后父容器（条带）塌缩 height→0。
            // 此时跳过 clamp，允许自由拖拽——面板 root 会裁切超出屏幕的部分。
            // 父容器有实际尺寸时（首次渲染、未触发 absolute），正常 clamp。
            if (pw < 1f || ph < 1f)
            {
                target.style.left = leftTop.x;
                target.style.top = leftTop.y;
                return;
            }

            var w = target.layout.width > 1f ? target.layout.width : target.resolvedStyle.width;
            var h = target.layout.height > 1f ? target.layout.height : target.resolvedStyle.height;
            if (float.IsNaN(w) || w < 1f) w = 200f;
            if (float.IsNaN(h) || h < 1f) h = 80f;

            var minX = 0f;
            var minY = 0f;
            var maxX = Mathf.Max(0f, pw - w);
            var maxY = Mathf.Max(0f, ph - h);
            // 逐轴钳制：某轴可动范围退化（卡片≥父容器，如定高条带里的高卡片）就
            // 还原该轴自由拖拽——dc6542c 的垂直自由在被定高条带复活钳制后再次生效
            target.style.left = maxX > 0.5f ? Mathf.Clamp(leftTop.x, minX, maxX) : leftTop.x;
            target.style.top = maxY > 0.5f ? Mathf.Clamp(leftTop.y, minY, maxY) : leftTop.y;
        }

        static bool IsInteractive(VisualElement ve)
        {
            while (ve != null)
            {
                // 注意：ScrollView 不算交互件——卡片内容包在滚动层里，若拦截则
                // 内容区完全无法启动拖拽（只能捏着 padding 边缘拖）。鼠标滚轮
                // 滚动走 PointerWheelEvent，与拖拽手势不冲突。
                if (ve is Button || ve is Toggle || ve is TextField ||
                    ve is Slider || ve is Scroller ||
                    ve is DropdownField || ve is Foldout)
                    return true;
                if (ve.ClassListContains("unity-base-slider") ||
                    ve.ClassListContains("unity-toggle") ||
                    ve.ClassListContains("a2ui-btn"))
                    return true;
                ve = ve.parent;
            }

            return false;
        }
    }
}
