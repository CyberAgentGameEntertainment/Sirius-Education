using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Demo.Scripts
{
    /// <summary>
    ///     SRP Core の FreeCamera と同じ移動操作で、マウスでの見回しだけを Unity の Scene View と同じ右ドラッグにしたカメラ
    /// </summary>
    public class SceneViewCamera : MonoBehaviour
    {
        private const float MouseRotateDegreesPerPixel = 0.2f;
        private const float ControllerSensitivityMultiplier = 0.01f;

        [SerializeField] private float lookSpeedController = 120f;
        [SerializeField] private float moveSpeed = 10f;
        [SerializeField] private float moveSpeedIncrement = 2.5f;
        [SerializeField] private float turbo = 10f;

        private InputAction _controllerLookAction;
        private InputAction _moveAction;
        private InputAction _speedAction;
        private InputAction _yMoveAction;

        private void OnEnable()
        {
            var map = new InputActionMap("Scene View Camera");

            _controllerLookAction = map.AddAction("look");
            _controllerLookAction.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=15, y=15)");
            _moveAction = map.AddAction("move", binding: "<Gamepad>/leftStick");
            _speedAction = map.AddAction("speed", binding: "<Gamepad>/dpad");
            _yMoveAction = map.AddAction("yMove");

            _moveAction.AddCompositeBinding("Dpad")
                .With("Up", "<Keyboard>/w")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/s")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/a")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/d")
                .With("Right", "<Keyboard>/rightArrow");
            _speedAction.AddCompositeBinding("Dpad")
                .With("Up", "<Keyboard>/home")
                .With("Down", "<Keyboard>/end");
            _yMoveAction.AddCompositeBinding("Dpad")
                .With("Up", "<Keyboard>/pageUp")
                .With("Down", "<Keyboard>/pageDown")
                .With("Up", "<Keyboard>/e")
                .With("Down", "<Keyboard>/q")
                .With("Up", "<Gamepad>/rightshoulder")
                .With("Down", "<Gamepad>/leftshoulder");

            map.Enable();
        }

        private void OnDisable()
        {
            _controllerLookAction.actionMap.Dispose();
        }

        private void Update()
        {
            if (DebugManager.instance.displayRuntimeUI)
            {
                return;
            }

            Look();
            Move();
        }

        private void Look()
        {
            // Scene View と同じく右ボタンを押している間だけ、ヨーはワールド上方向・ピッチはカメラの右方向を軸に回す
            var mouse = Mouse.current;
            var degrees = mouse != null && mouse.rightButton.isPressed
                ? mouse.delta.ReadValue() * MouseRotateDegreesPerPixel
                : Vector2.zero;
            degrees += _controllerLookAction.ReadValue<Vector2>() * (lookSpeedController * ControllerSensitivityMultiplier);
            if (degrees == Vector2.zero)
            {
                return;
            }

            var yaw = Quaternion.AngleAxis(degrees.x, Vector3.up);
            var pitch = Quaternion.AngleAxis(-degrees.y, Vector3.right);
            transform.rotation = yaw * transform.rotation * pitch;
        }

        private void Move()
        {
            var changeSpeed = _speedAction.ReadValue<Vector2>().y;
            if (changeSpeed != 0f)
            {
                moveSpeed = Mathf.Max(moveSpeed + changeSpeed * moveSpeedIncrement, moveSpeedIncrement);
            }

            var move = _moveAction.ReadValue<Vector2>();
            var yMove = _yMoveAction.ReadValue<Vector2>().y;
            if (move == Vector2.zero && yMove == 0f)
            {
                return;
            }

            var speed = Time.deltaTime * moveSpeed;
            var fire = Mouse.current?.leftButton.isPressed == true || Gamepad.current?.xButton.isPressed == true;
            if (fire)
            {
                speed *= turbo;
            }
            transform.position += transform.forward * (speed * move.y)
                                  + transform.right * (speed * move.x)
                                  + Vector3.up * (speed * yMove);
        }
    }
}
