using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using IoTModbus;
using IoTModbus.Device;

namespace STE.TGL.SIPanel
{
    #region Public Class : IODeviceException
    /// <summary>表示 IODevice 類別執行期間所發生的錯誤。</summary>
    public class IODeviceException : Exception
    {
        /// <summary>初始化 STE.TGL.SIPanel.IODeviceException 類別的新執行個體。</summary>
        public IODeviceException() : base() { }
        /// <summary>使用指定的錯誤訊息，初始化 STE.TGL.SIPanel.IODeviceException 類別的新執行個體。</summary>
        /// <param name="message">描述錯誤的訊息。</param>
        public IODeviceException(string message) : base(message) { }
        /// <summary>使用指定的錯誤訊息和造成這個例外狀況原因的內部例外參考，初始化 STE.TGL.SIPanel.IODeviceException 類別的新執行個體。</summary>
        /// <param name="message">解釋例外狀況原因的錯誤訊息。</param>
        /// <param name="innerException">目前例外狀況原因的例外狀況，如果沒有指定內部例外狀況，則為 Null 參考 (Visual Basic 中的 Nothing)。</param>
        public IODeviceException(string message, Exception innerException) : base(message, innerException) { }
    }
    #endregion

    #region Public Class : InputEventArgs
    /// <summary>Input狀態變更事件傳遞物件</summary>
    public class InputEventArgs : EventArgs
    {
        /// <summary>取得Input索引值</summary>
        public ushort Index { get; private set; }
        /// <summary>取得Input狀態</summary>
        public bool Status { get; private set; }
        /// <summary>取得長按時間，單位豪秒；或取得長按逾時時間，單位秒</summary>
        public ushort Time { get; private set; }
        /// <summary>建立Input狀態變更事件傳遞物件</summary>
        /// <param name="index">Input索引值</param>
        /// <param name="status">狀態值</param>
        public InputEventArgs(ushort index, bool status)
        {
            this.Index = index;
            this.Status = status;
        }
        /// <summary>建立Input狀態變更事件傳遞物件</summary>
        /// <param name="index">Input索引值</param>
        /// <param name="time">長按時間，單位豪秒；或長按逾時時間，單位秒</param>
        public InputEventArgs(ushort index, ushort time)
        {
            this.Index = index;
            this.Time = time;
            this.Status = true;
        }
    }
    #endregion

    #region Public Enum : ButtonActionSimulationType
    /// <summary>按鍵模擬類型</summary>
    public enum ButtonActionSimulationType : ushort
    {
        /// <summary>單擊</summary>
        Click = 0,
        /// <summary>雙擊</summary>
        DoubleClick = 1,
        /// <summary>長按</summary>
        LongPush = 2
    }
    #endregion

    public class IODevice : IDisposable
    {
        #region Consts
        /// <summary>雙擊認定檢查時間，單位豪秒</summary>
        const ushort DOUBLE_CLICK_CHECK_TIME = 1000;
        /// <summary>長按逾時時間，單位秒</summary>
        const ushort LONG_PUSH_TIMEOUT = 30;
        /// <summary>閃爍單位時間，單位豪秒</summary>
        const int PULSE_STEP_TIME = 100;
        #endregion

        #region Public Callback Events
        /// <summary>開始輪詢狀態時產生此事件</summary>
        public event EventHandler OnPoolingStart;
        /// <summary>停止輪詢狀態時產生此事件</summary>
        public event EventHandler OnPoolingStop;
        /// <summary>連線至設備時產生此事件</summary>
        public event EventHandler OnConnected;
        /// <summary>與設備斷線時產生此事件</summary>
        public event EventHandler OnDisconnect;
        /// <summary>設備Input狀態變更時產生此事件</summary>
        public event EventHandler<InputEventArgs> OnInputStatusChanged;
        /// <summary>Input狀態模擬按鍵單擊時產生此事件</summary>
        public event EventHandler<InputEventArgs> OnClick;
        /// <summary>Input狀態模擬按鍵雙擊時產生此事件</summary>
        public event EventHandler<InputEventArgs> OnDoubleClick;
        /// <summary>Input狀態模擬按鍵長按時產生此事件</summary>
        public event EventHandler<InputEventArgs> OnLongPush;
        /// <summary>Input狀態模擬按鍵長按時產生此事件</summary>
        public event EventHandler<InputEventArgs> OnLongPushTimeout;
        /// <summary>Input狀態模擬按鍵按下時產生此事件</summary>
        public event EventHandler<InputEventArgs> OnButtonDown;
        /// <summary>Input狀態模擬按鍵單擊時產生此事件</summary>
        public event EventHandler<InputEventArgs> OnButtonUp;
        #endregion

        #region Private Variables
        ILog _Logger = Logging.GetLogger(typeof(IODevice));
        bool _isDisposed = false;
        bool _onPulse = false;
        bool _OnPooling = false;
        bool _StopPooling = false;

        TcpClient _TcpClient = null;
        ModbusIpMaster _Master = null;

        ReaderWriterLockSlim _Lock = null;
        Timer _PoolingTimer = null;
        DateTime[] _PushDownTime = null;
        DateTime[] _PushUpTime = null;
        ushort[] _PushDownCount = null;
        ushort[] _PushActionID = null;
        ushort[] _LongPushActionTime = null;
        Timer[] _ActionTimer = null;
        Timer _PulseTimer = null;
        short[] _PulseHigh = null;
        short[] _PulseLow = null;
        short[] _PulseStep = null;
        ushort[] _PulseDelay = null;
        #endregion

        #region static extern bool InternetGetConnectedState - UWP 不適用
        //[System.Runtime.InteropServices.DllImport("WININET")]
        //static extern bool InternetGetConnectedState(ref InternetConnectionState lpdwFlags, int dwReserved);
        //enum InternetConnectionState : int
        //{
        //    INTERNET_CONNECTION_MODEM = 0x1,
        //    INTERNET_CONNECTION_LAN = 0x2,
        //    INTERNET_CONNECTION_PROXY = 0x4,
        //    INTERNET_RAS_INSTALLED = 0x10,
        //    INTERNET_CONNECTION_OFFLINE = 0x20,
        //    INTERNET_CONNECTION_CONFIGURED = 0x40
        //}
        #endregion

        #region Public Properties
        #region Property : string Name(R)
        private string _Name = string.Empty;
        /// <summary>取得設備名稱</summary>
        public string Name { get { return _Name; } }
        #endregion

        #region Property : string RemoteIP(R/W)
        private string _RemoteIP = string.Empty;
        /// <summary>設定或取得遠端設備的IP。設定時，可能會造成斷線。</summary>
        public string RemoteIP
        {
            get { return _RemoteIP; }
            set
            {
                if (!_RemoteIP.Equals(value))
                {
                    if (_IsConnected)
                        Disconnect();
                }
                _RemoteIP = value;
            }
        }
        #endregion

        #region Property : int RemotePort(R/W)
        private int _RemotePort = 502;
        /// <summary>設定或取得遠端設備的通訊連接埠號。當設定時，可能會造成斷線。</summary>
        public int RemotePort
        {
            get { return _RemotePort; }
            set
            {
                if (!_RemotePort.Equals(value))
                {
                    if (_IsConnected)
                        Disconnect();
                }
                _RemotePort = value;
            }
        }
        #endregion

        #region Property : ushort InputCount(R/PW)
        private ushort _InputCount = 0;
        /// <summary>取得DI埠數</summary>
        public ushort InputCount
        {
            get { return _InputCount; }
            private set
            {
                _InputCount = value;
                _InputStatus = new bool[_InputCount];
                _PushDownTime = new DateTime[_InputCount];
                _PushUpTime = new DateTime[_InputCount];
                _PushDownCount = new ushort[_InputCount];
                _PushActionID = new ushort[_InputCount];
                _LongPushActionTime = new ushort[_InputCount];
                _ActionTimer = new Timer[_InputCount];
                _EnableDblClick = new bool[_InputCount];
            }
        }
        #endregion

        #region Property : ushort CoilCount(R/PW)
        private ushort _CoilCount = 0;
        /// <summary>取得DO埠數</summary>
        public ushort CoilCount
        {
            get { return _CoilCount; }
            private set
            {
                _CoilCount = value;
                _CoilStatus = new bool[_CoilCount];
                _PulseControl = new bool[_CoilCount];
                _PulseHigh = new short[_CoilCount];
                _PulseLow = new short[_CoilCount];
                _PulseStep = new short[_CoilCount];
                _PulseDelay = new ushort[_CoilCount];
            }
        }
        #endregion

        #region Property : ushort InputAddress(R/W)
        private ushort _InputAddress = 0;
        /// <summary>設定或取得ID位址，於此位址使用FunctionCode:02取得DI狀態值</summary>
        public ushort InputAddress
        {
            get { return _InputAddress; }
            set { _InputAddress = value; }
        }
        #endregion

        #region Property : ushort CoilAddress(R/W)
        private ushort _CoilAddress = 0;
        /// <summary>設定或取得DO的位址，於此位址使用FunctionCode:01取得DO狀態值</summary>
        public ushort CoilAddress
        {
            get { return _CoilAddress; }
            set { _CoilAddress = value; }
        }
        #endregion

        #region Property : bool[] InputStatus(R)
        private bool[] _InputStatus = new bool[] { };
        /// <summary>取得DI的狀態</summary>
        public bool[] InputStatus { get { return _InputStatus; } }
        #endregion

        #region Property : bool[] CoilStatus(R)
        private bool[] _CoilStatus = new bool[] { };
        /// <summary>取得DO狀態</summary>
        public bool[] CoilStatus { get { return _CoilStatus; } }
        #endregion

        #region Property : int PoolingCycle(R/W)
        private int _PoolingCycle = 100;
        /// <summary>設定或取得狀態輪詢時間，單位豪秒(ms)，設定0時，執行一次狀態取得後，則停止輪詢</summary>
        public int PoolingCycle
        {
            get { return _PoolingCycle; }
            set
            {
                if (_PoolingCycle != value && _PoolingTimer != null)
                    _PoolingTimer.Change(value, value);
                _PoolingCycle = value;
            }
        }
        #endregion

        #region Property : bool[] StatusSync(R)
        private bool[] _StatusSync = new bool[] { };
        /// <summary>取得Input與Coil是否設定為同步</summary>
        public bool[] StatusSync
        {
            get { return _StatusSync; }
        }
        #endregion

        #region Property : bool[] EnablePushTimeout(R)
        private bool[] _EnablePushTimeout = new bool[] { };
        /// <summary>取得Input長按逾時是否啟用</summary>
        public bool[] EnablePushTimeout
        {
            get { return _EnablePushTimeout; }
        }
        #endregion

        #region Property : bool IsOnPooling(R)
        private bool _IsOnPooling = false;
        /// <summary>取得目前是否正常態擷取中</summary>
        public bool IsOnPooling { get { return _IsOnPooling; } }
        #endregion

        #region Property : bool IsConnected(R)
        private bool _IsConnected = false;
        /// <summary>取得目前是否正與設備連線中</summary>
        public bool IsConnected { get { return _IsConnected; } }
        #endregion

        #region Property : bool IsOnPulse(R)
        /// <summary>取得目前是否正處於閃爍狀態下</summary>
        public bool IsOnPulse { get { return (_PulseTimer != null); } }
        #endregion

        #region Property : DateTime[] InputLastChanged(R)
        private DateTime[] _InputLastChanged = new DateTime[] { };
        /// <summary>取得最後變更的時間</summary>
        public DateTime[] InputLastChanged { get { return _InputLastChanged; } }
        #endregion

        #region Property : ushort LongPushTimeout(R/W)
        private ushort _LongPushTimeout = LONG_PUSH_TIMEOUT;
        /// <summary>設定或取得長按逾時時間，單位秒，不得為0，且不得大於60秒</summary>
        /// <exception cref="ArgumentOutOfRangeException">長按逾時時間，不得為0，且不得大於60秒</exception>
        public ushort LongPushTimeout
        {
            get { return _LongPushTimeout; }
            set
            {
                if (value <= 0 || value > 60)
                    throw new ArgumentOutOfRangeException();
                _LongPushTimeout = value;
            }
        }
        #endregion

        #region Property : ushort DoubleClickTimeRange(R/W)
        private ushort _DoubleClickTimeRange = DOUBLE_CLICK_CHECK_TIME;
        /// <summary>設定或取得雙擊認定檢查時間，單位豪秒，預設值為1000豪秒，其值必須介於100ms~5000ms間</summary>
        public ushort DoubleClickTimeRange
        {
            get { return _DoubleClickTimeRange; }
            set
            {
                if (value < 100 || value > 5000)
                    throw new ArgumentOutOfRangeException();
                _DoubleClickTimeRange = value;
            }
        }
        #endregion

        #region Property : bool[] PulseControl(R)
        private bool[] _PulseControl = null;
        /// <summary>取得閃爍控制狀態</summary>
        public bool[] PulseControl { get { return _PulseControl; } }
        #endregion

        #region Property : bool[] StatusSync(R)
        private bool[] _EnableDblClick = new bool[] { };
        /// <summary>取得Input是否可使用雙擊模式</summary>
        public bool[] EnableDblClick
        {
            get { return _EnableDblClick; }
        }
        #endregion

        #endregion

        #region Construct Method : IODevice(string name, string devIp, int devPort)
        public IODevice(string name, string devIp, int devPort)
        {
            _Lock = new ReaderWriterLockSlim();
            _Name = name;
            this.RemoteIP = devIp;
            this.RemotePort = devPort;
        }
        ~IODevice() { Dispose(false); }
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
                if (_IsConnected)
                    Disconnect();
                if (_Lock != null)
                {
                    _Lock.Dispose();
                    _Lock = null;
                }
            }
            _isDisposed = true;
        }
        #endregion

        #region Public Method : void InitializeDevice(ushort diAddr, ushort diCount, ushort doAddr, ushort doCount, int poolingCycle)
        /// <summary>初始化設備類別</summary>
        /// <param name="diAddr">Input Status Address</param>
        /// <param name="diCount">Input Status Channels</param>
        /// <param name="doAddr">Coil Status Address</param>
        /// <param name="doCount">Coil Status Channels</param>
        /// <param name="poolingCycle">狀態擷取週期，單位豪秒(ms)</param>
        public void InitializeDevice(ushort diAddr, ushort diCount, ushort doAddr, ushort doCount, int poolingCycle)
        {
            this.InputCount = diCount;
            this.InputAddress = diAddr;
            this.CoilCount = doCount;
            this.CoilAddress = doAddr;
            this.PoolingCycle = poolingCycle;
            int syncCount = Math.Min(diCount, doCount);
            _StatusSync = new bool[syncCount];
            _EnablePushTimeout = new bool[syncCount];
            _InputLastChanged = new DateTime[diCount];
            DateTime dt = DateTime.Now;
            try
            {
                _Lock.EnterUpgradeableReadLock();
                for (int i = 0; i < diCount; i++)
                {
                    _InputLastChanged[i] = dt;
                    _EnableDblClick[i] = true;
                    _EnablePushTimeout[i] = true;
                }
            }
            finally { _Lock.ExitUpgradeableReadLock(); }
        }
        #endregion

        #region Public Method : async Task BeginPooling(int cycle)
        /// <summary>以傳入值開始進行輪詢。</summary>
        /// <param name="cycle">輪詢時間，單位豪秒(ms)。此參數值將不會變更PoolingCycle屬性值</param>
        /// <exception cref="IODeviceException">需先初始化設備</exception>
        public void BeginPooling(int cycle)
        {
            //UWP 不適用
            //if (!CheckInternet())
            //    throw new SocketException(10050);
            if (_InputCount == 0 && _CoilCount == 0)
                throw new IODeviceException("Please initialize device first!!");
            _IsOnPooling = false;
            _StopPooling = false;
            _IsConnected = Connect();
            if (_IsConnected)
            {
                if (_PoolingTimer != null)
                {
                    try { _PoolingTimer.Dispose(); }
                    catch { }
                    _PoolingTimer = null;
                }
                _PoolingTimer = new Timer(PoolingThread, null, 0, cycle);
                _IsOnPooling = (cycle != 0);
                // 產生事件
                try
                {
                    if (this.OnPoolingStart != null)
                    {
                        this.OnPoolingStart.Invoke(this, EventArgs.Empty);
                    }
                }
                catch
                {
                    _Logger.Debug("===== OnPoolingStart Error =====");
                }
            }
        }
        #endregion

        #region Public Method : void ChangeCoilStatus(ushort index, bool value)
        /// <summary>變更單個Coil狀態</summary>
        /// <param name="index">Coil索引值</param>
        /// <param name="value">狀態值</param>
        /// <exception cref="IODeviceException">設備未連線或初始化設備</exception>
        public void ChangeCoilStatus(ushort index, bool value)
        {
            if (_CoilStatus == null)
                throw new IODeviceException("Please initialize device first!!");
            if (!_IsConnected)
                throw new IODeviceException("Device disconnect!!");
            if (index >= _CoilStatus.Length)
                throw new ArgumentOutOfRangeException("index");
            try
            {
                _Lock.EnterUpgradeableReadLock();
                _CoilStatus[index] = value;
            }
            finally { _Lock.ExitUpgradeableReadLock(); }
            try
            {
                _Master.WriteSingleCoil((ushort)(this.CoilAddress + index), value);
            }
            catch (SlaveException se)
            {
                _Logger.Debug("[BUS]MG:FC=({0:X}),Addr={1},EC={2}", se.FunctionCode, se.SlaveAddress, se.SlaveExceptionCode);
            }
            catch (IOException)
            {
                Disconnect();
                // 產生事件
                if (this.OnPoolingStop != null)
                {
                    this.OnPoolingStop.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                _Logger.Error(ex);
                //Console.WriteLine("{0}\n{1}", ex.GetType().ToString(), ex.Message);
            }
        }
        #endregion

        #region Public Method : void ChangeCoilStatus(bool[] values)
        /// <summary>變更多個Coil狀態</summary>
        /// <param name="values">所有的Coil狀態值</param>
        /// <exception cref="IODeviceException">設備未連線或初始化設備</exception>
        public void ChangeCoilStatus(bool[] values)
        {
            if (_CoilStatus == null)
                throw new IODeviceException("Please initialize device first!!");
            if (!_IsConnected)
                throw new IODeviceException("Device disconnect!!");
            if (values.Length > _CoilStatus.Length)
                throw new ArgumentOutOfRangeException("values");
            try
            {
                _Lock.EnterWriteLock();
                Array.Copy(values, _CoilStatus, values.Length);
            }
            finally { _Lock.ExitWriteLock(); }
            try
            {
                _Master.WriteMultipleCoils((ushort)(this.CoilAddress), values);
            }
            catch (SlaveException se)
            {
                _Logger.Debug("[BUS]MG:FC=({0:X}),Addr={1},EC={2}", se.FunctionCode, se.SlaveAddress, se.SlaveExceptionCode);
            }
        }
        #endregion

        #region Public Method : void SetIOSync(bool sync)
        /// <summary>設定所有的Input與Coil同步狀態</summary>
        /// <param name="sync">是否設定同步，True:同步, False:不同步</param>
        /// <exception cref="IODeviceException">未初始化設備</exception>
        public void SetIOSync(bool sync)
        {
            if (_StatusSync == null)
                throw new IODeviceException("Please initialize device first!!");
            for (int i = 0; i < _StatusSync.Length; i++)
                _StatusSync[i] = sync;
        }
        #endregion

        #region Public Method : void SetIOSync(int index, bool sync)
        /// <summary>設定Input與Coil是否同步，即當Input狀態變更時，隨即設定Coil</summary>
        /// <param name="index">Input Status索引值</param>
        /// <param name="sync">是否設定同步，True:同步, False:不同步</param>
        /// <exception cref="IODeviceException">未初始化設備</exception>
        /// <exception cref="ArgumentOutOfRangeException">索引值超出範圍</exception>
        public void SetIOSync(int index, bool sync)
        {
            if (_StatusSync == null)
                throw new IODeviceException("Please initialize device first!!");
            if (index >= _StatusSync.Length)
                throw new ArgumentOutOfRangeException("index");
            if (_StatusSync[index] != sync)
                _StatusSync[index] = sync;
        }
        #endregion

        #region Public Method : void SetEnablePushTimeout(bool enable)
        /// <summary>設定所有的Input是否啟用長按逾時機制</summary>
        /// <param name="enable">是否啟用，True:, False:不同步</param>
        /// <exception cref="IODeviceException">未初始化設備</exception>
        public void SetEnablePushTimeout(bool enable)
        {
            if (_EnablePushTimeout == null)
                throw new IODeviceException("Please initialize device first!!");
            for (int i = 0; i < _StatusSync.Length; i++)
                _EnablePushTimeout[i] = enable;
        }
        #endregion

        #region Public Method : void EnablePushTimeout(int index, bool sync)
        /// <summary>設定單一Input是否啟用長按逾時機制</summary>
        /// <param name="index">Input Status索引值</param>
        /// <param name="enable">是否設定同步，True:同步, False:不同步</param>
        /// <exception cref="IODeviceException">未初始化設備</exception>
        /// <exception cref="ArgumentOutOfRangeException">索引值超出範圍</exception>
        public void SetEnablePushTimeout(int index, bool enable)
        {
            if (_EnablePushTimeout == null)
                throw new IODeviceException("Please initialize device first!!");
            if (index >= _EnablePushTimeout.Length)
                throw new ArgumentOutOfRangeException("index");
            if (_EnablePushTimeout[index] != enable)
                _EnablePushTimeout[index] = enable;
        }
        #endregion

        #region Public Method : void SetDblClickMode(bool sync)
        /// <summary>設定所有的Input是否可雙擊模式</summary>
        /// <param name="sync">是否設定可雙擊，True:是, False:否</param>
        /// <exception cref="IODeviceException">未初始化設備</exception>
        public void SetDblClickMode(bool sync)
        {
            if (_EnableDblClick == null)
                throw new IODeviceException("Please initialize device first!!");
            for (int i = 0; i < _EnableDblClick.Length; i++)
                _EnableDblClick[i] = sync;
        }
        #endregion

        #region Public Method : void SetDblClickMode(int index, bool sync)
        /// <summary>設定單一Input是否可雙擊模式</summary>
        /// <param name="index">Input Status索引值</param>
        /// <param name="sync">是否設定可雙擊，True:是, False:否</param>
        /// <exception cref="IODeviceException">未初始化設備</exception>
        /// <exception cref="ArgumentOutOfRangeException">索引值超出範圍</exception>
        public void SetDblClickMode(int index, bool sync)
        {
            if (_EnableDblClick == null)
                throw new IODeviceException("Please initialize device first!!");
            if (index >= _EnableDblClick.Length)
                throw new ArgumentOutOfRangeException("index");
            if (_EnableDblClick[index] != sync)
                _EnableDblClick[index] = sync;
        }
        #endregion

        #region Public Method : void SetLongPushTime(ushort ms)
        /// <summary>設定所有的Input模擬按鈕長按的時間，單位秒</summary>
        /// <param name="sec">長按事件回呼的時間，單位秒，最短3秒，最長不得大於60秒</param>
        /// <exception cref="ArgumentOutOfRangeException">長按時間過短，最短3秒，最長不得大於60秒</exception>
        /// <exception cref="IODeviceException">未初始化設備</exception>
        public void SetLongPushTime(ushort sec)
        {
            if (_LongPushActionTime == null)
                throw new IODeviceException("Please initialize device first!!");
            if (sec < 3 || sec > 60)
                throw new ArgumentOutOfRangeException("sec");
            for (int i = 0; i < _LongPushActionTime.Length; i++)
                _LongPushActionTime[i] = (ushort)(sec * 1000);
        }
        #endregion

        #region Public Method : void SetLongPushTime(int index, ushort ms)
        /// <summary>設定單一Input模擬按鈕長按的時間</summary>
        /// <param name="index">Input Status索引值</param>
        /// <param name="ms">長按事件回呼的時間，單位豪秒，最短100豪秒</param>
        /// <exception cref="IODeviceException">未初始化設備</exception>
        /// <exception cref="ArgumentOutOfRangeException">索引值或回呼時間超出範圍</exception>
        public void SetLongPushTime(int index, ushort ms)
        {
            if (_LongPushActionTime == null)
                throw new IODeviceException("Please initialize device first!!");
            if (index >= _LongPushActionTime.Length)
                throw new ArgumentOutOfRangeException("index");
            if (ms < 100)
                throw new ArgumentOutOfRangeException("ms");
            if (_LongPushActionTime[index] != ms)
                _LongPushActionTime[index] = ms;
        }
        #endregion

        #region Public Method : void SetPulseControl(int index, short pulseUnits1, short pulseUnits2, ushort sleep = 0)
        /// <summary>設定Coil為閃爍狀態，以兩段閃爍單位值呈現閃爍狀態，每單位為100ms</summary>
        /// <param name="index">Coil 索引值</param>
        /// <param name="pulseUnits1">第一段單位值，以正值表示On；負值表示Off</param>
        /// <param name="pulseUnits2">第二段單位值，以正值表示On；負值表示Off</param>
        /// <param name="delay">先暫停幾個單位後再執行，預設值為0</param>
        /// <exception cref="IODeviceException">未初始化設備</exception>
        /// <exception cref="ArgumentOutOfRangeException">索引值或回呼時間超出範圍</exception>
        /// <exception cref="ArgumentException">參數輸入錯誤，兩段單位值不得為0，且必須一個為正值一個為負值</exception>
        public void SetPulseControl(int index, short pulseUnits1, short pulseUnits2, ushort delay = 0)
        {
            if (_PulseControl == null)
                throw new IODeviceException("Please initialize device first!!");
            if (index > _PulseControl.Length)
                throw new ArgumentOutOfRangeException("index");
            if (pulseUnits1 == 0 || pulseUnits2 == 0 || pulseUnits1 > 0 && pulseUnits2 > 0 || pulseUnits1 < 0 && pulseUnits2 < 0)
                throw new ArgumentException("兩段Step值不得為0，且必須一個為正值一個為負值");
            _PulseControl[index] = false;
            _PulseHigh[index] = pulseUnits1;
            _PulseLow[index] = pulseUnits2;
            _PulseDelay[index] = delay;
            _PulseControl[index] = true;
        }
        #endregion

        #region Public Method : void StartPulse()
        /// <summary>開始閃爍，需先設定閃爍方式，否則將不會啟動</summary>
        /// <exception cref="IODeviceException">設備未連線或初始化設備</exception>
        /// <exception cref="ArgumentOutOfRangeException">索引值或回呼時間超出範圍</exception>
        public void StartPulse()
        {
            if (_PulseControl == null)
                throw new IODeviceException("Please initialize device first!!");
            if (!_IsConnected)
                throw new IODeviceException("Device disconnect!!");
            if (_PulseTimer != null)
            {
                _PulseTimer.Dispose();
                _PulseTimer = null;
            }
            for (int i = 0; i < _PulseStep.Length; i++)
            {
                if (!_PulseControl[i]) continue;
                if (_PulseDelay[i] != 0)
                {
                    if (_PulseHigh[i] > _PulseLow[i])
                        _PulseStep[i] = (short)(_PulseHigh[i] + _PulseDelay[i]);    // pulseStep1為正值，即先亮再滅
                    else if (_PulseHigh[i] < _PulseLow[i])
                        _PulseStep[i] = (short)(_PulseLow[i] - _PulseDelay[i]);     // pulseStep1為負值，即先亮再滅
                }
                else
                    _PulseStep[i] = 0;
            }
            _PulseTimer = new Timer(PulseCoilThread, null, 0, PULSE_STEP_TIME);
        }
        #endregion

        #region Public Method : void PausePulse()
        /// <summary>暫停閃爍</summary>
        /// <exception cref="IODeviceException">設備未連線或初始化設備</exception>
        public void PausePulse()
        {
            if (_PulseControl == null)
                throw new IODeviceException("Please initialize device first!!");
            if (!_IsConnected)
                throw new IODeviceException("Device disconnect!!");
            if (_PulseTimer != null)
            {
                _PulseTimer.Dispose();
                _PulseTimer = null;
            }
        }
        #endregion

        #region Public Method : void ResumePulse()
        /// <summary>繼續閃爍</summary>
        /// <exception cref="IODeviceException">設備未連線或初始化設備</exception>
        public void ResumePulse()
        {
            if (_PulseControl == null)
                throw new IODeviceException("Please initialize device first!!");
            if (!_IsConnected)
                throw new IODeviceException("Device disconnect!!");
            if (_PulseTimer != null)
            {
                _PulseTimer.Dispose();
                _PulseTimer = null;
            }
            _PulseTimer = new Timer(PulseCoilThread, null, 0, PULSE_STEP_TIME);
        }
        #endregion

        #region Public Method : void StopPulse()
        /// <summary>停止閃爍</summary>
        /// <exception cref="IODeviceException">設備未連線或初始化設備</exception>
        /// <exception cref="ArgumentOutOfRangeException">索引值或回呼時間超出範圍</exception>
        public void StopPulse()
        {
            if (_PulseControl == null)
                throw new IODeviceException("Please initialize device first!!");
            if (!_IsConnected)
                throw new IODeviceException("Device disconnect!!");
            if (_PulseTimer != null)
            {
                _PulseTimer.Dispose();
                _PulseTimer = null;
            }
            for (int i = 0; i < _PulseStep.Length; i++)
                _PulseStep[i] = 0;
        }
        #endregion

        #region Public Method : void ClearPulse()
        /// <summary>清除閃爍控制</summary>
        /// <exception cref="IODeviceException">設備未連線或初始化設備</exception>
        /// <exception cref="ArgumentOutOfRangeException">索引值或回呼時間超出範圍</exception>
        public void ClearPulse()
        {
            if (_PulseControl == null)
                throw new IODeviceException("Please initialize device first!!");
            if (!_IsConnected)
                throw new IODeviceException("Device disconnect!!");
            if (_PulseTimer != null)
            {
                _PulseTimer.Dispose();
                _PulseTimer = null;
            }
            for (int i = 0; i < _PulseStep.Length; i++)
            {
                _PulseControl[i] = false;
                _PulseDelay[i] = 0;
                _PulseHigh[i] = 0;
                _PulseLow[i] = 0;
                _PulseStep[i] = 0;
            }
        }
        #endregion

        #region Private Method : bool CheckInternet() - UWP 不適用
        ///// <summary>檢查有網路可供連線</summary>
        ///// <returns></returns>
        //private bool CheckInternet()
        //{
        //    // http://msdn.microsoft.com/en-us/library/windows/desktop/aa384702(v=vs.85).aspx
        //    InternetConnectionState flag = InternetConnectionState.INTERNET_CONNECTION_LAN;
        //    return InternetGetConnectedState(ref flag, 0);
        //}
        #endregion

        #region Private Method : bool Connect()
        private bool Connect()
        {
            if (_Master != null)
            {
                _Master.Dispose();
                _Master = null;
            }
            if (_TcpClient != null)
            {
                _TcpClient.Dispose();
                _TcpClient = null;
            }
            _Logger.Debug("[BUS]MG:Connect to device[{0}]({1}:{2})", _Name, _RemoteIP, _RemotePort);
            // UWP 不適用
            //if (CheckInternet())
            //{
            try
            {
                _TcpClient = new TcpClient();
                //IAsyncResult asyncResult = _TcpClient.BeginConnect(_RemoteIP, _RemotePort, null, null);
                //asyncResult.AsyncWaitHandle.WaitOne(3000, true); //wait for 3 sec
                Task tk = _TcpClient.ConnectAsync(_RemoteIP, _RemotePort);
                if (!tk.Wait(3000) || !_TcpClient.Client.Connected)
                    return false;
            }
            catch { return false; }
            try
            {
                if (_TcpClient != null && !_TcpClient.Connected)
                {
                    _TcpClient.Dispose();
                    _TcpClient = null;
                    _Logger.Debug("[BUS]MG:Can't connect to device[{0}]({1}:{2})", _Name, _RemoteIP, _RemotePort);
                    return false;
                }

                if (_TcpClient != null && _TcpClient.Connected)
                {
                    // Create Modbus TCP Master by the _TcpClient
                    _Master = ModbusIpMaster.CreateIp(_TcpClient);
                    _Master.Transport.Retries = 0; // 不需要重試
                    _Master.Transport.ReadTimeout = 1500;
                    _Logger.Debug("[BUS]MG:Device[{0}] connected...", _Name);
                    // 產生事件
                    if (this.OnConnected != null)
                    {
                        this.OnConnected.Invoke(this, EventArgs.Empty);
                    }
                    return true;
                }
                else
                {
                    _Logger.Debug("[BUS]MG:Can't connect to device[{0}]({1}:{2})", _Name, _RemoteIP, _RemotePort);
                    return false;
                }
            }
            catch (IOException ex)
            {
                _Logger.Debug("[BUS]MG:Can't connect to device[{0}]({1}:{2})", _Name, _RemoteIP, _RemotePort);
                _Logger.Debug("[BUS]MG:{0}", ex.Message);
                return false;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _Logger.Debug(ex.Message);
                return false;
            }
            // UWP 不適用
            //}
            //else
            //{
            //    _Logger.Debug("[SYS]EX:Network fail!!");
            //    return false;
            //}
        }
        #endregion

        #region Private Method : void Disconnect()
        private void Disconnect()
        {
            try
            {
                if (_PoolingTimer != null)
                {
                    _StopPooling = true;
                    _PoolingTimer.Dispose();
                    _PoolingTimer = null;
                }
                if (_Master != null)
                    _Master.Dispose();
                _Master = null;
                if (_TcpClient != null)
                {
                    if (_TcpClient.Connected)
                        _TcpClient.Dispose();
                    _TcpClient = null;
                }
                // 產生事件
                if (this.OnDisconnect != null)
                {
                    this.OnDisconnect.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                _Logger.Error(ex);
            }
            finally
            {
                _IsConnected = false;
                _IsOnPooling = false;
            }
        }
        #endregion

        #region Private Method : void PoolingThread(object o)
        private void PoolingThread(object o)
        {
            if (_OnPooling || _StopPooling) return;
            try
            {
                _OnPooling = true;
                bool[] bs = null;

                #region 讀取 Input Status
                try
                {
                    bs = _Master.ReadInputs(_InputAddress, _InputCount);
                }
                catch (SlaveException se)
                {
                    _Logger.Debug("[BUS]MG:FC=({0:X}),Addr={1},EC={2}", se.FunctionCode, se.SlaveAddress, se.SlaveExceptionCode);
                    //Console.WriteLine("[BUS]MG:FC=({0:X}),Addr={1},EC={2}", se.FunctionCode, se.SlaveAddress, se.SlaveExceptionCode);
                }
                catch (IOException)
                {
                    Disconnect();
                    // 產生事件
                    if (this.OnPoolingStop != null)
                    {
                        this.OnPoolingStop.Invoke(this, EventArgs.Empty);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    _Logger.Error(ex);
                    //Console.WriteLine("{0}\n{1}", ex.GetType().ToString(), ex.Message);
                }
                #endregion

                bool isPushDown = false;
                for (ushort i = 0; i < _InputCount; i++)
                {
                    if (_StopPooling) break;
                    if (bs[i] != _InputStatus[i])
                    {
                        #region 本次狀態與上次狀態不同
                        isPushDown = (bs[i] && !_InputStatus[i]);
                        try
                        {
                            _Lock.EnterUpgradeableReadLock();
                            _InputStatus[i] = bs[i];
                            _InputLastChanged[i] = DateTime.Now;
                        }
                        finally { _Lock.ExitUpgradeableReadLock(); }

                        // 同步到輸出
                        if (_StatusSync[i])
                            ChangeCoilStatus(i, bs[i]);

                        #region 產生 OnInputStatusChanged 事件
                        if (this.OnInputStatusChanged != null)
                        {
                            this.OnInputStatusChanged.Invoke(this, new InputEventArgs(i, bs[i]));
                        }
                        #endregion

                        if (isPushDown)
                        {
                            #region Input 自 0 變 1 時，等同於按下按鍵

                            #region 產生 OnButtonDown 事件
                            if (this.OnButtonDown != null)
                            {
                                this.OnButtonDown.Invoke(this, new InputEventArgs(i, true));
                            }
                            #endregion

                            if (_ActionTimer[i] == null)
                            {
                                // 按第一次
                                _PushDownTime[i] = DateTime.Now;
                                _ActionTimer[i] = new Timer(ClickCheckThread, new object[] { i, DateTime.Now }, _DoubleClickTimeRange / 2, 0);
                            }
                            else
                            {
                                // 已按過一次
                                if (DateTime.Now.Subtract(_PushDownTime[i]).TotalMilliseconds <= _DoubleClickTimeRange)
                                {
                                    // 在雙擊時間範圍內再按一次
                                    try { _ActionTimer[i].Dispose(); }
                                    finally { _ActionTimer[i] = null; }

                                    #region 產生 OnDoubleClick 事件
                                    if (this.OnDoubleClick != null)
                                    {
                                        this.OnDoubleClick.Invoke(this, new InputEventArgs(i, true));
                                    }
                                    #endregion
                                }
                            }
                            #endregion
                        }
                        else
                        {
                            #region Input 自 1 變 0 時，等同於放開按鍵

                            #region 產生 OnButtonUp 事件
                            if (this.OnButtonUp != null)
                            {
                                this.OnButtonUp.Invoke(this, new InputEventArgs(i, false));
                            }
                            #endregion

                            double dnTime = DateTime.Now.Subtract(_PushDownTime[i]).TotalMilliseconds;
                            if (_ActionTimer[i] != null)
                            {
                                // 第一次按下
                                try { _ActionTimer[i].Dispose(); }
                                finally { _ActionTimer[i] = null; }
                                bool trigerClick = false;
                                if (!_EnableDblClick[i])
                                {
                                    // 關閉雙擊模式
                                    trigerClick = true;
                                }
                                else if (dnTime < _DoubleClickTimeRange)
                                {
                                    // DoubleClickTimeRange 檢查時間前放開，隨即以 DoubleClickTimeRange / 4 的時間檢查是否為雙擊
                                    _PushUpTime[i] = DateTime.Now;
                                    _ActionTimer[i] = new Timer(DoubleClickTimeroutThread, new object[] { i, DateTime.Now }, _DoubleClickTimeRange / 4, 0);
                                }
                                else if (dnTime > _LongPushActionTime[i])
                                {
                                    // 超過長按時間但未超過長按逾時時間放開
                                    #region 產生 OnLongPush 事件
                                    if (this.OnLongPush != null)
                                    {
                                        this.OnLongPush.Invoke(this, new InputEventArgs(i, (ushort)dnTime));
                                    }
                                    #endregion
                                }
                                else
                                {
                                    // 超過 this.DoubleClickTimeRange 檢查時間放開
                                    trigerClick = true;
                                }
                                if (trigerClick)
                                {
                                    #region 產生 OnClick 事件
                                    if (this.OnClick != null)
                                    {
                                        this.OnClick.Invoke(this, new InputEventArgs(i, true));
                                    }
                                    #endregion
                                }
                            }
                            #endregion
                        }
                        #endregion
                    }
                }
                bs = null;
                try
                {
                    bs = _Master.ReadCoils(_CoilAddress, _CoilCount);
                    Array.Copy(bs, _CoilStatus, bs.Length);
                }
                catch (SlaveException se)
                {
                    _Logger.Debug("[BUS]MG:FC=({0:X}),Addr={1},EC={2}", se.FunctionCode, se.SlaveAddress, se.SlaveExceptionCode);
                    //Console.WriteLine("[BUS]MG:FC=({0:X}),Addr={1},EC={2}", se.FunctionCode, se.SlaveAddress, se.SlaveExceptionCode);
                }
                catch (IOException)
                {
                    Disconnect();
                    // 產生事件
                    if (this.OnPoolingStop != null)
                    {
                        this.OnPoolingStop.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (Exception ex)
                {
                    _Logger.Error(ex);
                    //Console.WriteLine("{0}\n{1}", ex.GetType().ToString(), ex.Message);
                }
            }
            catch (SlaveException se)
            {
                _Logger.Debug("[BUS]MG:FC=({0:X}),Addr={1},EC={2}", se.FunctionCode, se.SlaveAddress, se.SlaveExceptionCode);
                //Console.WriteLine("[BUS]MG:FC=({0:X}),Addr={1},EC={2}", se.FunctionCode, se.SlaveAddress, se.SlaveExceptionCode);
            }
            catch (Exception ex)
            {
                _Logger.Error(ex);
                //Console.WriteLine("{0}\n{1}", ex.GetType().ToString(), ex.Message);
            }
            finally { _OnPooling = false; }
        }
        #endregion

        #region Private Method : void ClickCheckThread(object o)
        /// <summary>檢查 Click 狀態，在此判斷長按或單擊</summary>
        /// <param name="o">以 Input 索引值與按下的時間所組成的陣列</param>
        private void ClickCheckThread(object o)
        {
            object[] os = (object[])o;
            ushort index = Convert.ToUInt16(os[0]);
            try { _ActionTimer[index].Dispose(); }
            finally { _ActionTimer[index] = null; }

            if (_EnablePushTimeout[index] && _InputStatus[index])
            {
                // 持續按著，進入長按逾時模式
                _ActionTimer[index] = new Timer(LongPushTimeoutThread, o, _LongPushTimeout * 1000, 0);
            }
            else
            {
                // 已放開
                #region 產生 OnClick 事件
                if (this.OnClick != null)
                {
                    this.OnClick.Invoke(this, new InputEventArgs(index, true));
                }
                #endregion
            }
        }
        #endregion

        #region Private Method : void LongPushTimeoutThread(object o)
        /// <summary>檢查長按逾時的計時執行緒</summary>
        /// <param name="o">以 Input 索引值與按下的時間所組成的陣列</param>
        private void LongPushTimeoutThread(object o)
        {
            object[] os = (object[])o;
            ushort index = Convert.ToUInt16(os[0]);
            DateTime dt = Convert.ToDateTime(os[1]);
            try { _ActionTimer[index].Dispose(); }
            finally { _ActionTimer[index] = null; }

            #region 產生 OnLongPushTimeout 事件
            if (this.OnLongPushTimeout != null)
            {
                this.OnLongPushTimeout.Invoke(this, new InputEventArgs(index, _LongPushTimeout));
            }
            #endregion
        }
        #endregion

        #region Private Method : void DoubleClickTimeroutThread(object o)
        /// <summary>等待第二次按下的時間，如此計時執行緒啟動，則表示第二次超過 DoubleClickTimeRange 屬性值 1/4 的時間</summary>
        /// <param name="o">以 Input 索引值與按下的時間所組成的陣列</param>
        private void DoubleClickTimeroutThread(object o)
        {
            object[] os = (object[])o;
            ushort index = Convert.ToUInt16(os[0]);
            DateTime dt = Convert.ToDateTime(os[1]);
            try { _ActionTimer[index].Dispose(); }
            finally { _ActionTimer[index] = null; }

            #region 產生 OnClick 事件
            if (this.OnClick != null)
            {
                this.OnClick.Invoke(this, new InputEventArgs(index, true));
            }
            #endregion
        }
        #endregion

        #region Private Method : void PulseCoilThread(object o)
        /// <summary>閃爍計時器執行緒</summary>
        /// <param name="o">null</param>
        private void PulseCoilThread(object o)
        {
            if (_onPulse) return;
            _onPulse = true;
            bool hasChange = false;
            try
            {
                _Lock.EnterUpgradeableReadLock();
                for (int i = 0; i < _CoilCount; i++)
                {
                    if (!_PulseControl[i]) continue;
                    if (_PulseHigh[i] > _PulseLow[i])
                    {
                        // 先亮再滅
                        _PulseStep[i]--;
                        if (_PulseStep[i] < _PulseLow[i])
                            _PulseStep[i] = _PulseHigh[i];
                        _CoilStatus[i] = (_PulseStep[i] >= 0);
                        hasChange |= true;
                    }
                    else
                    {
                        _PulseStep[i]++;
                        if (_PulseStep[i] > _PulseLow[i])
                            _PulseStep[i] = _PulseHigh[i];
                        _CoilStatus[i] = (_PulseStep[i] > 0);
                        hasChange |= true;
                    }
                }
            }
            finally { _Lock.ExitUpgradeableReadLock(); }
            if (hasChange)
                ChangeCoilStatus(_CoilStatus);
            _onPulse = false;
        }
        #endregion
    }
}
