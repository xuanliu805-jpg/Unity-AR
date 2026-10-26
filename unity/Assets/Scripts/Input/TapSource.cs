using UnityEngine;

namespace UnityAR.Input
{
    /// <summary>一次点击事件的信息。</summary>
    public struct TapInfo
    {
        /// <summary>是否为「按下瞬间」的点击（而不是拖动过程）。</summary>
        public bool Began;

        /// <summary>屏幕坐标（像素）。</summary>
        public Vector2 ScreenPosition;

        /// <summary>触点编号；鼠标点击时为 -1。</summary>
        public int FingerId;
    }

    /// <summary>
    /// 点击来源抽象。
    ///
    /// Unity 的「Active Input Handling」有三种取值（Old / New / Both），
    /// 直接写 <c>Input.GetTouch</c> 或 <c>Touchscreen.current</c> 都可能在新老工程里编译不过。
    /// 这里用接口把两种输入系统都包起来，由 <see cref="TapSourceFactory"/> 在编译期挑选可用的实现，
    /// 这样同一套代码在任意 Input Handling 设置下都能编译。
    /// </summary>
    public interface ITapSource
    {
        /// <summary>尝试取一次点击；没有点击时返回 false。</summary>
        bool TryGetTap(out TapInfo tap);
    }

    /// <summary>新输入系统（com.unity.inputsystem）实现。</summary>
#if ENABLE_INPUT_SYSTEM
    public class NewInputTapSource : ITapSource
    {
        public bool TryGetTap(out TapInfo tap)
        {
            tap = default(TapInfo);

            var touchscreen = UnityEngine.InputSystem.Touchscreen.current;
            if (touchscreen != null)
            {
                var touch = touchscreen.primaryTouch;
                if (touch.press.wasPressedThisFrame)
                {
                    tap = new TapInfo
                    {
                        Began = true,
                        ScreenPosition = touch.position.ReadValue(),
                        FingerId = touch.touchId.ReadValue()
                    };
                    return true;
                }

                return false;
            }

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                tap = new TapInfo
                {
                    Began = true,
                    ScreenPosition = mouse.position.ReadValue(),
                    FingerId = -1
                };
                return true;
            }

            return false;
        }
    }
#endif

    /// <summary>旧输入系统（Input Manager）实现。</summary>
#if ENABLE_LEGACY_INPUT_MANAGER
    public class LegacyInputTapSource : ITapSource
    {
        public bool TryGetTap(out TapInfo tap)
        {
            tap = default(TapInfo);

            if (Input.touchCount > 0)
            {
                var touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    tap = new TapInfo
                    {
                        Began = true,
                        ScreenPosition = touch.position,
                        FingerId = touch.fingerId
                    };
                    return true;
                }

                return false;
            }

            if (Input.GetMouseButtonDown(0))
            {
                tap = new TapInfo
                {
                    Began = true,
                    ScreenPosition = Input.mousePosition,
                    FingerId = -1
                };
                return true;
            }

            return false;
        }
    }
#endif

    /// <summary>两种输入系统都没启用时的占位实现。</summary>
    public class NullTapSource : ITapSource
    {
        public bool TryGetTap(out TapInfo tap)
        {
            tap = default(TapInfo);
            return false;
        }
    }

    /// <summary>按编译期的输入系统开关挑选实现。</summary>
    public static class TapSourceFactory
    {
        public static ITapSource Create()
        {
#if ENABLE_INPUT_SYSTEM
            return new NewInputTapSource();
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new LegacyInputTapSource();
#else
            return new NullTapSource();
#endif
        }
    }
}
