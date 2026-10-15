using System.Collections.Generic;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **音频层**：把内核的接触事件流（<see cref="PoolSimulator.DrainContactEvents"/>）映射成"撞声"，
    /// 并把出杆（<see cref="PhysicsTestDrive.LaunchCount"/> 的计数增量）映射成 strike。
    ///
    /// ── 分层（与瞄准辅助同范式）────────────────────────────────────────
    /// · **纯逻辑**：<see cref="VolumeForSpeed"/> / <see cref="PitchForSerial"/> / <see cref="ThrottleState"/>
    ///   —— 判据 `T90` 直接驱动；
    /// · **纯逻辑（D15-AU-02 新增）**：<see cref="TierForSpeed"/> / <see cref="LayerCountFor"/> /
    ///   <see cref="LayerLinearGain"/> / <see cref="LayerPitchFactor"/> —— 分层 / 分档 / 退让（规格 §3–§5）；
    /// · 本类 <c>Update</c>：薄胶水 —— 轮询出杆计数、drain 接触事件、映射 + 限流 + 分层 <c>PlayOneShot</c>。
    ///
    /// ── 三条契约 ─────────────────────────────────────────────────────
    ///  1. **关闭即静默且不积压**：<see cref="audioEnabled"/> 为假 ⇒ 不播放，且**仍 drain 并丢弃**积压事件
    ///     （否则重新开启瞬间会炸一耳朵旧事件）；
    ///  2. **strike 每帧至多一次**（计数增量天然有界）；
    ///  3. **限流**：同帧同类 ≤ 3 + 每新帧首个接受须距上次 ≥ 0.03 s（防开球"机关枪"）；
    ///     音高按播放序号三值轮转（**确定性**抖动，不用随机数）。
    ///
    /// ── D15-AU-02：素材归一化后的分层 / 分档 / 退让（规格 `Docs/35-A_音频反馈强化_spec.md` §3–§5）──
    /// · **分层**：strike / hit / rail / pocket 各 2 层（主层 L1 + 尾音层 L2，L2 复用或借用本工程其它 clip）；
    /// · **分档**：<see cref="TierForSpeed"/> 只决定**层数**（音量仍走连续 <see cref="VolumeForSpeed"/>，
    ///   拒绝量化 —— 避免阶跃"拉链噪声"）；
    /// · **退让**：P2 —— 同帧 <c>hit</c>/<c>rail</c> 已接受 ≥2 声 ⇒ 后续同类只发 L1（丢 L2）；
    ///   P3 —— 帧内功率和 ≤ <see cref="FramePowerCapDb"/>，先丢 L2；P4 —— pocket 永不退让；
    /// · **层内功率**：Σvol_i² ≤ 1（<see cref="LayerLinearGain"/> 内含归一化）。
    ///
    /// ── 确定性铁律（本单沿用）─────────────────────────────────────────
    /// **禁止** <c>UnityEngine.Random</c> / <c>System.Random</c> / 时间抖动；音高**只**由
    /// <see cref="PitchForSerial"/> 三值轮转给出；分档**只**由 `rel` 闭式阈值决定。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────
    /// 真实听感（清脆 / 闷 / 轰炸 / 爽感 / 层次）**不被 EditMode 判据覆盖**（批处理无声）——
    /// §3 的层结构基于包络实测**推断**，属**假设**，须由用户耳朵校验；排期验收由用户实机。
    /// </summary>
    [AddComponentMenu("Billiards/Pool Audio")]
    public sealed class PoolAudio : MonoBehaviour
    {
        /// <summary>同帧同类事件最多播放数（开球群碰撞防叠）。</summary>
        public const int MaxSameKindPerFrame = 3;

        /// <summary>同类事件跨帧最小播放间隔（秒）。</summary>
        public const float MinRepeatIntervalSeconds = 0.03f;

        /// <summary>音量调制满档对应的相对速度（m/s，= 出厂初速上界）。</summary>
        public const float FullSpeedForVolume = 4.5f;

        /// <summary>AudioSource 池大小（限流后同时发声数有界）。</summary>
        public const int SourcePoolSize = 6;

        /// <summary>BGM 相对主音量的比例（背景不抢前景撞声）。</summary>
        public const float BgmVolumeRatio = 0.45f;

        // ── D15-AU-02 新增常量（规格 §4 / §5）──────────────────────────────

        /// <summary>力度档阈值：`rel ≥ 1.0` 起进入 T1（规格 §4）。</summary>
        public const float TierT1Speed = 1.0f;

        /// <summary>力度档阈值：`rel ≥ 2.5` 起进入 T2（**层数由 1 → 2**）。</summary>
        public const float TierT2Speed = 2.5f;

        /// <summary>力度档阈值：`rel ≥ 3.5` 起进入 T3（满力）。</summary>
        public const float TierT3Speed = 3.5f;

        /// <summary>帧内功率和上界（dBFS，规格 §5.4）。</summary>
        public const float FramePowerCapDb = -1.0f;

        /// <summary>pocket 尾音层（L2）相对主层的起始延迟（秒，规格 §3.1：+45 ms）。</summary>
        public const float PocketSecondLayerDelaySeconds = 0.045f;

        [Tooltip("被观察的驱动器（出杆计数 + 内核）。为空 ⇒ 本组件不生效。")]
        public PhysicsTestDrive drive;

        [Tooltip("出杆音（strike）。")]
        public AudioClip strikeClip;

        [Tooltip("球-球撞音（hit）。")]
        public AudioClip hitClip;

        [Tooltip("球-库撞音（rail）。")]
        public AudioClip railClip;

        [Tooltip("落袋音（pocket）。")]
        public AudioClip pocketClip;

        [Tooltip("hit 尾音层借用的 UI 脆音（规格 §3.1）。为空 ⇒ 退回 hitClip。")]
        public AudioClip clickClip;

        [Tooltip("pocket 尾音层借用的软 UI 收尾音（规格 §3.1）。为空 ⇒ 退回 pocketClip。")]
        public AudioClip backClip;

        [Tooltip("背景音乐（循环播放，音量取主音量 × 0.45）。")]
        public AudioClip bgmClip;

        /// <summary>音频总开关（设置页预留接口：单字段单读点；关闭 ⇒ 不播放且丢弃积压事件）。</summary>
        [Tooltip("音频总开关。")]
        public bool audioEnabled = true;

        /// <summary>主音量（设置页预留接口）。</summary>
        [Range(0f, 1f)]
        [Tooltip("主音量。")]
        public float masterVolume = 0.8f;

        private readonly List<ContactEvent> _events = new List<ContactEvent>();
        private readonly List<AudioSource> _sources = new List<AudioSource>();
        private readonly List<DelayedLayer> _delayed = new List<DelayedLayer>();
        private readonly ThrottleState _throttle = new ThrottleState();
        private AudioSource _bgmSource;
        private int _lastLaunchCount;
        private int _playSerial;
        private int _sourceCursor;
        private float _framePower;

        /// <summary>帧内功率和的线性上界（= 10^(<see cref="FramePowerCapDb"/>/10)）。</summary>
        private static readonly float FramePowerLinearCap = Mathf.Pow(10f, FramePowerCapDb / 10f);

        /// <summary>逐事件**层相对电平**（dB；索引 0 = L1 主层 = 0 dB）。规格 §3.1。</summary>
        private static readonly float[][] LayerGainDbTable =
        {
            new[] { 0f, -6f },   // Strike：L2 短"闷体"  −6 dB
            new[] { 0f, -5f },   // Hit   ：L2 高频脆点  −5 dB
            new[] { 0f, -4f },   // Rail  ：L2 中频 body −4 dB
            new[] { 0f, -8f },   // Pocket：L2 落网收尾  −8 dB
        };

        private void Start()
        {
            if (bgmClip == null)
            {
                return;
            }

            var go = new GameObject("PoolAudioBgm");
            go.transform.SetParent(transform, false);
            _bgmSource = go.AddComponent<AudioSource>();
            _bgmSource.clip = bgmClip;
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;
            _bgmSource.spatialBlend = 0f;
            _bgmSource.volume = Mathf.Clamp01(BgmVolumeRatio * masterVolume);
            _bgmSource.Play();
        }

        // ── 纯逻辑（判据 T90 直接驱动）──────────────────────────────────────

        /// <summary>相对速度 → 音量 ∈ [0.25, 1]（1:1 线性；轻碰轻响，快撞满响）。</summary>
        public static float VolumeForSpeed(float relativeSpeed, float fullSpeed = FullSpeedForVolume)
        {
            if (!(fullSpeed > 0f))
            {
                return 0.25f;
            }

            return Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(relativeSpeed / fullSpeed));
        }

        /// <summary>播放序号 → 音高（0.94 / 1.00 / 1.06 三值轮转——**确定性抖动**，不用随机数）。</summary>
        public static float PitchForSerial(int serial)
        {
            int phase = ((serial % 3) + 3) % 3;
            return 1f + 0.06f * (phase - 1);
        }

        // ── 纯逻辑（D15-AU-02：分层 / 分档 / 退让；规格 §3–§5）────────────────

        /// <summary>本单新增：分层/分档所针对的**音频事件**（strike 由出杆触发，其余来自接触事件流）。</summary>
        public enum AudioCue
        {
            /// <summary>出杆（<see cref="PhysicsTestDrive.LaunchCount"/> 增量）。</summary>
            Strike = 0,

            /// <summary>球-球（<c>ContactKind.BallBall</c>）。</summary>
            Hit = 1,

            /// <summary>球-库（<c>ContactKind.Cushion</c>）。</summary>
            Rail = 2,

            /// <summary>落袋 / 越喉（<c>ContactKind.Pocket</c>）。</summary>
            Pocket = 3,
        }

        /// <summary>本单新增：力度档（规格 §4）。**只决定层数**，不量化音量。</summary>
        public enum SpeedTier
        {
            /// <summary>轻碰 `rel ∈ [0, 1.0)` —— 1 层。</summary>
            T0 = 0,

            /// <summary>中 `rel ∈ [1.0, 2.5)` —— 1 层。</summary>
            T1 = 1,

            /// <summary>强 `rel ∈ [2.5, 3.5)` —— 2 层。</summary>
            T2 = 2,

            /// <summary>满 `rel ∈ [3.5, ∞)` —— 2 层（`rel &gt; 4.5` 一律按 T3）。</summary>
            T3 = 3,
        }

        /// <summary>相对速度 → 力度档（**闭式阈值，无随机/时间抖动**；规格 §4）。</summary>
        public static SpeedTier TierForSpeed(float relativeSpeed)
        {
            if (relativeSpeed < TierT1Speed)
            {
                return SpeedTier.T0;
            }

            if (relativeSpeed < TierT2Speed)
            {
                return SpeedTier.T1;
            }

            if (relativeSpeed < TierT3Speed)
            {
                return SpeedTier.T2;
            }

            return SpeedTier.T3;
        }

        /// <summary>档位 → 基础层数（T0/T1 = 1；T2/T3 = 2）。规格 §4。</summary>
        public static int BaseLayerCount(SpeedTier tier)
        {
            return (tier == SpeedTier.T2 || tier == SpeedTier.T3) ? 2 : 1;
        }

        /// <summary>
        /// 档位 + 帧内已接受同类声部数 → **实际层数**（规格 §5.3 P2：同帧 `hit`/`rail` 已接受 ≥2 声
        /// ⇒ 后续同类只发 L1、丢 L2）。**strike / pocket 不退让**（P4 / P5）。
        /// </summary>
        public static int LayerCountFor(AudioCue cue, SpeedTier tier, int priorAcceptedSameKind)
        {
            int n = BaseLayerCount(tier);
            if (n < 2)
            {
                return n;
            }

            bool yielding = (cue == AudioCue.Hit || cue == AudioCue.Rail) && priorAcceptedSameKind >= 2;
            return yielding ? 1 : n;
        }

        /// <summary>逐事件、逐层**相对电平**（dB；L1 = 0 dB）。规格 §3.1。</summary>
        public static float LayerGainDb(AudioCue cue, int layer)
        {
            return LayerGainDbTable[(int)cue][layer];
        }

        /// <summary>逐事件、逐层**音高因子**（乘在 <see cref="PitchForSerial"/> 轮转基音之上）。规格 §3.1。</summary>
        public static float LayerPitchFactor(AudioCue cue, int layer)
        {
            switch (cue)
            {
                case AudioCue.Strike:
                    return layer == 0 ? 1.00f : 0.50f;   // L2 降八度 ⇒ 短"闷体"
                case AudioCue.Hit:
                    return layer == 0 ? 1.00f : 1.25f;   // L2 提"清脆"
                case AudioCue.Rail:
                    return layer == 0 ? 0.75f : 1.00f;   // L1 降调低频"闷"，L2 中频 body
                case AudioCue.Pocket:
                    return layer == 0 ? 1.00f : 0.80f;   // L2 软收尾
                default:
                    return 1.00f;
            }
        }

        /// <summary>
        /// 逐层线性增益，**已做层内功率归一化**（Σvol_i² ≤ 1，规格 §3.2 R-L）：
        /// 两层 0/−6 dB ⇒ 功率和 1.25 ⇒ L1 实得 ≈ −0.97 dB（"L1 基础电平比类目标再降约 1 dB"）。
        /// </summary>
        public static float LayerLinearGain(AudioCue cue, int layer, int layerCount)
        {
            float sum = 0f;
            for (int i = 0; i < layerCount; i++)
            {
                float g = Mathf.Pow(10f, LayerGainDb(cue, i) / 20f);
                sum += g * g;
            }

            float scale = sum > 1f ? 1f / Mathf.Sqrt(sum) : 1f;
            return Mathf.Pow(10f, LayerGainDb(cue, layer) / 20f) * scale;
        }

        /// <summary>
        /// **同帧多碰撞限流器**（纯逻辑，判据 `T90` 驱动）：同帧同类 ≤ <see cref="MaxSameKindPerFrame"/>；
        /// **每个新帧的首个**接受还须距上次接受 ≥ <see cref="MinRepeatIntervalSeconds"/>（同帧内豁免间隔检查，
        /// 否则"同帧允许 3 个"会被间隔规则当场否决）。帧边界按调用方传入的 <paramref name="nowSeconds"/>
        /// 变化判定（不读 `Time` ⇒ 可判据直接驱动）。
        /// </summary>
        public sealed class ThrottleState
        {
            private readonly int[] _acceptedThisFrame = new int[3];
            private readonly float[] _lastAcceptedAt = new float[3];
            private float _frameStamp = float.NaN;

            public ThrottleState()
            {
                Reset();
            }

            public bool TryAccept(ContactKind kind, float nowSeconds)
            {
                bool newFrame = float.IsNaN(_frameStamp) || nowSeconds != _frameStamp;
                if (newFrame)
                {
                    _frameStamp = nowSeconds;
                    for (int i = 0; i < _acceptedThisFrame.Length; i++)
                    {
                        _acceptedThisFrame[i] = 0;
                    }
                }

                int k = (int)kind;
                if (_acceptedThisFrame[k] >= MaxSameKindPerFrame)
                {
                    return false;
                }

                if (newFrame
                    && !float.IsNaN(_lastAcceptedAt[k])
                    && nowSeconds - _lastAcceptedAt[k] < MinRepeatIntervalSeconds)
                {
                    return false;
                }

                _acceptedThisFrame[k]++;
                _lastAcceptedAt[k] = nowSeconds;
                return true;
            }

            /// <summary>
            /// 本帧已接受该类的声部数（**只读**；供 P2 层退让判定）。**不改变** <see cref="TryAccept"/>
            /// 的接受/拒绝语义 ⇒ `T90 (c)` 七格期望不变。
            /// </summary>
            public int AcceptedThisFrame(ContactKind kind)
            {
                return _acceptedThisFrame[(int)kind];
            }

            public void Reset()
            {
                _frameStamp = float.NaN;
                for (int i = 0; i < _acceptedThisFrame.Length; i++)
                {
                    _acceptedThisFrame[i] = 0;
                    _lastAcceptedAt[i] = float.NaN;
                }
            }
        }

        // ── 胶水（薄；输入/扬声器不被判据覆盖）────────────────────────────────

        private void Update()
        {
            if (drive == null)
            {
                return;
            }

            PoolSimulator sim = drive.Simulator;

            if (!audioEnabled)
            {
                // 关闭 ⇒ 不播放；同时丢弃积压（否则开启瞬间炸一耳朵旧事件），BGM 静音。
                if (_bgmSource != null && !_bgmSource.mute)
                {
                    _bgmSource.mute = true;
                }

                _events.Clear();
                _delayed.Clear();
                sim?.DrainContactEvents(_events);
                return;
            }

            if (_bgmSource != null && _bgmSource.mute)
            {
                _bgmSource.mute = false;
            }

            float now = Time.unscaledTime;
            _framePower = 0f;

            // 0) 到时的延迟层（pocket 尾音，+45 ms）
            ProcessDelayedLayers(now);

            // 1) 出杆音（轮询计数增量；每帧至多一次；每次事件消耗 1 个播放序号）
            if (drive.LaunchCount != _lastLaunchCount)
            {
                _lastLaunchCount = drive.LaunchCount;
                int serial = _playSerial++;
                SpeedTier tier = TierForSpeed(drive.LastLaunchSpeed);
                PlayLayered(AudioCue.Strike, BaseLayerCount(tier),
                    VolumeForSpeed(drive.LastLaunchSpeed), serial, now);
            }

            // 2) 接触事件流：drain → 限流筛选 → 分档/分层 → 播放
            if (sim == null)
            {
                return;
            }

            _events.Clear();
            if (sim.DrainContactEvents(_events) == 0)
            {
                return;
            }

            for (int i = 0; i < _events.Count; i++)
            {
                ContactEvent contact = _events[i];
                if (!_throttle.TryAccept(contact.kind, now))
                {
                    continue;
                }

                if (!TryCueFor(contact.kind, out AudioCue cue))
                {
                    continue;
                }

                // P2：本声之前，本帧同类已接受数（AcceptedThisFrame 已含本声 ⇒ 减 1）
                int priorAccepted = _throttle.AcceptedThisFrame(contact.kind) - 1;
                SpeedTier tier = TierForSpeed(contact.relativeSpeed);
                int layers = LayerCountFor(cue, tier, priorAccepted);

                int serial = _playSerial++;
                PlayLayered(cue, layers, VolumeForSpeed(contact.relativeSpeed), serial, now);
            }
        }

        private void PlayLayered(AudioCue cue, int layerCount, float speedVolume, int serial, float now)
        {
            for (int layer = 0; layer < layerCount; layer++)
            {
                AudioClip clip = LayerClip(cue, layer);
                if (clip == null)
                {
                    continue;
                }

                float gain = LayerLinearGain(cue, layer, layerCount);
                float volume = Mathf.Clamp01(speedVolume * gain * masterVolume);
                float pitch = Mathf.Clamp(PitchForSerial(serial) * LayerPitchFactor(cue, layer), 0.01f, 3f);

                if (cue == AudioCue.Pocket && layer == 1)
                {
                    // 尾音层延迟触发（确定性固定延迟，不读随机/不抖动）
                    _delayed.Add(new DelayedLayer
                    {
                        clip = clip,
                        volume = volume,
                        pitch = pitch,
                        fireAt = now + PocketSecondLayerDelaySeconds,
                    });
                    continue;
                }

                Emit(cue, clip, volume, pitch, layer);
            }
        }

        private void ProcessDelayedLayers(float now)
        {
            for (int i = _delayed.Count - 1; i >= 0; i--)
            {
                if (now < _delayed[i].fireAt)
                {
                    continue;
                }

                DelayedLayer d = _delayed[i];
                _delayed.RemoveAt(i);
                Emit(AudioCue.Pocket, d.clip, d.volume, d.pitch, 1);
            }
        }

        /// <summary>发一个声部；含 P3 帧内功率守（先丢 L2；pocket 豁免 P4）。</summary>
        private void Emit(AudioCue cue, AudioClip clip, float volume, float pitch, int layer)
        {
            float power = volume * volume;
            if (cue != AudioCue.Pocket && layer > 0 && _framePower + power > FramePowerLinearCap)
            {
                return;   // 帧内功率到顶 ⇒ 丢声部（L1 先于 L2 发声 ⇒ 天然"先丢 L2"）
            }

            AudioSource source = NextSource();
            source.pitch = pitch;
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
            _framePower += power;
        }

        private AudioClip LayerClip(AudioCue cue, int layer)
        {
            if (layer == 0)
            {
                return PrimaryClip(cue);
            }

            switch (cue)
            {
                case AudioCue.Hit:
                    return clickClip != null ? clickClip : hitClip;       // 借用 UI 脆音；缺则退回
                case AudioCue.Pocket:
                    return backClip != null ? backClip : pocketClip;      // 借用软 UI 收尾；缺则退回
                default:
                    return PrimaryClip(cue);                              // strike / rail 复用本 clip
            }
        }

        private AudioClip PrimaryClip(AudioCue cue)
        {
            switch (cue)
            {
                case AudioCue.Strike:
                    return strikeClip;
                case AudioCue.Hit:
                    return hitClip;
                case AudioCue.Rail:
                    return railClip;
                case AudioCue.Pocket:
                    return pocketClip;
                default:
                    return null;
            }
        }

        private static bool TryCueFor(ContactKind kind, out AudioCue cue)
        {
            switch (kind)
            {
                case ContactKind.BallBall:
                    cue = AudioCue.Hit;
                    return true;
                case ContactKind.Cushion:
                    cue = AudioCue.Rail;
                    return true;
                case ContactKind.Pocket:
                    cue = AudioCue.Pocket;
                    return true;
                default:
                    cue = AudioCue.Hit;
                    return false;
            }
        }

        private AudioSource NextSource()
        {
            while (_sources.Count < SourcePoolSize)
            {
                var go = new GameObject("PoolAudioSource_" + _sources.Count);
                go.transform.SetParent(transform, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _sources.Add(source);
            }

            AudioSource chosen = _sources[_sourceCursor];
            _sourceCursor = (_sourceCursor + 1) % _sources.Count;
            return chosen;
        }

        private struct DelayedLayer
        {
            public AudioClip clip;
            public float volume;
            public float pitch;
            public float fireAt;
        }
    }
}
