// Build with the .NET Framework 4 compiler: /target:winexe /platform:x86.
// Exit 0: a complete video graph and usable IVideoWindow were constructed.
// Exit 2: unsupported/partial media graph. Exit 3: invalid input/probe error.
// Graph construction is not proof of complete playback. Never Run or Pause it.
using System;
using System.IO;
using System.Runtime.InteropServices;

internal static class SealVideoProbe
{
    private static readonly Guid VideoMajorType = new Guid("73646976-0000-0010-8000-00AA00389B71");
    private const uint ErrorMode = 0x0001 | 0x0002 | 0x8000;

    [DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);

    [STAThread]
    private static int Main(string[] arguments)
    {
        SetErrorMode(ErrorMode);
        if (IntPtr.Size != 4 || arguments.Length != 1) return 3;
        object graphObject = null;
        try
        {
            string path = Path.GetFullPath(arguments[0]);
            if (!File.Exists(path)) return 3;
            graphObject = Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770")));
            IGraphBuilder graph = (IGraphBuilder)graphObject;
            // S_FALSE and VFW_S_PARTIAL_RENDER are not complete video support.
            if (graph.RenderFile(path, null) != 0) return 2;
            IVideoWindow videoWindow = graphObject as IVideoWindow;
            if (videoWindow == null) return 2;
            int visible;
            if (videoWindow.get_Visible(out visible) != 0) return 2;
            return HasConnectedVideo(graph) ? 0 : 2;
        }
        catch (Exception) { return 3; }
        finally { Release(graphObject); }
    }

    private static void Release(object value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }

    private static bool HasConnectedVideo(IGraphBuilder graph)
    {
        IEnumFilters enumerator = null;
        try
        {
            if (graph.EnumFilters(out enumerator) != 0 || enumerator == null) return false;
            IBaseFilter[] filters = new IBaseFilter[1];
            for (int count = 0; count < 256 && enumerator.Next(1, filters, IntPtr.Zero) == 0; count++)
            {
                IBaseFilter filter = filters[0];
                filters[0] = null;
                try { if (FilterHasConnectedVideo(filter)) return true; }
                finally { Release(filter); }
            }
            return false;
        }
        finally { Release(enumerator); }
    }

    private static bool FilterHasConnectedVideo(IBaseFilter filter)
    {
        IEnumPins enumerator = null;
        try
        {
            if (filter == null || filter.EnumPins(out enumerator) != 0 || enumerator == null) return false;
            IPin[] pins = new IPin[1];
            for (int count = 0; count < 256 && enumerator.Next(1, pins, IntPtr.Zero) == 0; count++)
            {
                IPin pin = pins[0], peer = null;
                pins[0] = null;
                AMMediaType media = new AMMediaType();
                try
                {
                    if (pin.ConnectedTo(out peer) == 0 && peer != null &&
                        pin.ConnectionMediaType(media) == 0 && media.MajorType == VideoMajorType) return true;
                }
                finally
                {
                    if (media.Format != IntPtr.Zero) Marshal.FreeCoTaskMem(media.Format);
                    if (media.Unknown != IntPtr.Zero) Marshal.Release(media.Unknown);
                    Release(peer);
                    Release(pin);
                }
            }
            return false;
        }
        finally { Release(enumerator); }
    }

    [ComImport, Guid("56A868A9-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphBuilder
    {
        [PreserveSig] int AddFilter(IntPtr filter, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int RemoveFilter(IntPtr filter);
        [PreserveSig] int EnumFilters(out IEnumFilters filters);
        [PreserveSig] int FindFilterByName([MarshalAs(UnmanagedType.LPWStr)] string name, out IntPtr filter);
        [PreserveSig] int ConnectDirect(IntPtr first, IntPtr second, IntPtr mediaType);
        [PreserveSig] int Reconnect(IntPtr pin);
        [PreserveSig] int Disconnect(IntPtr pin);
        [PreserveSig] int SetDefaultSyncSource();
        [PreserveSig] int Connect(IntPtr first, IntPtr second);
        [PreserveSig] int Render(IntPtr pin);
        [PreserveSig] int RenderFile([MarshalAs(UnmanagedType.LPWStr)] string path, [MarshalAs(UnmanagedType.LPWStr)] string playlist);
    }

    [ComImport, Guid("56A86893-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumFilters
    {
        [PreserveSig] int Next(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IBaseFilter[] filters, IntPtr fetched);
    }

    [ComImport, Guid("56A86895-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBaseFilter
    {
        [PreserveSig] int GetClassID(out Guid clsid);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long time);
        [PreserveSig] int GetState(int timeout, out int state);
        [PreserveSig] int SetSyncSource(IntPtr clock);
        [PreserveSig] int GetSyncSource(out IntPtr clock);
        [PreserveSig] int EnumPins(out IEnumPins pins);
    }

    [ComImport, Guid("56A86892-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumPins
    {
        [PreserveSig] int Next(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IPin[] pins, IntPtr fetched);
    }

    [ComImport, Guid("56A86891-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPin
    {
        [PreserveSig] int Connect(IntPtr pin, IntPtr mediaType);
        [PreserveSig] int ReceiveConnection(IntPtr pin, IntPtr mediaType);
        [PreserveSig] int Disconnect();
        [PreserveSig] int ConnectedTo(out IPin pin);
        [PreserveSig] int ConnectionMediaType([Out] AMMediaType mediaType);
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class AMMediaType
    {
        public Guid MajorType;
        public Guid SubType;
        [MarshalAs(UnmanagedType.Bool)] public bool FixedSizeSamples;
        [MarshalAs(UnmanagedType.Bool)] public bool TemporalCompression;
        public int SampleSize;
        public Guid FormatType;
        public IntPtr Unknown;
        public int FormatSize;
        public IntPtr Format;
    }

    [ComImport, Guid("56A868B4-0AD4-11CE-B03A-0020AF0BA770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IVideoWindow
    {
        [PreserveSig] int put_Caption([MarshalAs(UnmanagedType.BStr)] string value);
        [PreserveSig] int get_Caption([MarshalAs(UnmanagedType.BStr)] out string value);
        [PreserveSig] int put_WindowStyle(int value);
        [PreserveSig] int get_WindowStyle(out int value);
        [PreserveSig] int put_WindowStyleEx(int value);
        [PreserveSig] int get_WindowStyleEx(out int value);
        [PreserveSig] int put_AutoShow(int value);
        [PreserveSig] int get_AutoShow(out int value);
        [PreserveSig] int put_WindowState(int value);
        [PreserveSig] int get_WindowState(out int value);
        [PreserveSig] int put_BackgroundPalette(int value);
        [PreserveSig] int get_BackgroundPalette(out int value);
        [PreserveSig] int put_Visible(int value);
        [PreserveSig] int get_Visible(out int value);
    }
}
