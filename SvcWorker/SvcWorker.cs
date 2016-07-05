using System;
using System.Collections.Concurrent;
using System.Threading;

//using CJF.Utility;

namespace STE.TGL.SIPanel
{
    public class SvcWorker : IDisposable
    {
        #region Consts
        /// <summary>重新連線時間，單位秒</summary>
        const int RETRY_CONNECT = 10;
        /// <summary>設備輪詢時間，單位豪秒</summary>
        const int POOLING_CYCLE = 25;
        /// <summary>空閒時間，時間到時，則顯示跑馬燈，單位分鐘</summary>
        const int IDLE_TIME = 5;
        /// <summary>空閒狀態檢查週期，單位豪秒</summary>
        const int IDLE_CHECK_TIME = 10000;
        /// <summary>設備一DI端點數量</summary>
        const ushort DEVICE_1_INPUT_COUNT = 6;
        /// <summary>設備二DI端點數量</summary>
        const ushort DEVICE_2_INPUT_COUNT = 6;
        /// <summary>設備一DO端點數量</summary>
        const ushort DEVICE_1_COIL_COUNT = 6;
        /// <summary>設備二DO端點數量</summary>
        const ushort DEVICE_2_COIL_COUNT = 6;
        /// <summary>設備詢問週期，單位豪秒</summary>
        const ushort DEVICE_POOLING_TIME = 100;
        /// <summary>長按事件啟動時間，單位秒</summary>
        const ushort LONGPUSH_ACTION_TIME = 3;
        /// <summary>長按逾時時間，單位秒</summary>
        const ushort LONGPUSH_TIMEOUT = 10;
        /// <summary>空閒跑馬燈的速度，單位豪秒，越短越快</summary>
        const ushort LED_MARQUEE_SPEED = 500;
        #endregion

        #region 內部類別變數
        ILog _Logger = Logging.GetLogger(typeof(SvcWorker));
        DateTime _StartupTime;
        Random rndKey = new Random(DateTime.Now.Millisecond);
        ConcurrentDictionary<string, IODevice> _IOs = null;
        ConcurrentDictionary<string, Timer> _IOReconnect = null;
        #endregion

        #region 內部變數
        bool _isExit = false;
        bool _isDisposed = false;
        bool _StopLEDShow = false;
        DateTime _LastButtonAction = DateTime.MaxValue;
        Timer _IdleShowTimer = null;
        bool[] _IdleShowValue = null;
        bool _isShiftLeft = false;
        bool _onMarquee = false;
        bool[] _OriginalCoil = null;
        #endregion

        #region Construct Method : SvcWorker(string[] args)
        public SvcWorker(string[] args)
        {
            _StartupTime = DateTime.Now;
            _IOReconnect = new ConcurrentDictionary<string, Timer>();
        }
        ~SvcWorker() { Dispose(false); }
        #endregion

        #region IDisposable 成員
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region Protected Virtual Method : void Dispose(bool disposing)
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            if (disposing)
            {
                if (_IOReconnect != null)
                    _IOReconnect.Clear();
                _IOReconnect = null;
                if (_IOs != null)
                    _IOs.Clear();
                _IOs = null;
            }
            _isDisposed = true;
        }
        #endregion

        #region Public Method : void Shutdown()
        /// <summary>關閉伺服器</summary>
        public void Shutdown()
        {
            if (_isDisposed || _isExit) return;
            _isExit = true;
            //Console.WriteLine("正在關閉程式...");
            _Logger.Debug("[SYS]MG:正在關閉程式...");
            if (_IdleShowTimer != null)
            {
                _IdleShowTimer.Dispose();
                _IdleShowTimer = null;
            }
            foreach (Timer tmr in _IOReconnect.Values)
            {
                if (tmr != null)
                {
                    tmr.Dispose();
                }
            }
            _IOReconnect.Clear();
            foreach (IODevice io in _IOs.Values)
            {
                io.Dispose();
            }
            _IOs.Clear();
        }
        #endregion

        #region Public Method : void Start()
        /// <summary>開始執行</summary>
        public void Start()
        {
            CreateDevices();
            foreach (IODevice io in _IOs.Values)
            {
                //Console.Write("Initialize device[{0}] ({1}:{2})... ", io.Name, io.RemoteIP, io.RemotePort);
                _Logger.Debug("Initialize device[{0}] ({1}:{2})... ", io.Name, io.RemoteIP, io.RemotePort);

                if (io.Name.Substring(io.Name.Length - 1).Equals("1"))
                {
                    // 第一台
                    io.InitializeDevice(0, DEVICE_1_INPUT_COUNT, 0, DEVICE_1_COIL_COUNT, POOLING_CYCLE);
                    io.SetIOSync(true);
                }
                else
                {
                    // 第二台
                    io.InitializeDevice(0, DEVICE_2_INPUT_COUNT, 0, DEVICE_2_COIL_COUNT, POOLING_CYCLE);
                    io.SetIOSync(false);
                    io.SetIOSync(0, true);
                    io.SetIOSync(1, true);
                    io.SetEnablePushTimeout(false);
                    io.SetEnablePushTimeout(0, true);
                    io.SetEnablePushTimeout(1, true);
                }
                io.SetLongPushTime(LONGPUSH_ACTION_TIME);
                io.LongPushTimeout = LONGPUSH_TIMEOUT;
                _Logger.Debug("Device Initialize Finish...");
                RepoolingThread(new object[] { io, POOLING_CYCLE });
            }
            // ===== For IdleShow =====
            //_IdleShowValue = new bool[] { true, false, false, false, false, false };
            //_LastButtonAction = DateTime.Now;
            //_StopLEDShow = true;
            //_IdleShowTimer = new Timer(CheckIdleTimeThread, null, 0, IDLE_CHECK_TIME);
        }
        #endregion

        #region Private Method : void CreateDevices()
        private void CreateDevices()
        {
            #region Create Device Class
            _IOs = new ConcurrentDictionary<string, IODevice>();
            IODevice io = null;
            string[] devName = { "DM1-1", "DM1-2", "DM2-1", "DM2-2" };
            string[] devIP = { "192.168.127.130", "192.168.127.131", "192.168.127.230", "192.168.127.231" };
            for (int i = 0; i < devName.Length; i++)
            {
                io = new IODevice(devName[i], devIP[i], 502);
                io.OnInputStatusChanged += new EventHandler<InputEventArgs>(Device_OnInputStatusChanged);
                io.OnConnected += new EventHandler(Device_OnConnected);
                io.OnDisconnect += new EventHandler(Device_OnDisconnect);
                io.OnPoolingStart += new EventHandler(Device_OnPoolingStart);
                io.OnPoolingStop += new EventHandler(Device_OnPoolingStop);
                io.OnClick += new EventHandler<InputEventArgs>(Device_OnClick);
                io.OnDoubleClick += new EventHandler<InputEventArgs>(Device_OnDoubleClick);
                io.OnLongPush += new EventHandler<InputEventArgs>(Device_OnLongPush);
                io.OnLongPushTimeout += new EventHandler<InputEventArgs>(Device_OnLongPushTimeout);
                io.OnButtonDown += new EventHandler<InputEventArgs>(Device_OnButtonDown);
                io.OnButtonUp += new EventHandler<InputEventArgs>(Device_OnButtonUp);
                _IOs.TryAdd(devName[i], io);
            }
            #endregion
        }
        #endregion

        #region Private Method : void Device_OnInputStatusChanged(object sender, InputEventArgs e)
        private void Device_OnInputStatusChanged(object sender, InputEventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] DI Value Changed:{1} -> {2}", io.Name, e.Index, e.Status);
        }
        #endregion

        #region Private Method : void Device_OnPoolingStop(object sender, EventArgs e)
        private void Device_OnPoolingStop(object sender, EventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] Stop Pooling...", io.Name);
        }
        #endregion

        #region Private Method : void Device_OnPoolingStart(object sender, EventArgs e)
        private void Device_OnPoolingStart(object sender, EventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] Start Pooling...", io.Name);
        }
        #endregion

        #region Private Method : void Device_OnDisconnect(object sender, EventArgs e)
        private void Device_OnDisconnect(object sender, EventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] Disconnect, Reconnect {1} sec...", io.Name, RETRY_CONNECT);
            _StopLEDShow = true;
            if (_IOReconnect.ContainsKey(io.Name))
            {
                Timer tmr = null;
                _IOReconnect.TryRemove(io.Name, out tmr);
                if (tmr != null)
                {
                    tmr.Dispose();
                    tmr = null;
                }
            }
            _IOReconnect.TryAdd(io.Name, new Timer(RepoolingThread, new object[] { io, POOLING_CYCLE }, RETRY_CONNECT * 1000, 0));
        }
        #endregion

        #region Private Method : void Device_OnConnected(object sender, EventArgs e)
        private void Device_OnConnected(object sender, EventArgs e)
        {
            IODevice io = (IODevice)sender;
            //_Logger.Debug("[{0}] Device Connected...", io.Name);
        }
        #endregion

        #region Private Method : void Device_OnClick(object sender, InputEventArgs e)
        private void Device_OnClick(object sender, InputEventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] Button Clicked:{1}", io.Name, e.Index);
        }
        #endregion

        #region Private Method : void Device_OnDoubleClick(object sender, InputEventArgs e)
        private void Device_OnDoubleClick(object sender, InputEventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] Button Double Clicked:{1}", io.Name, e.Index);
        }
        #endregion

        #region Private Method : void Device_OnLongPush(object sender, InputEventArgs e)
        private void Device_OnLongPush(object sender, InputEventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] Button Long Push:{1} / {2}ms", io.Name, e.Index, e.Time);
        }
        #endregion

        #region Private Method : void Device_OnLongPushTimeout(object sender, InputEventArgs e)
        private void Device_OnLongPushTimeout(object sender, InputEventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] Button Long Push Timeout:{1} / {2}s", io.Name, e.Index, e.Time);
            if (io.PulseControl[e.Index])
                io.PulseControl[e.Index] = false;
            else
            {
                io.SetPulseControl(e.Index, 5, -5);
                if (!io.IsOnPulse)
                    io.StartPulse();
            }
        }
        #endregion

        #region Private Method : void Device_OnButtonDown(object sender, InputEventArgs e)
        private void Device_OnButtonDown(object sender, InputEventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] Pushed Button:{1}", io.Name, e.Index);
            _LastButtonAction = DateTime.Now;
            _StopLEDShow = true;
            if (io.Name.Substring(io.Name.Length - 1).Equals("2"))
            {
                if (e.Index <= 1)
                    return;
                io.ChangeCoilStatus((ushort)(e.Index - 2), true);
            }
        }
        #endregion

        #region Private Method : void Device_OnButtonUp(object sender, InputEventArgs e)
        private void Device_OnButtonUp(object sender, InputEventArgs e)
        {
            IODevice io = (IODevice)sender;
            _Logger.Debug("[{0}] Released Button:{1}", io.Name, e.Index);
            if (io.Name.Substring(io.Name.Length - 1).Equals("2"))
            {
                if (e.Index <= 1)
                    return;
                io.ChangeCoilStatus((ushort)(e.Index - 2), false);
            }
        }
        #endregion

        #region Private Method : void RepoolingThread(object o)
        private void RepoolingThread(object o)
        {
            object[] os = (object[])o;
            IODevice io = (IODevice)os[0];
            int cycle = Convert.ToInt32(os[1]);
            _Logger.Debug("Connect to device[{0}]... ", io.Name);
            io.BeginPooling(cycle);
            if (io.IsConnected && io.IsOnPooling)
            {
                _Logger.Debug("Device[{0}] connect success...", io.Name);
                Timer tmr = null;
                _IOReconnect.TryRemove(io.Name, out tmr);
                if (tmr != null)
                {
                    tmr.Dispose();
                    tmr = null;
                }
            }
            else
            {
                _Logger.Debug("Device[{0}] connect fail, Retry {1} sec...", io.Name, RETRY_CONNECT);
                if (_IOReconnect.ContainsKey(io.Name))
                {
                    Timer tmr = null;
                    _IOReconnect.TryRemove(io.Name, out tmr);
                    if (tmr != null)
                    {
                        tmr.Dispose();
                        tmr = null;
                    }
                }
                _IOReconnect.TryAdd(io.Name, new Timer(RepoolingThread, new object[] { io, POOLING_CYCLE }, RETRY_CONNECT * 1000, 0));
            }
        }
        #endregion

        #region Private Method : void CheckIdleTimeThread(object o)
        private void CheckIdleTimeThread(object o)
        {
            if (_StopLEDShow && DateTime.Now.Subtract(_LastButtonAction).TotalMinutes >= IDLE_TIME)
            {
                _IdleShowValue = new bool[] { false, true, false, false, false, false };
                _isShiftLeft = true;
                _OriginalCoil = new bool[DEVICE_1_COIL_COUNT];
                Array.Copy(_IOs["DM1-1"].CoilStatus, _OriginalCoil, DEVICE_1_COIL_COUNT);
                _StopLEDShow = false;
                _onMarquee = false;
                _IdleShowTimer.Dispose();
                _IdleShowTimer = new Timer(IdleLEDShow, null, 0, LED_MARQUEE_SPEED);
            }
        }
        #endregion

        #region Private Method : void IdleLEDShow(object o)
        private void IdleLEDShow(object o)
        {
            if (_onMarquee) return;
            _onMarquee = true;
            if (!_StopLEDShow)
            {
                if (_isShiftLeft)
                {
                    for (int i = 0; i < DEVICE_1_COIL_COUNT - 1; i++)
                        _IdleShowValue[i] = _IdleShowValue[i + 1];
                    _IdleShowValue[DEVICE_1_COIL_COUNT - 1] = false;
                    _isShiftLeft = !_IdleShowValue[0];
                }
                else
                {
                    for (int i = DEVICE_1_COIL_COUNT - 2; i >= 0; i--)
                        _IdleShowValue[i + 1] = _IdleShowValue[i];
                    _IdleShowValue[0] = false;
                    _isShiftLeft = _IdleShowValue[DEVICE_1_COIL_COUNT - 1];
                }
                _IOs["DM1-1"].ChangeCoilStatus(_IdleShowValue);
            }
            else
            {
                _IdleShowTimer.Dispose();
                _IdleShowTimer = new Timer(CheckIdleTimeThread, null, 0, IDLE_CHECK_TIME);
                _IOs["DM1-1"].ChangeCoilStatus(_OriginalCoil);
            }
            _onMarquee = false;
        }
        #endregion
    }
}
