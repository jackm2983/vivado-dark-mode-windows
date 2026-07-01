using System.Runtime.InteropServices;
using Karna.Magnification;
using System.Windows.Forms;
using System.Text;
using System.Diagnostics;

namespace WindowOverlayApp
{
    public partial class Form1 : Form
    {
        const int SWP_NOACTIVATE = 0x0010;
        const int SWP_SHOWWINDOW = 0x0040;
        const int SWP_NOMOVE = 0x0002;
        const int SWP_NOSIZE = 0x0001;

        // window event constants
        const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003; // triggers when a window comes to the foreground
        const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetClientRect(IntPtr hWnd, ref RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
            uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hWnd);

        const int GW_HWNDFIRST = 0;
        const int GW_HWNDLAST = 1;
        const int GW_HWNDNEXT = 2;
        const uint GW_HWNDPREV = 3; // window directly above hWnd in z order
        const int GW_OWNER = 4;
        const int GW_CHILD = 5;
        const int GW_ENABLEDPOPUP = 6;

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private IntPtr notepadHandle = IntPtr.Zero;
        private IntPtr winEventHook = IntPtr.Zero;
        private IntPtr foregroundEventHook = IntPtr.Zero;
        private WinEventDelegate winEventDelegate, foregroundEventDelegate;

        private System.Windows.Forms.Timer findWindowTimer;
        private System.Windows.Forms.Timer updateDebounceTimer;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        Magnifier magnifier;
        private System.Windows.Forms.Timer resizeTimer;
        private bool isResizing = false;

        public Form1()
        {
            InitializeComponent();
            winEventDelegate = new WinEventDelegate(WinEventProc);
            foregroundEventDelegate = new WinEventDelegate(ForegroundEventProc);
            magnifier = new Magnifier(this);

            updateDebounceTimer = new System.Windows.Forms.Timer();
            updateDebounceTimer.Interval = 150;
            updateDebounceTimer.Tick += (s, args) =>
            {
                updateDebounceTimer.Stop();
                ApplyOverlayUpdate();
            };
        }

        const int WS_EX_TOOLWINDOW = 0x00000080; // hides from alt+tab
        const int WS_EX_APPWINDOW = 0x00040000; // forces a window to appear in alt+tab
        const int WS_EX_NOACTIVATE = 0x08000000; // prevents the window from receiving focus

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x80000;
                const int WS_EX_TRANSPARENT = 0x20;

                CreateParams cp = base.CreateParams;

                cp.ExStyle |= WS_EX_TOOLWINDOW;
                cp.ExStyle |= WS_EX_LAYERED;
                cp.ExStyle |= WS_EX_TRANSPARENT;

                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            findWindowTimer = new System.Windows.Forms.Timer();
            findWindowTimer.Interval = 250;
            findWindowTimer.Tick += (s, args) =>
            {
                EnsureTargetWindow();
                ApplyOverlayUpdate();
            };
            findWindowTimer.Start();

            EnsureTargetWindow();
        }

        // finds the first visible top level window whose title contains the given text
        static IntPtr FindWindowContaining(string titleFragment)
        {
            IntPtr result = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd))
                    return true;

                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                StringBuilder sb = new StringBuilder(length + 1);
                GetWindowText(hWnd, sb, sb.Capacity);

                if (sb.ToString().IndexOf(titleFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result = hWnd;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            return result;
        }

        // checks whether the current target window is still valid, and if not,
        // looks for any window with "vivado" in the title and attaches to it
        private void EnsureTargetWindow()
        {
            if (notepadHandle != IntPtr.Zero && IsWindow(notepadHandle))
                return;

            IntPtr found = FindWindowContaining("Vivado 20");

            if (found == IntPtr.Zero)
            {
                notepadHandle = IntPtr.Zero;
                return;
            }

            if (found == notepadHandle)
                return;

            notepadHandle = found;

            if (winEventHook != IntPtr.Zero)
                UnhookWinEvent(winEventHook);
            if (foregroundEventHook != IntPtr.Zero)
                UnhookWinEvent(foregroundEventHook);

            winEventHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, winEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            foregroundEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, foregroundEventDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);

            ApplyOverlayUpdate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            if (winEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(winEventHook);
            }
            if (foregroundEventHook != IntPtr.Zero)
            {
                UnhookWinEvent(foregroundEventHook);
            }
            if (findWindowTimer != null)
            {
                findWindowTimer.Stop();
                findWindowTimer.Dispose();
            }
            if (updateDebounceTimer != null)
            {
                updateDebounceTimer.Stop();
                updateDebounceTimer.Dispose();
            }
        }

        private RECT lastRect;

        // called by event hooks, restarts the debounce timer instead of updating
        // immediately, so bursts of events collapse into a single settled update
        private void UpdateOverlayWindow()
        {
            if (notepadHandle == IntPtr.Zero)
                return;

            updateDebounceTimer.Stop();
            updateDebounceTimer.Start();
        }

        // does the actual repositioning and magnifier refresh
        private void ApplyOverlayUpdate()
        {
            if (notepadHandle == IntPtr.Zero)
                return;

            RECT rect = new RECT();
            if (GetWindowRect(notepadHandle, ref rect))
            {
                const int windowBorder = 2;
                const int aeroBorder = 7 + windowBorder;
                const int aeroBorderTop = -1 + windowBorder;

                rect.Left += aeroBorder;
                rect.Top += aeroBorderTop;
                rect.Right -= aeroBorder;
                rect.Bottom -= aeroBorder;

                this.Size = new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
                this.Location = new Point(rect.Left, rect.Top);

                IntPtr insertAfter = GetWindow(notepadHandle, GW_HWNDPREV);
                if (insertAfter == IntPtr.Zero)
                    insertAfter = notepadHandle;

                // skip repositioning if we would be inserting ourselves after ourselves,
                // this happens when a new window opens above us and we are still the
                // window directly above vivado, in that case we are already correctly placed
                if (insertAfter != this.Handle)
                {
                    SetWindowPos(this.Handle, insertAfter, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE);
                }

                magnifier.UpdateMaginifier();
            }
        }

        // called whenever the target window changes position or size
        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd == notepadHandle)
            {
                UpdateOverlayWindow();
            }
        }

        // reposition the overlay any time the foreground window changes, since
        // z order may have shifted, this keeps the overlay following vivado
        // even when vivado itself is not the active window
        private void ForegroundEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            UpdateOverlayWindow();
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
