using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Lingmai.RedMist
{
    public sealed class HoldToTalkButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private const int NoPointer = int.MinValue;

        private bool _isHolding;
        private int _activePointerId = NoPointer;

        public event Action Pressed;
        public event Action Released;
        public event Action Cancelled;

        public bool IsHolding => _isHolding;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_isHolding || eventData == null || eventData.button != PointerEventData.InputButton.Left) return;

            _isHolding = true;
            _activePointerId = eventData.pointerId;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!IsActivePointer(eventData) || eventData.button != PointerEventData.InputButton.Left) return;

            ResetHoldingState();
            Released?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!IsActivePointer(eventData)) return;
            Cancel();
        }

        public void Cancel()
        {
            if (!_isHolding) return;

            ResetHoldingState();
            Cancelled?.Invoke();
        }

        private void OnDisable()
        {
            Cancel();
        }

        private bool IsActivePointer(PointerEventData eventData)
        {
            return _isHolding && eventData != null && eventData.pointerId == _activePointerId;
        }

        private void ResetHoldingState()
        {
            _isHolding = false;
            _activePointerId = NoPointer;
        }
    }
}
