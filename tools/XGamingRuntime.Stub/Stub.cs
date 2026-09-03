// Compile-time-only stand-in for the Microsoft GDK's XGamingRuntime interop assembly.
// The Xbox/Game Pass build of Star Trucker ships that DLL in BepInEx\interop; the Steam
// build does not, so StarTruckMP.Client cannot compile against a Steam install without it.
// StarTruckMP guards every call behind FindAssembly(null, "XGamingRuntime") != null, so on
// Steam none of this is ever loaded or executed — it only has to satisfy the compiler.
using System;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace XGamingRuntime
{
    public class XUserHandle { }

    public enum XUserAddOptions { None = 0, AddDefaultUserAllowingUI = 2 }
    public enum XUserGamertagComponent { Classic = 0, Modern = 1, ModernSuffix = 2, UniqueModern = 3 }
    public enum XUserGetTokenAndSignatureOptions { None = 0 }

    public class XUserGetTokenAndSignatureUtf16HttpHeader : Il2CppObjectBase
    {
        public XUserGetTokenAndSignatureUtf16HttpHeader(string name, string value) : base(IntPtr.Zero) { Name = name; Value = value; }
        public string Name { get; }
        public string Value { get; }
    }

    public class XUserGetTokenAndSignatureUtf16Data
    {
        public string Token { get; set; }
        public string Signature { get; set; }
    }

    public class XUserAddCompleted
    {
        private readonly Action<int, XUserHandle> _callback;
        private XUserAddCompleted(Action<int, XUserHandle> callback) { _callback = callback; }
        public static implicit operator XUserAddCompleted(Action<int, XUserHandle> callback) => new(callback);
    }

    public class XUserGetTokenAndSignatureUtf16Result
    {
        private readonly Action<int, XUserGetTokenAndSignatureUtf16Data> _callback;
        private XUserGetTokenAndSignatureUtf16Result(Action<int, XUserGetTokenAndSignatureUtf16Data> callback) { _callback = callback; }
        public static implicit operator XUserGetTokenAndSignatureUtf16Result(Action<int, XUserGetTokenAndSignatureUtf16Data> callback) => new(callback);
    }

    public static class HR
    {
        public static bool FAILED(int hr) => hr < 0;
        public static bool SUCCEEDED(int hr) => hr >= 0;
    }

    public static class SDK
    {
        private const int E_NOTIMPL = unchecked((int)0x80004001);

        public static int XGameRuntimeInitialize() => E_NOTIMPL;
        public static void XGameRuntimeUninitialize() { }
        public static void XTaskQueueDispatch(uint timeoutMs) { }
        public static void XUserCloseHandle(XUserHandle handle) { }
        public static void XUserAddAsync(XUserAddOptions options, XUserAddCompleted completed) { }
        public static int XUserGetId(XUserHandle handle, out ulong xuid) { xuid = 0; return E_NOTIMPL; }
        public static int XUserGetGamertag(XUserHandle handle, XUserGamertagComponent component, out string gamertag) { gamertag = null; return E_NOTIMPL; }
        public static void XUserGetTokenAndSignatureUtf16Async(
            XUserHandle handle,
            XUserGetTokenAndSignatureOptions options,
            string method,
            string url,
            Il2CppReferenceArray<XUserGetTokenAndSignatureUtf16HttpHeader> headers,
            Il2CppStructArray<byte> body,
            XUserGetTokenAndSignatureUtf16Result result) { }
    }
}
