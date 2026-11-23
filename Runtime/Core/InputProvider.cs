using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DrawingSystem
{
    /// <summary>
    /// Abstract base class for all input providers. Subclasses implement
    /// <see cref="PollInput"/> to read device-specific input each frame.
    /// </summary>
    public abstract class InputProviderBase : MonoBehaviour
    {
        /// <summary>
        /// Fired each frame when new drawing input is available.
        /// </summary>
        public event Action<DrawingInputData> OnInput;

        /// <summary>
        /// Called every frame in <see cref="Update"/>. Implementations should read
        /// their respective input device and call <see cref="RaiseInput"/> with the result.
        /// </summary>
        protected abstract void PollInput();

        /// <summary>
        /// Unity Update callback. Delegates to <see cref="PollInput"/>.
        /// </summary>
        protected virtual void Update()
        {
            PollInput();
        }

        /// <summary>
        /// Fires the <see cref="OnInput"/> event with the supplied data.
        /// </summary>
        /// <param name="data">The input data to broadcast.</param>
        protected void RaiseInput(DrawingInputData data)
        {
            OnInput?.Invoke(data);
        }
    }

    /// <summary>
    /// Input provider that reads mouse input, with automatic fallback to Pen if active.
    /// </summary>
    [AddComponentMenu("Drawing System/Mouse Input Provider")]
    public class MouseInputProvider : InputProviderBase
    {
        private bool _isDrawing;
        private bool _wasPressed;
        private float _lastPenTime = -10f;
        private float _lastValidPressure = 0.5f;

        /// <inheritdoc />
        protected override void PollInput()
        {
#if ENABLE_INPUT_SYSTEM
            // ペンタブレット（XP-Pen等）が使われている場合はペン入力を優先
            Pen pen = Pen.current;
            if (pen != null)
            {
                bool isContact = pen.tip.isPressed || pen.eraser.isPressed;
                float rawPressure = pen.pressure.ReadValue();
                bool hasPressure = rawPressure > 0.005f;
                bool isPenActive = isContact || (hasPressure && _isDrawing);

                if (isPenActive)
                {
                    _lastPenTime = Time.unscaledTime;

                    InputPhase penPhase;
                    if (!_isDrawing)
                    {
                        penPhase = InputPhase.Began;
                        _isDrawing = true;
                    }
                    else
                    {
                        penPhase = InputPhase.Moved;
                    }

                    float pressure = rawPressure > 0.001f ? Mathf.Clamp01(rawPressure) : Mathf.Max(0.02f, _lastValidPressure * 0.3f);
                    if (rawPressure > 0.001f) _lastValidPressure = pressure;

                    var penData = new DrawingInputData
                    {
                        Position = pen.position.ReadValue(),
                        Pressure = pressure,
                        Tilt = pen.tilt.ReadValue(),
                        IsEraser = pen.eraser.isPressed,
                        Phase = penPhase,
                        DeviceName = $"Pen ({pen.displayName})"
                    };

                    RaiseInput(penData);
                    return;
                }
                else if (_isDrawing)
                {
                    _isDrawing = false;
                    _lastPenTime = Time.unscaledTime;

                    var penData = new DrawingInputData
                    {
                        Position = pen.position.ReadValue(),
                        Pressure = Mathf.Max(0.02f, _lastValidPressure * 0.2f),
                        Tilt = pen.tilt.ReadValue(),
                        IsEraser = pen.eraser.isPressed,
                        Phase = InputPhase.Ended,
                        DeviceName = $"Pen ({pen.displayName})"
                    };

                    RaiseInput(penData);
                    return;
                }
            }

            // ペン操作中・直後はWindowsが合成するマウスクリックを抑制
            if (Time.unscaledTime - _lastPenTime < 0.35f)
            {
                _isDrawing = false;
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                bool isPressed = mouse.leftButton.isPressed;

                if (!_isDrawing && isPressed)
                {
                    _isDrawing = true;
                    RaiseInput(new DrawingInputData
                    {
                        Position = mouse.position.ReadValue(),
                        Pressure = 1.0f,
                        Tilt = Vector2.zero,
                        IsEraser = false,
                        Phase = InputPhase.Began,
                        DeviceName = "Mouse"
                    });
                    return;
                }
                else if (_isDrawing && isPressed)
                {
                    RaiseInput(new DrawingInputData
                    {
                        Position = mouse.position.ReadValue(),
                        Pressure = 1.0f,
                        Tilt = Vector2.zero,
                        IsEraser = false,
                        Phase = InputPhase.Moved,
                        DeviceName = "Mouse"
                    });
                    return;
                }
                else if (_isDrawing && !isPressed)
                {
                    _isDrawing = false;
                    RaiseInput(new DrawingInputData
                    {
                        Position = mouse.position.ReadValue(),
                        Pressure = 1.0f,
                        Tilt = Vector2.zero,
                        IsEraser = false,
                        Phase = InputPhase.Ended,
                        DeviceName = "Mouse"
                    });
                    return;
                }

                return;
            }
#endif
            bool legacyPressed = Input.GetMouseButton(0);
            bool legacyJustPressed = Input.GetMouseButtonDown(0);
            bool legacyJustReleased = Input.GetMouseButtonUp(0);

            if (!legacyPressed && !legacyJustReleased)
            {
                _wasPressed = false;
                return;
            }

            InputPhase legPhase;
            if (legacyJustPressed)
            {
                legPhase = InputPhase.Began;
            }
            else if (legacyJustReleased)
            {
                legPhase = InputPhase.Ended;
            }
            else
            {
                legPhase = InputPhase.Moved;
            }

            var legData = new DrawingInputData
            {
                Position = Input.mousePosition,
                Pressure = 1.0f,
                Tilt = Vector2.zero,
                IsEraser = false,
                Phase = legPhase
            };

            _wasPressed = legacyPressed;
            RaiseInput(legData);
        }
    }

    /// <summary>
    /// Input provider that reads touch input via the legacy
    /// <see cref="UnityEngine.Input"/> API. Only the first touch is tracked
    /// for drawing; additional touches are ignored.
    /// </summary>
    [AddComponentMenu("Drawing System/Touch Input Provider")]
    public class TouchInputProvider : InputProviderBase
    {
        /// <inheritdoc />
        protected override void PollInput()
        {
            if (Input.touchCount == 0)
            {
                return;
            }

            Touch touch = Input.GetTouch(0);

            InputPhase phase;
            switch (touch.phase)
            {
                case UnityEngine.TouchPhase.Began:
                    phase = InputPhase.Began;
                    break;
                case UnityEngine.TouchPhase.Moved:
                    phase = InputPhase.Moved;
                    break;
                case UnityEngine.TouchPhase.Stationary:
                    phase = InputPhase.Stationary;
                    break;
                case UnityEngine.TouchPhase.Ended:
                    phase = InputPhase.Ended;
                    break;
                case UnityEngine.TouchPhase.Canceled:
                    phase = InputPhase.Cancelled;
                    break;
                default:
                    phase = InputPhase.Cancelled;
                    break;
            }

            float pressure = touch.pressure > 0f ? touch.pressure : 1.0f;

            var data = new DrawingInputData
            {
                Position = touch.position,
                Pressure = pressure,
                Tilt = Vector2.zero,
                IsEraser = false,
                Phase = phase
            };

            RaiseInput(data);
        }
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// Input provider that reads pen/stylus input via the Unity New Input System.
    /// Requires the <c>com.unity.inputsystem</c> package and the
    /// <c>ENABLE_INPUT_SYSTEM</c> scripting define to be active.
    /// </summary>
    /// <remarks>
    /// If no <see cref="Pen"/> device is connected, this provider logs a warning
    /// once and silently skips input polling until a device becomes available.
    /// </remarks>
    [AddComponentMenu("Drawing System/Pen Tablet Input Provider")]
    public class PenTabletInputProvider : InputProviderBase
    {
        private bool _isPenDrawing;
        private bool _isMouseDrawing;
        private float _lastPenTime = -10f;
        private float _lastValidPressure = 0.5f;

        /// <inheritdoc />
        protected override void PollInput()
        {
            Pen pen = Pen.current;

            if (pen != null)
            {
                bool isContact = pen.tip.isPressed || pen.eraser.isPressed;
                float rawPressure = pen.pressure.ReadValue();
                bool hasPressure = rawPressure > 0.005f;

                // ペンが接触している、または筆圧が感知されている場合は「ペンアクティブ」
                bool isPenActive = isContact || (hasPressure && _isPenDrawing);

                if (isPenActive)
                {
                    _lastPenTime = Time.unscaledTime;

                    InputPhase phase;
                    if (!_isPenDrawing)
                    {
                        phase = InputPhase.Began;
                        _isPenDrawing = true;
                    }
                    else
                    {
                        phase = InputPhase.Moved;
                    }

                    float pressure = rawPressure > 0.001f ? Mathf.Clamp01(rawPressure) : Mathf.Max(0.02f, _lastValidPressure * 0.3f);
                    if (rawPressure > 0.001f) _lastValidPressure = pressure;

                    Vector2 tilt = pen.tilt.ReadValue();
                    bool isEraser = pen.eraser.isPressed;

                    var data = new DrawingInputData
                    {
                        Position = pen.position.ReadValue(),
                        Pressure = pressure,
                        Tilt = tilt,
                        IsEraser = isEraser,
                        Phase = phase,
                        DeviceName = $"Pen ({pen.displayName})"
                    };

                    RaiseInput(data);
                    return;
                }
                else if (_isPenDrawing)
                {
                    // ペンが離れた瞬間
                    _isPenDrawing = false;
                    _lastPenTime = Time.unscaledTime;

                    Vector2 tilt = pen.tilt.ReadValue();
                    bool isEraser = pen.eraser.isPressed;
                    float pressure = Mathf.Max(0.02f, _lastValidPressure * 0.2f);

                    var data = new DrawingInputData
                    {
                        Position = pen.position.ReadValue(),
                        Pressure = pressure,
                        Tilt = tilt,
                        IsEraser = isEraser,
                        Phase = InputPhase.Ended,
                        DeviceName = $"Pen ({pen.displayName})"
                    };

                    RaiseInput(data);
                    return;
                }
            }

            _isPenDrawing = false;

            // ペン操作中・直後はWindowsが合成するマウスクリックを抑制
            if (Time.unscaledTime - _lastPenTime < 0.35f)
            {
                _isMouseDrawing = false;
                return;
            }

            // ペン入力がないときはマウス入力をフォールバックとして処理
            PollMouseFallback();
        }

        private void PollMouseFallback()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            bool isPressed = mouse.leftButton.isPressed;

            if (!_isMouseDrawing && isPressed)
            {
                _isMouseDrawing = true;
                RaiseInput(new DrawingInputData
                {
                    Position = mouse.position.ReadValue(),
                    Pressure = 1.0f,
                    Tilt = Vector2.zero,
                    IsEraser = false,
                    Phase = InputPhase.Began,
                    DeviceName = "Mouse"
                });
            }
            else if (_isMouseDrawing && isPressed)
            {
                RaiseInput(new DrawingInputData
                {
                    Position = mouse.position.ReadValue(),
                    Pressure = 1.0f,
                    Tilt = Vector2.zero,
                    IsEraser = false,
                    Phase = InputPhase.Moved,
                    DeviceName = "Mouse"
                });
            }
            else if (_isMouseDrawing && !isPressed)
            {
                _isMouseDrawing = false;
                RaiseInput(new DrawingInputData
                {
                    Position = mouse.position.ReadValue(),
                    Pressure = 1.0f,
                    Tilt = Vector2.zero,
                    IsEraser = false,
                    Phase = InputPhase.Ended,
                    DeviceName = "Mouse"
                });
            }
        }
    }
#endif
}
