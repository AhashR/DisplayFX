using System;
using System.Runtime.InteropServices;

namespace WindowsDisplayAPI.Native.DeviceContext
{
    internal class DCHandle : SafeHandle
    {
        private readonly bool _created;
        private readonly IntPtr _windowHandle;

        private DCHandle(IntPtr handle, bool created, IntPtr windowHandle = default) : base(IntPtr.Zero, true)
        {
            SetHandle(handle);
            _created = created;
            _windowHandle = windowHandle;
        }

        public override bool IsInvalid
        {
            get => handle == IntPtr.Zero;
        }

        public static DCHandle CreateFromDevice(string screenName, string devicePath)
        {
            return new DCHandle(
                DeviceContextApi.CreateDC(screenName, screenName, null, IntPtr.Zero),
                true
            );
        }

        public static DCHandle CreateFromScreen(string screenName)
        {
            return CreateFromDevice(screenName, screenName);
        }

        public static DCHandle CreateFromWindow(IntPtr windowHandle)
        {
            return new DCHandle(
                DeviceContextApi.GetDC(windowHandle),
                false,
                windowHandle
            );
        }

        public static DCHandle CreateGlobal()
        {
            return new DCHandle(
                DeviceContextApi.CreateDC("DISPLAY", null, null, IntPtr.Zero),
                true
            );
        }

        protected override bool ReleaseHandle()
        {
            return _created
                ? DeviceContextApi.DeleteDC(this.handle)
                : DeviceContextApi.ReleaseDC(_windowHandle, this.handle);
        }
    }
}
