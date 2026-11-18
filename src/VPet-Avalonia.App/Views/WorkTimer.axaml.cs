using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Threading;
using VPetAvalonia.Core;
using VPetAvalonia.Abstractions;
using VPetAvalonia.Abstractions.Interfaces;

namespace VPetAvalonia.App.Views
{
    /// <summary>
    /// 工作计时器
    /// 原版只在 MainLogic 的 15 秒 EventTimer 里刷新 UI, 这里用独立的 1 秒定时器,
    /// 进度条看起来是连续的; 完成判定按真实经过时间, 与 work.Time(分钟) 比较
    /// </summary>
    public partial class WorkTimer : UserControl
    {
        private readonly DispatcherTimer _timer;
        private double _workTime = 0;
        private bool _isWorking = false;
        private DateTime? _startTime;

        /// <summary>
        /// 当前进行中的工作, 用于完成结算
        /// </summary>
        private Work? _work;

        public WorkTimer()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTick;
        }

        public double GetCount { get; set; }

        /// <summary>
        /// 是否正在工作
        /// </summary>
        public bool IsWorking => _isWorking;

        /// <summary>
        /// 当前工作
        /// </summary>
        public Work? CurrentWork => _work;

        /// <summary>
        /// 已工作时长(分钟)
        /// </summary>
        public double WorkMinutes => _workTime;

        public void Start(Work work)
        {
            if (work == null)
            {
                Console.WriteLine("[VPet] WorkTimer.Start 收到空工作, 忽略");
                return;
            }

            _work = work;
            ApplyStyle(work);
            Opacity = 1.0;
            _isWorking = true;
            _workTime = 0;
            _startTime = DateTime.Now;
            GetCount = 0;

            tNow.Text = work.Type switch
            {
                Work.WorkType.Study => "正在学习",
                Work.WorkType.Play => "正在玩耍",
                _ => "正在工作",
            };
            tNumberUnit.Text = "分钟";

            PBLeft.Maximum = Math.Max(1, work.Time);
            PBLeft.Value = 0;

            // 工作动画由 MainLogic.StartWork 里的 work.Display(Main) 负责播放。
            // 原版这里写的是 work.Display(m) —— m 是 Main; 移植后本控件不是 IDisplayHost,
            // 传 this 进去只会走 false 分支(静默不播), 所以挪走了。
            UpdateDisplay();
            _timer.Start();
        }

        /// <summary>
        /// 每秒刷新进度; 到点自动完成
        /// </summary>
        private void OnTick(object? sender, EventArgs e)
        {
            if (!_isWorking || _startTime == null)
                return;
            _workTime = (DateTime.Now - _startTime.Value).TotalMinutes;
            UpdateDisplay();
            if (_work != null && _workTime >= _work.Time)
            {
                Stop(reason: FinishWorkInfo.StopReason.TimeFinish);
            }
        }

        /// <summary>当前进度条底色 (自检用: 核对是否真的套用了工作配色)</summary>
        public IBrush? StyledBackground => bdRoot?.Background;

        /// <summary>
        /// 应用工作自带的配色 (原版 Work.SetStyle: 每个工作在 vup.lps 里定义了
        /// BorderBrush/Background/ButtonBackground/ButtonForeground/Foreground)
        /// </summary>
        public void ApplyStyle(Work work)
        {
            var border = ToBrush(work.BorderBrush, "#FF0290D5");
            var back = ToBrush(work.Background, "#FF81D4FA");
            var btn = ToBrush(work.ButtonBackground, "#AA0286C6");
            var btnFg = ToBrush(work.ButtonForeground, "#FFFFFFFF");
            var fg = ToBrush(work.Foreground, "#FF0286C6");

            if (bdRoot != null)
            {
                bdRoot.BorderBrush = border;
                bdRoot.Background = back;
            }
            if (tNow != null) tNow.Foreground = fg;
            if (tNumber != null) tNumber.Foreground = fg;
            if (tNumberUnit != null) tNumberUnit.Foreground = fg;
            if (btnStop != null)
            {
                btnStop.Background = btn;
                btnStop.Foreground = btnFg;
            }
            if (btnSwitch != null)
                btnSwitch.Background = border;
        }

        /// <summary>
        /// lps 里的颜色是 "0290D5" 这种不带 # 的 RRGGBB, 也可能是空串或写错, 一律兜底
        /// </summary>
        private static IBrush ToBrush(string? raw, string fallback)
        {
            var text = (raw ?? "").Trim();
            if (text.Length == 0)
                return new SolidColorBrush(Color.Parse(fallback));
            if (!text.StartsWith("#"))
                text = "#" + text;
            // 不带 alpha 时补 FF, 否则 Color.Parse 会解析失败
            if (text.Length == 7)
                text = "#FF" + text.Substring(1);
            return Color.TryParse(text, out var c)
                ? new SolidColorBrush(c)
                : new SolidColorBrush(Color.Parse(fallback));
        }

        public void Stop(Action? @then = null, FinishWorkInfo.StopReason reason = FinishWorkInfo.StopReason.MenualStop)
        {
            _timer.Stop();
            if (_isWorking)
            {
                // 先复位状态再回调, 避免回调里再次 Stop 造成递归
                var info = new FinishWorkInfo
                {
                    work = _work,
                    count = GetCount,
                    spendtime = _workTime,
                    Reason = reason,
                };
                _work = null;
                _isWorking = false;
                _startTime = null;
                E_FinishWork?.Invoke(info);
            }
            this.Opacity = 0.0;
            UpdateDisplay();
            @then?.Invoke();
        }

        public event Action<FinishWorkInfo>? E_FinishWork;

        public void UpdateWorkTime(double minutes)
        {
            _workTime = minutes;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            tNumber.Text = _workTime.ToString("F1");
            var total = _work?.Time ?? 60;
            PBLeft.Value = total > 0 ? Math.Min(100, _workTime / total * 100) : 0;
        }

        private void btnStop_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Stop(reason: FinishWorkInfo.StopReason.MenualStop);
        }

        private void SwitchState_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_isWorking)
            {
                Stop(reason: FinishWorkInfo.StopReason.MenualStop);
            }
            else if (_work != null)
            {
                Start(_work);
            }
        }
    }
}
